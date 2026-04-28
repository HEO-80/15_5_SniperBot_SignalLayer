using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using _15_5_SniperBot_SignalLayer.Models;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class SignalEngine
    {
        // Buffer de swaps por pool address
        private readonly ConcurrentDictionary<string, List<SwapEvent>> _buffer = new();
        private readonly object _lock = new();

        // Configuración
        private readonly int     _minSwaps30s;
        private readonly int     _minSwaps120s;
        private readonly decimal _minRatio;
        private readonly int     _minUniqueTraders;

        public event Action<TokenSignal>? OnSignalDetected;

        public SignalEngine(int minSwaps30s, int minSwaps120s, decimal minRatio, int minUniqueTraders)
        {
            _minSwaps30s      = minSwaps30s;
            _minSwaps120s     = minSwaps120s;
            _minRatio         = minRatio;
            _minUniqueTraders = minUniqueTraders;
        }

        public void AddSwap(SwapEvent swap)
        {
            lock (_lock)
            {
                if (!_buffer.ContainsKey(swap.PoolAddress))
                    _buffer[swap.PoolAddress] = new List<SwapEvent>();

                _buffer[swap.PoolAddress].Add(swap);

                // Limpiar eventos > 5 minutos para no acumular memoria
                var cutoff = DateTime.UtcNow.AddMinutes(-5);
                _buffer[swap.PoolAddress].RemoveAll(s => s.Timestamp < cutoff);
            }

            EvaluatePool(swap.PoolAddress);
        }

        private void EvaluatePool(string poolAddress)
        {
            List<SwapEvent> swaps;
            lock (_lock)
            {
                if (!_buffer.TryGetValue(poolAddress, out var list)) return;
                swaps = new List<SwapEvent>(list);
            }

            var now    = DateTime.UtcNow;
            var t30s   = now.AddSeconds(-30);
            var t60s   = now.AddSeconds(-60);
            var t120s  = now.AddSeconds(-120);

            var swaps30s  = swaps.Where(s => s.Timestamp >= t30s).ToList();
            var swaps60s  = swaps.Where(s => s.Timestamp >= t60s).ToList();
            var swaps120s = swaps.Where(s => s.Timestamp >= t120s).ToList();

            var buys60s   = swaps60s.Count(s => s.IsBuy);
            var sells60s  = swaps60s.Count(s => !s.IsBuy);
            var wallets   = swaps60s.Select(s => s.WalletSender).Distinct().Count();
            var ratio     = sells60s == 0 ? buys60s : (decimal)buys60s / sells60s;

            var signal = new TokenSignal
            {
                TokenAddress  = poolAddress,
                PoolAddress   = poolAddress,
                Swaps30s      = swaps30s.Count,
                Swaps120s     = swaps120s.Count,
                Buys60s       = buys60s,
                Sells60s      = sells60s,
                UniqueWallets = wallets,
                LastSeen      = now
            };

            // Log de señal para todos los pools con actividad
            if (swaps30s.Count > 0)
                Logger.Signal($"{poolAddress[..10]}... | {signal}");

            // Verificar si cumple todos los umbrales
            if (swaps30s.Count  < _minSwaps30s)
            { Logger.Reject($"{poolAddress[..10]}... | low_swaps_30s ({swaps30s.Count} < {_minSwaps30s})"); return; }

            if (swaps120s.Count < _minSwaps120s)
            { Logger.Reject($"{poolAddress[..10]}... | low_swaps_120s ({swaps120s.Count} < {_minSwaps120s})"); return; }

            if (ratio < _minRatio)
            { Logger.Reject($"{poolAddress[..10]}... | low_ratio ({ratio:F1} < {_minRatio})"); return; }

            if (wallets < _minUniqueTraders)
            { Logger.Reject($"{poolAddress[..10]}... | low_wallets ({wallets} < {_minUniqueTraders}) — posible wash-trading"); return; }

            // ✅ Señal válida — notificar
            Logger.Success($"[SIGNAL ✅] {poolAddress[..10]}... | {signal}");
            OnSignalDetected?.Invoke(signal);
        }

        public void PrintStats()
        {
            Logger.Info($"[SIGNAL ENGINE] Pools en buffer: {_buffer.Count}");
        }
    }
}