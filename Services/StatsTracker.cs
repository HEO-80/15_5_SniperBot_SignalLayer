using System;
using _15_5_SniperBot_SignalLayer.Services;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class StatsTracker
    {
        private int _swapsRaw;
        private int _signalsDetected;
        private int _signalsPassedMomentum;
        private int _signalsPassedPool;
        private int _signalsPassedGoPlus;
        private int _signalsPassedCallStatic;
        private int _tradesExecuted;
        private int _postBuyCompleted;
        private int _postBuyAborted;
        private int _takeProfit;
        private int _stopLoss;
        private decimal _pnlBruto;
        private decimal _gasCost;

        private readonly decimal _gasBuyEth;
        private readonly decimal _gasSellEth;

        public StatsTracker(decimal gasBuyEth = 0.00001m, decimal gasSellEth = 0.00001m)
        {
            _gasBuyEth  = gasBuyEth;
            _gasSellEth = gasSellEth;
        }

        public void AddSwapRaw()             => _swapsRaw++;
        public void AddSignalDetected()      => _signalsDetected++;
        public void AddPassedMomentum()      => _signalsPassedMomentum++;
        public void AddPassedPool()          => _signalsPassedPool++;
        public void AddPassedGoPlus()        => _signalsPassedGoPlus++;
        public void AddPassedCallStatic()    => _signalsPassedCallStatic++;
        public void AddTradeExecuted()       => _tradesExecuted++;
        public void AddPostBuyCompleted()    => _postBuyCompleted++;
        public void AddPostBuyAborted()      => _postBuyAborted++;
        public void AddTakeProfit(decimal pnl) { _takeProfit++; _pnlBruto += pnl; _gasCost += _gasBuyEth + _gasSellEth; }
        public void AddStopLoss(decimal pnl)   { _stopLoss++;  _pnlBruto += pnl; _gasCost += _gasBuyEth + _gasSellEth; }

        public void PrintSummary()
        {
            var pnlNeto = _pnlBruto - _gasCost;
            var usd     = pnlNeto * 2500m; // ETH aproximado

            Logger.Info("══════════════ ESTADÍSTICAS DE SESIÓN (15.5) ══════════════");
            Logger.Info($"  Swaps raw recibidos      : {_swapsRaw}");
            Logger.Info($"  Señales detectadas       : {_signalsDetected}");
            Logger.Info($"  Pasan momentum filter    : {_signalsPassedMomentum}");
            Logger.Info($"  Pasan pool profile       : {_signalsPassedPool}");
            Logger.Info($"  Pasan GoPlus             : {_signalsPassedGoPlus}");
            Logger.Info($"  Pasan callStatic         : {_signalsPassedCallStatic}");
            Logger.Info($"  Trades ejecutados        : {_tradesExecuted}");
            Logger.Info($"  Post-buy completados     : {_postBuyCompleted}");
            Logger.Info($"  Post-buy abortados       : {_postBuyAborted}");
            Logger.Info($"  Take Profit (TP)         : {_takeProfit}");
            Logger.Info($"  Stop Loss   (SL)         : {_stopLoss}");
            Logger.Info($"  PnL bruto                : +{_pnlBruto:F6} ETH");
            Logger.Info($"  Gas estimado             : -{_gasCost:F6} ETH");
            Logger.Info($"  PnL NETO                 : {pnlNeto:F6} ETH  ({usd:F2} USD)");
            Logger.Info("═══════════════════════════════════════════════════════════");
        }
    }
}