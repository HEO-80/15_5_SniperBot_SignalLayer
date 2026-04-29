using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class DexScreenerService
    {
        private readonly HttpClient _http;
        private const string BASE_URL = "https://api.dexscreener.com/latest/dex/pairs/base/";

        public DexScreenerService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        public async Task<PoolProfile?> GetPoolProfileAsync(string poolAddress)
        {
            try
            {
                var url      = BASE_URL + poolAddress;
                var response = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);

                if (!doc.RootElement.TryGetProperty("pairs", out var pairs)) return null;
                if (pairs.ValueKind != JsonValueKind.Array || pairs.GetArrayLength() == 0) return null;

                var pair = pairs[0];

                var liquidity  = pair.TryGetProperty("liquidity", out var liq)
                    && liq.TryGetProperty("usd", out var liqUsd)
                    ? liqUsd.GetDecimal() : 0;

                var priceUsd   = pair.TryGetProperty("priceUsd", out var pEl)
                    ? decimal.TryParse(pEl.GetString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0
                    : 0;

                var ageMinutes = 0.0;
                if (pair.TryGetProperty("pairCreatedAt", out var createdEl))
                {
                    var createdMs  = createdEl.GetInt64();
                    var created    = DateTimeOffset.FromUnixTimeMilliseconds(createdMs).UtcDateTime;
                    ageMinutes     = (DateTime.UtcNow - created).TotalMinutes;
                }

                var baseToken  = pair.TryGetProperty("baseToken", out var bt) ? bt : default;
                var symbol     = baseToken.TryGetProperty("symbol", out var sym)
                    ? sym.GetString() ?? "???" : "???";
                var tokenAddr  = baseToken.TryGetProperty("address", out var addr)
                    ? addr.GetString() ?? "" : "";

                var quoteToken = pair.TryGetProperty("quoteToken", out var qt) ? qt : default;
                var quoteAddr  = quoteToken.TryGetProperty("address", out var qa)
                    ? qa.GetString() ?? "" : "";

                bool hasWeth   = quoteAddr.Equals(
                    "0x4200000000000000000000000000000000000006",
                    StringComparison.OrdinalIgnoreCase)
                    || tokenAddr.Equals(
                    "0x4200000000000000000000000000000000000006",
                    StringComparison.OrdinalIgnoreCase);

                bool hasUsdc   = quoteAddr.Equals(
                    "0x833589fcd6edb6e08f4c7c32d4f71b54bda02913",
                    StringComparison.OrdinalIgnoreCase)
                    || tokenAddr.Equals(
                    "0x833589fcd6edb6e08f4c7c32d4f71b54bda02913",
                    StringComparison.OrdinalIgnoreCase);

                var volume24h  = pair.TryGetProperty("volume", out var vol)
                    && vol.TryGetProperty("h24", out var v24)
                    ? v24.GetDecimal() : 0;

                var priceChange5m = pair.TryGetProperty("priceChange", out var pc)
                    && pc.TryGetProperty("m5", out var pc5)
                    ? pc5.GetDecimal() : 0;

                return new PoolProfile
                {
                    PoolAddress    = poolAddress,
                    TokenAddress   = tokenAddr,
                    Symbol         = symbol,
                    LiquidityUsd   = liquidity,
                    AgeMinutes     = ageMinutes,
                    HasWeth        = hasWeth,
                    HasUsdc        = hasUsdc,
                    Volume24h      = volume24h,
                    PriceUsd       = priceUsd,
                    PriceChange5m  = priceChange5m
                };
            }
            catch (Exception ex)
            {
                Logger.Debug($"[DEXSCREENER] Error: {ex.Message}");
                return null;
            }
        }
    }

    public class PoolProfile
    {
        public string  PoolAddress   { get; set; } = "";
        public string  TokenAddress  { get; set; } = "";
        public string  Symbol        { get; set; } = "";
        public decimal LiquidityUsd  { get; set; }
        public double  AgeMinutes    { get; set; }
        public bool    HasWeth       { get; set; }
        public bool    HasUsdc       { get; set; }
        public decimal Volume24h     { get; set; }
        public decimal PriceUsd      { get; set; }
        public decimal PriceChange5m { get; set; }

        public override string ToString() =>
            $"liq=${LiquidityUsd:N0} | age={AgeMinutes:F0}min | " +
            $"pair={(HasWeth ? "WETH" : HasUsdc ? "USDC" : "??")} | vol24h=${Volume24h:N0} | Δ5m={PriceChange5m:F1}%";
    }
}