using System;
using System.Threading.Tasks;
using _15_5_SniperBot_SignalLayer.Models;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class PoolProfileService
    {
        private readonly DexScreenerService _dexScreener;
        private readonly decimal _minLiquidity;
        private readonly decimal _maxLiquidity;
        private readonly double  _maxAgeHours;
        private readonly double  _minAgeMinutes;

        public PoolProfileService(
            DexScreenerService dexScreener,
            decimal minLiquidity  = 5000m,
            decimal maxLiquidity  = 500000m,
            double  maxAgeHours   = 24.0,
            double  minAgeMinutes = 3.0)
        {
            _dexScreener   = dexScreener;
            _minLiquidity  = minLiquidity;
            _maxLiquidity  = maxLiquidity;
            _maxAgeHours   = maxAgeHours;
            _minAgeMinutes = minAgeMinutes;
        }

        public async Task<PoolProfile?> EvaluateAsync(TokenSignal signal)
        {
            var profile = await _dexScreener.GetPoolProfileAsync(signal.PoolAddress);

            if (profile == null)
            {
                Logger.Reject($"{signal.PoolAddress[..10]}... | dexscreener_no_data");
                return null;
            }

            // Par WETH o USDC obligatorio
            if (!profile.HasWeth && !profile.HasUsdc)
            {
                Logger.Reject($"{profile.Symbol} | no_weth_or_usdc_pair");
                return null;
            }

            // Rango de liquidez
            if (profile.LiquidityUsd < _minLiquidity)
            {
                Logger.Reject($"{profile.Symbol} | low_liquidity (${profile.LiquidityUsd:N0} < ${_minLiquidity:N0})");
                return null;
            }

            if (profile.LiquidityUsd > _maxLiquidity)
            {
                Logger.Reject($"{profile.Symbol} | high_liquidity (${profile.LiquidityUsd:N0} > ${_maxLiquidity:N0})");
                return null;
            }

            // Edad del pool
            var maxAgeMinutes = _maxAgeHours * 60;
            if (profile.AgeMinutes > maxAgeMinutes)
            {
                Logger.Reject($"{profile.Symbol} | too_old ({profile.AgeMinutes:F0}min > {maxAgeMinutes:F0}min)");
                return null;
            }

            if (profile.AgeMinutes < _minAgeMinutes)
            {
                Logger.Reject($"{profile.Symbol} | too_young ({profile.AgeMinutes:F1}min < {_minAgeMinutes}min)");
                return null;
            }

            // Enriquecer la señal con el símbolo
            signal.TokenAddress = profile.TokenAddress;
            signal.TokenSymbol  = profile.Symbol;

            Logger.Filter($"{profile.Symbol} | {profile} ✅");
            return profile;
        }
    }
}