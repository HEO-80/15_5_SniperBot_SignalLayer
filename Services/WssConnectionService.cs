using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _15_5_SniperBot_SignalLayer.Models;
using _15_5_SniperBot_SignalLayer.Services;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class WssConnectionService
    {
        private readonly string _wssUrl;
        private readonly SignalEngine _signalEngine;

        // Aerodrome V2 Router en Base
        private const string AERODROME_ROUTER  = "0xcf77a3ba9a5ca399b7c97c74d54e5b1beb874e43";
        // topic0 del evento Swap(address,address,int256,int256,uint160,uint128,int24)
        private const string TOPIC0_SWAP       = "0xc42079f94a6350d7e6235f29174924f928cc2ac818eb64fed8004e115fbcca67";

        public WssConnectionService(string wssUrl, SignalEngine signalEngine)
        {
            _wssUrl       = wssUrl;
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

            // Suscribirse a TODOS los logs (Alchemy ignora filtros server-side)
            var subscribeMsg = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id      = 1,
                method  = "eth_subscribe",
                @params = new object[] { "logs", new { } }
            });

            var bytes = Encoding.UTF8.GetBytes(subscribeMsg);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            Logger.Info("[WSS] Suscripción enviada — escuchando logs de Base Mainnet...");

            var buffer = new byte[4096];

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var sb     = new StringBuilder();
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

                // Ignorar mensajes de confirmación de suscripción
                if (!root.TryGetProperty("params", out var paramsEl)) return;
                if (!paramsEl.TryGetProperty("result", out var logEl)) return;

                // Extraer campos del log
                if (!logEl.TryGetProperty("address", out var addrEl))   return;
                if (!logEl.TryGetProperty("topics",  out var topicsEl)) return;

                var address = addrEl.GetString()?.ToLower() ?? "";
                var topics  = new List<string>();

                foreach (var t in topicsEl.EnumerateArray())
                    topics.Add(t.GetString()?.ToLower() ?? "");

                if (topics.Count == 0) return;

                // Log RAW de todo lo que llega — para diagnóstico
                Logger.Raw($"[WSS-RAW] address={address} | topic0={topics[0]}");

                // Filtro manual: solo Aerodrome router + evento Swap
                if (address != AERODROME_ROUTER) return;
                if (topics[0] != TOPIC0_SWAP)    return;

                // Extraer data del swap
                var dataStr = logEl.TryGetProperty("data", out var dataEl)
                    ? dataEl.GetString() ?? ""
                    : "";

                // Extraer sender desde topics[1] si existe
                var sender = topics.Count > 1
                    ? "0x" + topics[1].Replace("0x", "").TrimStart('0').PadLeft(40, '0')
                    : "unknown";

                // Extraer pool address del log (el address ES el pool en Aerodrome)
                var poolAddress = address;

                // Parseo básico de amounts desde data (primeros 2 slots de 32 bytes)
                decimal amount0 = 0, amount1 = 0;
                if (dataStr.Length >= 130)
                {
                    var hex0 = dataStr.Substring(2,  64);
                    var hex1 = dataStr.Substring(66, 64);
                    amount0  = ParseHexToDecimal(hex0);
                    amount1  = ParseHexToDecimal(hex1);
                }

                // Determinar dirección del swap (buy = amount0 negativo en Aerodrome V2)
                bool isBuy = amount0 < 0;

                var swapEvent = new SwapEvent
                {
                    PoolAddress  = poolAddress,
                    TokenAddress = poolAddress, // se enriquece después con DexScreener
                    WalletSender = sender,
                    AmountIn     = Math.Abs(isBuy ? amount1 : amount0),
                    AmountOut    = Math.Abs(isBuy ? amount0 : amount1),
                    IsBuy        = isBuy,
                    Timestamp    = DateTime.UtcNow
                };

                Logger.Debug($"[SWAP] pool={poolAddress[..10]}... | " +
                             $"{(isBuy ? "BUY " : "SELL")} | " +
                             $"sender={sender[..10]}...");

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
                // Convertir hex de 32 bytes a decimal (valor aproximado en wei)
                if (string.IsNullOrEmpty(hex)) return 0;
                var big = System.Numerics.BigInteger.Parse("0" + hex,
                    System.Globalization.NumberStyles.HexNumber);
                // Dividir por 1e18 para obtener valor en ETH aproximado
                return (decimal)big / 1_000_000_000_000_000_000m;
            }
            catch { return 0; }
        }
    }
}