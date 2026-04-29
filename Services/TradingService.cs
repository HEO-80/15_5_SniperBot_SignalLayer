using System;
using System.Threading.Tasks;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class TradingService
    {
        private readonly bool _simMode;

        public TradingService(bool simMode = true)
        {
            _simMode = simMode;
        }

        public async Task<decimal> BuyAsync(
            string symbol, string poolAddress,
            decimal amountEth, decimal priceUsd)
        {
            await Task.Delay(200);

            if (_simMode)
            {
                Logger.Info($"[SIM] COMPRA {amountEth} ETH → {symbol} @ ${priceUsd:F8}");
                return priceUsd;
            }

            Logger.Error("[TRADE] Modo real no implementado aún");
            return 0;
        }

        public async Task<decimal> SellAsync(
            string symbol, string poolAddress,
            decimal amountEth, decimal entryPrice, decimal currentPrice)
        {
            await Task.Delay(200);

            var pnl = (currentPrice - entryPrice) / entryPrice * 100m;

            if (_simMode)
            {
                Logger.Info($"[SIM] VENTA {symbol} @ ${currentPrice:F8} | PnL: {pnl:F2}%");
                return pnl;
            }

            Logger.Error("[TRADE] Modo real no implementado aún");
            return 0;
        }
    }
}