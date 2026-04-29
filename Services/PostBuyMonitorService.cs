using System;
using System.Threading.Tasks;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class PostBuyMonitorService
    {
        private readonly DexScreenerService _dexScreener;
        private readonly TradingService     _trading;
        private readonly int  _windowSeconds;
        private readonly int  _initialSizePct;
        private readonly bool _enabled;

        public PostBuyMonitorService(
            DexScreenerService dexScreener,
            TradingService     trading,
            bool enabled        = true,
            int  windowSeconds  = 20,
            int  initialSizePct = 25)
        {
            _dexScreener    = dexScreener;
            _trading        = trading;
            _enabled        = enabled;
            _windowSeconds  = windowSeconds;
            _initialSizePct = initialSizePct;
        }

        /// <summary>
        /// Ejecuta la entrada en dos tramos:
        /// 1. Compra initialSizePct% del size
        /// 2. Observa windowSeconds segundos
        /// 3. Si comportamiento normal → completa el 75% restante
        /// 4. Si anomalía → vende el tramo inicial inmediatamente
        /// Devuelve el precio de entrada o 0 si se abortó.
        /// </summary>
        public async Task<decimal> ExecuteAsync(
            string symbol, string poolAddress,
            decimal totalAmountEth, decimal initialPrice)
        {
            if (!_enabled)
            {
                // Sin post-buy monitoring — compra directa al 100%
                return await _trading.BuyAsync(
                    symbol, poolAddress, totalAmountEth, initialPrice);
            }

            var initialAmount = totalAmountEth * _initialSizePct / 100m;
            var remainAmount  = totalAmountEth - initialAmount;

            // ── Tramo 1: compra inicial ───────────────────────────────────────
            Logger.Info($"[POSTBUY] {symbol} | comprando {_initialSizePct}% inicial ({initialAmount:F5} ETH)");
            var entryPrice = await _trading.BuyAsync(
                symbol, poolAddress, initialAmount, initialPrice);

            if (entryPrice == 0)
            {
                Logger.Error($"[POSTBUY] {symbol} | fallo en compra inicial");
                return 0;
            }

            // ── Ventana de observación ────────────────────────────────────────
            Logger.Info($"[POSTBUY] {symbol} | observando {_windowSeconds}s...");

            var startTime  = DateTime.UtcNow;
            var peakPrice  = entryPrice;
            var anomaly    = false;

            while ((DateTime.UtcNow - startTime).TotalSeconds < _windowSeconds)
            {
                await Task.Delay(3000);

                var profile = await _dexScreener.GetPoolProfileAsync(poolAddress);
                if (profile == null || profile.PriceUsd == 0) continue;

                var currentPrice = profile.PriceUsd;
                var pnl          = (currentPrice - entryPrice) / entryPrice * 100m;

                if (currentPrice > peakPrice) peakPrice = currentPrice;

                Logger.Info($"[POSTBUY] {symbol} | precio=${currentPrice:F8} | PnL={pnl:F1}%");

                // Anomalía: cae más del 10% desde entrada en ventana inicial
                if (pnl < -10m)
                {
                    Logger.Warning($"[POSTBUY] {symbol} | anomalía detectada (PnL={pnl:F1}%) → salida inmediata");
                    anomaly = true;
                    break;
                }

                // Anomalía: precio plano absoluto (in=0, out=0) → posible honeypot
                if (profile.LiquidityUsd < 100m)
                {
                    Logger.Warning($"[POSTBUY] {symbol} | liquidez colapsada → salida inmediata");
                    anomaly = true;
                    break;
                }
            }

            if (anomaly)
            {
                // Vender tramo inicial inmediatamente
                await _trading.SellAsync(symbol, poolAddress, initialAmount, entryPrice, initialPrice);
                Logger.Warning($"[POSTBUY] {symbol} | posición abortada — tramo inicial vendido");
                return 0;
            }

            // ── Tramo 2: completar posición ───────────────────────────────────
            Logger.Success($"[POSTBUY] {symbol} | comportamiento normal ✅ → completando {100 - _initialSizePct}% ({remainAmount:F5} ETH)");
            await _trading.BuyAsync(symbol, poolAddress, remainAmount, initialPrice);

            return entryPrice;
        }
    }
}