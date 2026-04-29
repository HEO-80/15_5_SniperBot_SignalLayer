using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _15_5_SniperBot_SignalLayer.Models;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class WssConnectionService
    {
        private readonly string _wssUrl;
        private readonly SignalEngine _signalEngine;

        // Aerodrome V2 Basic — topic0 evento Swap
        // Swap(address sender, address to, uint256 amount0In, uint256 amount1In, uint256 amount0Out, uint256 amount1Out)
        private const string TOPIC0_SWAP_V2 = "0xd78ad95fa46c994b6551d0da85fc275fe613ce37657fb8d5e3d130840159d822";

        // WETH en Base
        private const string WETH_BASE = "0x4200000000000000000000000000000000000006";

        private static readonly HashSet<string> _blacklistedWallets = new(StringComparer.OrdinalIgnoreCase)
{
    "0x4752ba5dbc23f44d87826276bf6fd6b1c372ad24",
    "0x2aa7d880b7ad5964c02b919074fb27a71a7ddd07",
    "0x8f10b468b06c6fd214b65f87778827f7d113f996",
    "0x7747f8d2a76bd6345cc29622a946a929647f2359",
    "0x0ca4dbb82d67c51760ae608e4efb6369d31ed272",
    "0x83d55acdc72027ed339d267eebaf9a41e47490d5",
    "0x0bc9a56c3e2ff5b5a99f44534e1f9e98e0b6e7d1",
    "0x0403795ead3079acfdd7d8d46cd2f134371e64e2",
    "0x4a012af2b05616fb390ed32452641c3f04633bb5",
};
        public WssConnectionService(string wssUrl, SignalEngine signalEngine)
        {
            _wssUrl = wssUrl;
            _signalEngine = signalEngine;
        }

        public async Task StartListeningAsync(CancellationToken ct = default)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ConnectAndListenAsync(ct);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WSS] Conexión perdida: {ex.Message} — reconectando en 5s...");
                    await Task.Delay(5000, ct);
                }
            }
        }

        private async Task ConnectAndListenAsync(CancellationToken ct)
        {
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(_wssUrl), ct);
            Logger.Success("🟢 WSS conectado a Base Mainnet");

            // Suscribirse a TODOS los logs sin filtro server-side
            var subscribeMsg = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "eth_subscribe",
                @params = new object[] { "logs", new { } }
            });

            var bytes = Encoding.UTF8.GetBytes(subscribeMsg);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            Logger.Info("[WSS] Suscripción enviada — escuchando logs de Base Mainnet...");

            var buffer = new byte[8192];

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;

                do
                {
                    result = await ws.ReceiveAsync(buffer, ct);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                var raw = sb.ToString();
                if (string.IsNullOrWhiteSpace(raw)) continue;

                ProcessMessage(raw);
            }
        }

        private void ProcessMessage(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (!root.TryGetProperty("params", out var paramsEl)) return;
                if (!paramsEl.TryGetProperty("result", out var logEl)) return;

                if (!logEl.TryGetProperty("address", out var addrEl)) return;
                if (!logEl.TryGetProperty("topics", out var topicsEl)) return;

                var address = addrEl.GetString()?.ToLower() ?? "";
                var topics = new List<string>();

                foreach (var t in topicsEl.EnumerateArray())
                    topics.Add(t.GetString()?.ToLower() ?? "");

                if (topics.Count == 0) return;

                Logger.Raw($"[WSS-RAW] address={address} | topic0={topics[0]}");

                // Filtro: solo Swap de Aerodrome V2 Basic
                if (topics[0] != TOPIC0_SWAP_V2) return;

                var dataStr = logEl.TryGetProperty("data", out var dataEl)
                    ? dataEl.GetString() ?? ""
                    : "";

                var sender = topics.Count > 1
                    ? "0x" + topics[1].Replace("0x", "").TrimStart('0').PadLeft(40, '0')
                    : "unknown";

                var poolAddress = address;

                // Parseo correcto Aerodrome V2 Basic:
                // data = amount0In (32) + amount1In (32) + amount0Out (32) + amount1Out (32)
                decimal amount0In = 0, amount1In = 0, amount0Out = 0, amount1Out = 0;
                if (dataStr.Length >= 258)
                {
                    var clean = dataStr.StartsWith("0x") ? dataStr[2..] : dataStr;
                    amount0In = ParseHexToDecimal(clean.Substring(0, 64));
                    amount1In = ParseHexToDecimal(clean.Substring(64, 64));
                    amount0Out = ParseHexToDecimal(clean.Substring(128, 64));
                    amount1Out = ParseHexToDecimal(clean.Substring(192, 64));
                }

                // Buy  = entra token1 (WETH), sale token0 (el token nuevo)
                // Sell = entra token0 (el token nuevo), sale token1 (WETH)
                bool isBuy = amount1In > 0 && amount0In == 0;

                var swapEvent = new SwapEvent
                {
                    PoolAddress = poolAddress,
                    TokenAddress = poolAddress,
                    WalletSender = sender,
                    AmountIn = isBuy ? amount1In : amount0In,
                    AmountOut = isBuy ? amount0Out : amount1Out,
                    IsBuy = isBuy,
                    Timestamp = DateTime.UtcNow
                };

                // Logger.Debug($"[SWAP] pool={poolAddress[..10]}... | " +
                //              $"{(isBuy ? "BUY " : "SELL")} | " +
                //              $"in={swapEvent.AmountIn:F4} | out={swapEvent.AmountOut:F4} | " +
                //              $"sender={sender[..10]}...");

                // Filtrar wallets de arbitraje ANTES de loguear y de enviar al Signal Engine
                if (_blacklistedWallets.Contains(sender))
                {
                    Logger.Raw($"[BLACKLIST] wallet={sender[..10]}... ignorada");
                    return;
                }

                Logger.Debug($"[SWAP] pool={poolAddress[..10]}... | " +
                             $"{(isBuy ? "BUY " : "SELL")} | " +
                             $"in={swapEvent.AmountIn:F4} | out={swapEvent.AmountOut:F4} | " +
                             $"sender={sender}"); // <-- sin truncar


                _signalEngine.AddSwap(swapEvent);
            }
            catch (Exception ex)
            {
                Logger.Debug($"[WSS] Error procesando mensaje: {ex.Message}");
            }
        }

        private static decimal ParseHexToDecimal(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return 0;
                var big = System.Numerics.BigInteger.Parse(
                    "0" + hex,
                    System.Globalization.NumberStyles.HexNumber);
                return (decimal)big / 1_000_000_000_000_000_000m;
            }
            catch { return 0; }
        }
    }
}