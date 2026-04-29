using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class GoPlusService
    {
        private readonly HttpClient _http;
        private const string BASE_URL = "https://api.gopluslabs.io/api/v1/token_security/8453?contract_addresses=";

        public GoPlusService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        public async Task<bool> IsTokenSafeAsync(string tokenAddress, string symbol)
        {
            try
            {
                var url      = BASE_URL + tokenAddress;
                var response = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);

                if (!doc.RootElement.TryGetProperty("result", out var result)) 
                {
                    Logger.Reject($"{symbol} | goplus_no_result");
                    return false;
                }

                if (!result.TryGetProperty(tokenAddress.ToLower(), out var token))
                {
                    Logger.Reject($"{symbol} | goplus_token_not_found");
                    return false;
                }

                // ── Checks críticos ───────────────────────────────────────────
                if (GetInt(token, "is_mintable") == 1)
                { Logger.Reject($"{symbol} | goplus: is_mintable"); return false; }

                if (GetInt(token, "trading_cooldown") == 1)
                { Logger.Reject($"{symbol} | goplus: trading_cooldown"); return false; }

                if (GetInt(token, "transfer_pausable") == 1)
                { Logger.Reject($"{symbol} | goplus: transfer_pausable"); return false; }

                if (GetInt(token, "cannot_sell_all") == 1)
                { Logger.Reject($"{symbol} | goplus: cannot_sell_all"); return false; }

                if (GetInt(token, "is_blacklisted") == 1)
                { Logger.Reject($"{symbol} | goplus: is_blacklisted"); return false; }

                if (GetInt(token, "is_honeypot") == 1)
                { Logger.Reject($"{symbol} | goplus: is_honeypot"); return false; }

                // Taxes
                var buyTax  = GetDecimal(token, "buy_tax");
                var sellTax = GetDecimal(token, "sell_tax");

                if (buyTax > 0)
                { Logger.Reject($"{symbol} | goplus: buy_tax={buyTax}%"); return false; }

                if (sellTax > 0)
                { Logger.Reject($"{symbol} | goplus: sell_tax={sellTax}%"); return false; }

                // Advertencia no bloqueante
                if (GetInt(token, "is_open_source") == 0)
                    Logger.Warning($"{symbol} | goplus: not open source (advertencia)");

                Logger.Success($"[GOPLUS ✅] {symbol} | buy={buyTax}% sell={sellTax}% — limpio");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Reject($"{symbol} | goplus_error: {ex.Message}");
                return false;
            }
        }

        private static int GetInt(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var prop)) return 0;
            if (prop.ValueKind == JsonValueKind.String)
                return int.TryParse(prop.GetString(), out var v) ? v : 0;
            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetInt32();
            return 0;
        }

        private static decimal GetDecimal(JsonElement el, string key)
        {
            if (!el.TryGetProperty(key, out var prop)) return 0;
            if (prop.ValueKind == JsonValueKind.String)
                return decimal.TryParse(prop.GetString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
            if (prop.ValueKind == JsonValueKind.Number)
                return prop.GetDecimal();
            return 0;
        }
    }
}