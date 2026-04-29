using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using _15_5_SniperBot_SignalLayer.Models;

namespace _15_5_SniperBot_SignalLayer.Services
{
    public class SignalEngine
    {
        private readonly ConcurrentDictionary<string, List<SwapEvent>> _buffer      = new();
        private readonly ConcurrentDictionary<string, DateTime>        _cooldowns   = new();
        private readonly object _lock = new();

        // Wallets conocidas de arbitraje/bots — ignorar sus swaps
        private static readonly HashSet<string> _blacklistedWallets = new(StringComparer.OrdinalIgnoreCase)
        {
            "0x4752ba5dbc9168f817c9f3a7c67b43a78b4ca23", // bot arbitraje dominante Base
            "0x2aa7d880e3a4a0cb677c72d76e2fa1c7c48c3a24",
            "0x8407699e07d8e5d8e8e0e8b4c8c8c8c8c8c8c8c8",
        };

        private readonly int     _minSwaps30s;
        private readonly int     _minSwaps120s;
        private readonly decimal _minRatio;
        private readonly int     _minUniqueTraders;
        private readonly int     _cooldownSeconds;
        private readonly int     _minScore;

        public event Action<TokenSignal>? OnSignalDetected;

        public SignalEngine(
            int minSwaps30s, int minSwaps120s,
            decimal minRatio, int minUniqueTraders,
            int cooldownSeconds = 90, int minScore = 2)
        {
            _minSwaps30s      = minSwaps30s;
            _minSwaps120s     = minSwaps120s;
            _minRatio         = minRatio;
            _minUniqueTraders = minUniqueTraders;
            _cooldownSeconds  = cooldownSeconds;
            _minScore         = minScore;
        }

        public void AddSwap(SwapEvent swap)
        {
            // Ignorar wallets de arbitraje conocidas
            if (_blacklistedWallets.Contains(swap.WalletSender))
            {
                Logger.Raw($"[BLACKLIST] wallet={swap.WalletSender[..10]}... ignorada");
                return;
            }

            lock (_lock)
            {
                if (!_buffer.ContainsKey(swap.PoolAddress))
                    _buffer[swap.PoolAddress] = new List<SwapEvent>();

                _buffer[swap.PoolAddress].Add(swap);

                // Limpiar eventos > 5 minutos
                var cutoff = DateTime.UtcNow.AddMinutes(-5);
                _buffer[swap.PoolAddress].RemoveAll(s => s.Timestamp < cutoff);
            }

            EvaluatePool(swap.PoolAddress);
        }

        private void EvaluatePool(string poolAddress)
        {
            // Cooldown por pool — no spamear el mismo pool
            if (_cooldowns.TryGetValue(poolAddress, out var lastSignal))
            {
                if ((DateTime.UtcNow - lastSignal).TotalSeconds < _cooldownSeconds)
                    return;
            }

            List<SwapEvent> swaps;
            lock (_lock)
            {
                if (!_buffer.TryGetValue(poolAddress, out var list)) return;
                swaps = new List<SwapEvent>(list);
            }

            var now   = DateTime.UtcNow;
            var t30s  = now.AddSeconds(-30);
            var t60s  = now.AddSeconds(-60);
            var t120s = now.AddSeconds(-120);

            var swaps30s  = swaps.Where(s => s.Timestamp >= t30s).ToList();
            var swaps60s  = swaps.Where(s => s.Timestamp >= t60s).ToList();
            var swaps120s = swaps.Where(s => s.Timestamp >= t120s).ToList();

            var buys60s  = swaps60s.Count(s => s.IsBuy);
            var sells60s = swaps60s.Count(s => !s.IsBuy);
            var wallets  = swaps60s.Select(s => s.WalletSender)
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .Count();
            var ratio    = sells60s == 0
                ? (buys60s > 0 ? (decimal)buys60s : 0)
                : (decimal)buys60s / sells60s;

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

            // Log de actividad si hay swaps
            if (swaps30s.Count > 0)
                Logger.Signal($"{poolAddress[..10]}... | {signal}");

            // ── Filtros con REJECT reason ─────────────────────────────────────
            if (swaps30s.Count < _minSwaps30s)
            { Logger.Reject($"{poolAddress[..10]}... | low_swaps_30s ({swaps30s.Count} < {_minSwaps30s})"); return; }

            if (swaps120s.Count < _minSwaps120s)
            { Logger.Reject($"{poolAddress[..10]}... | low_swaps_120s ({swaps120s.Count} < {_minSwaps120s})"); return; }

            if (ratio < _minRatio)
            { Logger.Reject($"{poolAddress[..10]}... | low_ratio ({ratio:F1} < {_minRatio})"); return; }

            if (wallets < _minUniqueTraders)
            { Logger.Reject($"{poolAddress[..10]}... | low_wallets ({wallets} < {_minUniqueTraders}) — wash-trading"); return; }

            // ── Score simple (0-4) ────────────────────────────────────────────
            int score = 0;
            if (ratio >= 3.0m)           score++; // ratio alto
            if (wallets >= 5)            score++; // muchas wallets distintas
            if (swaps120s.Count >= 10)   score++; // volumen sostenido
            if (buys60s > sells60s * 2)  score++; // presión compradora fuerte

            if (score < _minScore)
            { Logger.Reject($"{poolAddress[..10]}... | low_score ({score} < {_minScore})"); return; }

            // ── Señal válida ──────────────────────────────────────────────────
            _cooldowns[poolAddress] = DateTime.UtcNow;

            Logger.Success($"[SIGNAL ✅] {poolAddress[..10]}... | {signal} | score={score}/4");
            OnSignalDetected?.Invoke(signal);
        }

        public int ActivePools => _buffer.Count;
    }
}