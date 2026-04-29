using System;
using System.Threading;
using System.Threading.Tasks;
using DotNetEnv;
using Nethereum.Web3;
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

            Logger.MinLevel = LogLevel.DEBUG;

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

            // ── Config ────────────────────────────────────────────────────────
            string wssUrl = Environment.GetEnvironmentVariable("WSS_RPC_URL") ?? "";
            string httpUrl = Environment.GetEnvironmentVariable("HTTP_RPC_URL") ?? "";
            string privateKey = Environment.GetEnvironmentVariable("PRIVATE_KEY") ?? "";
            bool simMode = GetBool("SIMULATION_MODE", true);

            if (string.IsNullOrEmpty(httpUrl) && !string.IsNullOrEmpty(wssUrl))
                httpUrl = wssUrl.Replace("wss://", "https://");

            decimal amountEth = GetDec("AMOUNT_ETH_PER_TRADE", 0.005m);
            decimal tpPct = GetDec("TP_PERCENTAGE", 40m);
            decimal slPct = GetDec("SL_PERCENTAGE", 15m);
            decimal breakEvenTrigger = GetDec("BREAK_EVEN_TRIGGER", 15m);
            decimal trailingPct = GetDec("TRAILING_STOP_PERCENT", 10m);
            int minSwaps30s = GetInt("MIN_SWAPS_30S", 2);
            int minSwaps120s = GetInt("MIN_SWAPS_120S", 4);
            decimal minRatio = GetDec("MIN_BUY_SELL_RATIO", 1.5m);
            int minUniqueTraders = GetInt("MIN_UNIQUE_TRADERS_60S", 2);
            decimal minLiq = GetDec("MIN_LIQUIDITY_USD", 5000m);
            decimal maxLiq = GetDec("MAX_LIQUIDITY_USD", 500000m);
            double maxAgeHours = GetDbl("MAX_TOKEN_AGE_HOURS", 24.0);
            double minPoolMins = GetDbl("MIN_POOL_AGE_MINUTES", 3.0);
            decimal gasBuyEth = GetDec("GAS_COST_BUY_ETH", 0.00001m);
            decimal gasSellEth = GetDec("GAS_COST_SELL_ETH", 0.00001m);

            // ── Banner ────────────────────────────────────────────────────────
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("══════════════════════════════════════════════════════════");
            Console.WriteLine("   🔥 SNIPER BOT 15.5 — SIGNAL LAYER / BASE MAINNET");
            Console.WriteLine("══════════════════════════════════════════════════════════");
            Console.ResetColor();

            // ── Validaciones ──────────────────────────────────────────────────
            if (string.IsNullOrEmpty(wssUrl) || !wssUrl.StartsWith("wss://"))
            { Logger.Error("[FATAL] WSS_RPC_URL no configurado."); return; }

            if (string.IsNullOrEmpty(privateKey) || privateKey.Length < 60)
            { Logger.Error("[FATAL] PRIVATE_KEY no válida."); return; }

            if (simMode)
                Logger.Warning("SIMULATION_MODE=true — No se enviará ninguna transacción real");
            else
                Logger.Warning("⚠️  MODO REAL ACTIVO");

            Logger.Info($"RPC (WSS)     : {wssUrl[..Math.Min(50, wssUrl.Length)]}...");
            Logger.Info($"Capital/trade : {amountEth} ETH  |  TP: +{tpPct}%  |  SL: -{slPct}%");
            Logger.Info($"Signal Engine : swaps30>={minSwaps30s} | swaps120>={minSwaps120s} | ratio>={minRatio} | wallets>={minUniqueTraders}");
            Logger.Info($"Pool filters  : liq=${minLiq:N0}-${maxLiq:N0} | age={minPoolMins}min-{maxAgeHours}h");
            Logger.Info("Iniciando Signal Engine y WSS...\n");

            // ── Servicios ─────────────────────────────────────────────────────
            var stats = new StatsTracker(gasBuyEth, gasSellEth);
            var dexScreener = new DexScreenerService();
            var poolProfileSvc = new PoolProfileService(dexScreener, minLiq, maxLiq, maxAgeHours, minPoolMins);
            var goPlus = new GoPlusService();
            var web3 = new Web3(httpUrl);
            var callStatic = new CallStaticService(web3);
            var trading = new TradingService(simMode);
            var priceMonitor = new PriceMonitorService(
                dexScreener, tpPct, slPct, breakEvenTrigger, trailingPct);
            bool postBuyEnabled = GetBool("POSTBUY_MONITORING_ENABLED", true);
            int postBuyInitPct = GetInt("POSTBUY_INITIAL_SIZE_PCT", 25);
            int postBuyWindowSec = GetInt("POSTBUY_WINDOW_SECONDS", 20);

            var postBuy = new PostBuyMonitorService(
                dexScreener, trading,
                enabled: postBuyEnabled,
                windowSeconds: postBuyWindowSec,
                initialSizePct: postBuyInitPct);

            var signalEngine = new SignalEngine(
                minSwaps30s, minSwaps120s, minRatio, minUniqueTraders,
                cooldownSeconds: 90,
                minScore: 2
            );
            var wss = new WssConnectionService(wssUrl, signalEngine);

            // ── Pipeline async ────────────────────────────────────────────────
            signalEngine.OnSignalDetected += async signal =>
            {
                try
                {
                    stats.AddSignalDetected();
                    Logger.Success($"[CANDIDATO] {signal.PoolAddress[..10]}... | {signal}");

                    // Fase 3 — Pool Profile
                    var profile = await poolProfileSvc.EvaluateAsync(signal);
                    if (profile == null) return;
                    stats.AddPassedPool();

                    // Fase 4 — GoPlus
                    bool isSafe = await goPlus.IsTokenSafeAsync(profile.TokenAddress, profile.Symbol);
                    if (!isSafe) return;
                    stats.AddPassedGoPlus();

                    // Fase 5 — callStatic
                    bool tradable = await callStatic.IsTokenTradableAsync(
                        profile.TokenAddress, profile.Symbol, amountEth);
                    if (!tradable) return;
                    stats.AddPassedCallStatic();

                    // Fase 6 — Compra + Monitor
                    Logger.Success($"[ENTRADA] {profile.Symbol} — ejecutando compra");
                    // decimal entryPrice = await trading.BuyAsync(
                    //     profile.Symbol, profile.PoolAddress, amountEth, profile.PriceUsd);
                    decimal entryPrice = await trading.BuyAsync(
profile.Symbol, profile.PoolAddress, amountEth, profile.PriceUsd);

                    if (entryPrice == 0) return;
                    stats.AddTradeExecuted();

                    (string reason, decimal pnl) result = await priceMonitor.MonitorAsync(
                        profile.Symbol, profile.PoolAddress, entryPrice, amountEth);

                    if (result.reason == "take-profit")
                        stats.AddTakeProfit(result.pnl * amountEth / 100m);
                    else
                        stats.AddStopLoss(result.pnl * amountEth / 100m);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[PIPELINE] Error: {ex.Message}");
                }
            };

            // ── Arrancar WSS ──────────────────────────────────────────────────
            var cts = new CancellationTokenSource();
            var wssTask = Task.Run(() => wss.StartListeningAsync(cts.Token));

            Logger.Info("Presiona ENTER para detener.");
            Console.ReadLine();

            cts.Cancel();
            await wssTask;

            stats.PrintSummary();
        }
    }
}