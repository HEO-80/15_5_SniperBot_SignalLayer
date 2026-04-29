using System;
using System.Threading;
using System.Threading.Tasks;
using _15_5_SniperBot_SignalLayer.Models;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class PriceMonitorService
    {
        private readonly DexScreenerService _dexScreener;
        private readonly decimal _tpPct;
        private readonly decimal _slPct;
        private readonly decimal _breakEvenTrigger;
        private readonly decimal _trailingPct;
        private readonly double  _maxPositionMinutes;
        private readonly double  _inactivitySeconds;

        public PriceMonitorService(
            DexScreenerService dexScreener,
            decimal tpPct            = 40m,
            decimal slPct            = 15m,
            decimal breakEvenTrigger = 15m,
            decimal trailingPct      = 10m,
            double  maxPositionMinutes = 15.0,
            double  inactivitySeconds  = 60.0)
        {
            _dexScreener        = dexScreener;
            _tpPct              = tpPct;
            _slPct              = slPct;
            _breakEvenTrigger   = breakEvenTrigger;
            _trailingPct        = trailingPct;
            _maxPositionMinutes = maxPositionMinutes;
            _inactivitySeconds  = inactivitySeconds;
        }

        public async Task<(string reason, decimal pnl)> MonitorAsync(
            string symbol, string poolAddress,
            decimal entryPrice, decimal amountEth,
            CancellationToken ct = default)
        {
            var stopPrice      = entryPrice * (1m - _slPct / 100m);
            var peakPrice      = entryPrice;
            var trailingActive = false;
            var timeoutActive  = true;
            var startTime      = DateTime.UtcNow;
            var lastPriceTime  = DateTime.UtcNow;
            var lastPrice      = entryPrice;

            Logger.Info($"[MON] {symbol} | entrada=${entryPrice:F8} | SL=${stopPrice:F8}");

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(3000, ct);

                var profile = await _dexScreener.GetPoolProfileAsync(poolAddress);
                if (profile == null || profile.PriceUsd == 0) continue;

                var currentPrice = profile.PriceUsd;
                var pnl          = (currentPrice - entryPrice) / entryPrice * 100m;

                // Detectar precio plano
                if (currentPrice != lastPrice)
                {
                    lastPrice     = currentPrice;
                    lastPriceTime = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - lastPriceTime).TotalSeconds > _inactivitySeconds && pnl < 5m)
                {
                    Logger.Warning($"[MON] {symbol} | precio plano {_inactivitySeconds}s → salida");
                    return ("inactivity", pnl);
                }

                // Actualizar pico
                if (currentPrice > peakPrice) peakPrice = currentPrice;

                // ── Trailing stop por escalones ───────────────────────────────
                decimal stopMinimo;
                if (pnl >= 50m)
                {
                    stopMinimo     = peakPrice * (1m - _trailingPct / 100m);
                    trailingActive = true;
                    timeoutActive  = false;
                    Logger.Info($"[TRAIL 🌙] {symbol} | Moon +{pnl:F1}% | stop→{stopMinimo:F8}");
                }
                else if (pnl >= 30m)
                {
                    stopMinimo     = entryPrice * 1.30m;
                    trailingActive = true;
                    timeoutActive  = false;
                }
                else if (pnl >= _breakEvenTrigger)
                {
                    stopMinimo     = entryPrice * 1.15m;
                    trailingActive = true;
                    timeoutActive  = false;
                    Logger.Info($"[TRAIL ✅] {symbol} | Escalón +15% | timeout OFF");
                }
                else
                {
                    stopMinimo = entryPrice * (1m - _slPct / 100m);
                }

                stopPrice = Math.Max(stopPrice, stopMinimo);

                var stopPct = (stopPrice - entryPrice) / entryPrice * 100m;
                Logger.Info($"[POS] {symbol} | ${currentPrice:F8} | PnL:{pnl:F1}% | Stop:{stopPct:F1}%");

                // ── Exits ─────────────────────────────────────────────────────
                if (currentPrice <= stopPrice)
                {
                    var reason = trailingActive ? "trail-stop" : "stop-loss";
                    Logger.Warning($"[EXIT] {symbol} | {reason.ToUpper()} | PnL:{pnl:F1}%");
                    return (reason, pnl);
                }

                if (pnl >= _tpPct)
                {
                    Logger.Success($"[EXIT] {symbol} | TAKE PROFIT +{pnl:F1}%");
                    return ("take-profit", pnl);
                }

                if (timeoutActive && (DateTime.UtcNow - startTime).TotalMinutes >= _maxPositionMinutes)
                {
                    Logger.Warning($"[EXIT] {symbol} | TIMEOUT {_maxPositionMinutes}min | PnL:{pnl:F1}%");
                    return ("timeout", pnl);
                }
            }

            return ("cancelled", 0m);
        }
    }
}