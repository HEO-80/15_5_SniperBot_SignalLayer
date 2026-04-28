using System;
using System.Threading.Tasks;
using DotNetEnv;
using _15_5_SniperBot_SignalLayer.Services;

namespace _15_5_SniperBot_SignalLayer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Clear();
            Env.Load();
            Logger.Init();

            // ── Helpers ───────────────────────────────────────────────────────
            decimal GetDec(string key, decimal def) =>
                decimal.TryParse(Environment.GetEnvironmentVariable(key),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;

            double GetDbl(string key, double def) =>
                double.TryParse(Environment.GetEnvironmentVariable(key),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;

            bool GetBool(string key, bool def) =>
                bool.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;

            int GetInt(string key, int def) =>
                int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : def;

            // ── Config desde .env ─────────────────────────────────────────────
            string wssUrl    = Environment.GetEnvironmentVariable("WSS_RPC_URL")  ?? "";
            string httpUrl   = Environment.GetEnvironmentVariable("HTTP_RPC_URL") ?? "";
            string privateKey= Environment.GetEnvironmentVariable("PRIVATE_KEY")  ?? "";
            bool   simMode   = GetBool("SIMULATION_MODE", true);

            if (string.IsNullOrEmpty(httpUrl) && !string.IsNullOrEmpty(wssUrl))
                httpUrl = wssUrl.Replace("wss://", "https://");

            // Capital y risk
            decimal amountEth        = GetDec("AMOUNT_ETH_PER_TRADE",   0.005m);
            decimal tpPct            = GetDec("TP_PERCENTAGE",           40m);
            decimal slPct            = GetDec("SL_PERCENTAGE",           15m);
            decimal breakEvenTrigger = GetDec("BREAK_EVEN_TRIGGER",      15m);
            decimal trailingPct      = GetDec("TRAILING_STOP_PERCENT",   10m);

            // Signal Engine
            int     minSwaps30s      = GetInt("MIN_SWAPS_30S",           3);
            int     minSwaps120s     = GetInt("MIN_SWAPS_120S",          8);
            decimal minRatio         = GetDec("MIN_BUY_SELL_RATIO",      2.0m);
            int     minUniqueTraders = GetInt("MIN_UNIQUE_TRADERS_60S",  5);

            // Pool filters
            decimal minLiq           = GetDec("MIN_LIQUIDITY_USD",       5000m);
            decimal maxLiq           = GetDec("MAX_LIQUIDITY_USD",       500000m);
            double  maxAgeHours      = GetDbl("MAX_TOKEN_AGE_HOURS",     24.0);
            double  minPoolMins      = GetDbl("MIN_POOL_AGE_MINUTES",    3.0);
            decimal maxLpConc        = GetDec("MAX_LP_CONCENTRATION",    0.80m);

            // Post-buy
            bool    postBuyEnabled   = GetBool("POSTBUY_MONITORING_ENABLED", true);
            int     postBuyInitPct   = GetInt("POSTBUY_INITIAL_SIZE_PCT",    25);
            int     postBuyWindowSec = GetInt("POSTBUY_WINDOW_SECONDS",      20);

            // Seguridad
            bool    callStaticEnabled = GetBool("CALLSTATIC_ENABLED", true);

            // Gas
            decimal gasBuyEth        = GetDec("GAS_COST_BUY_ETH",  0.00001m);
            decimal gasSellEth       = GetDec("GAS_COST_SELL_ETH", 0.00001m);

            // ── Banner ────────────────────────────────────────────────────────
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("══════════════════════════════════════════════════════════");
            Console.WriteLine("   🔥 SNIPER BOT 15.5 — SIGNAL LAYER / BASE MAINNET");
            Console.WriteLine("══════════════════════════════════════════════════════════");
            Console.ResetColor();

            // ── Validaciones críticas ─────────────────────────────────────────
            if (string.IsNullOrEmpty(wssUrl) || !wssUrl.StartsWith("wss://"))
            {
                Logger.Error("[FATAL] WSS_RPC_URL no configurado correctamente.");
                return;
            }
            if (string.IsNullOrEmpty(privateKey) || privateKey.Length < 60)
            {
                Logger.Error("[FATAL] PRIVATE_KEY no válida en el archivo .env.");
                return;
            }

            // ── Info de sesión ────────────────────────────────────────────────
            if (simMode)
                Logger.Warning("SIMULATION_MODE=true — No se enviará ninguna transacción real");
            else
                Logger.Warning("⚠️  MODO REAL ACTIVO — Las transacciones consumirán gas y ETH reales");

            Logger.Info($"RPC (WSS)     : {wssUrl[..Math.Min(50, wssUrl.Length)]}...");
            Logger.Info($"Capital/trade : {amountEth} ETH  |  TP: +{tpPct}%  |  SL: -{slPct}%  |  BE: +{breakEvenTrigger}%");
            Logger.Info($"Liquidez      : ${minLiq:N0} – ${maxLiq:N0}  |  Pool edad: {minPoolMins}min – {maxAgeHours}h");
            Logger.Info($"Signal Engine : swaps30>={minSwaps30s} | swaps120>={minSwaps120s} | ratio>={minRatio} | wallets>={minUniqueTraders}");
            Logger.Info($"callStatic    : {(callStaticEnabled ? "✅ activo" : "❌ desactivado")}");
            Logger.Info($"Post-buy      : {(postBuyEnabled ? $"✅ {postBuyInitPct}% inicial, {postBuyWindowSec}s observación" : "❌ desactivado")}");
            Logger.Info("Iniciando motor WebSocket...\n");

            // ── TODO: inicializar servicios y arrancar WSS ────────────────────
            Logger.Warning("[ Fase 0 OK ] Config cargada — servicios pendientes de implementar");

            Console.WriteLine("\nPresiona ENTER para salir.");
            Console.ReadLine();
        }
    }
}