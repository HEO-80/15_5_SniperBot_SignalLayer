# 📋 PROGRESS_LOG — Bot 15.5 SniperBot Signal Layer (Base Mainnet)

---

## 🗓️ 28-29/04/2026 — Sesión de desarrollo completo

### ✅ FASE 0 — Base (WSS + Logger + Config)

**Completado:**
- Proyecto .NET 10 creado (`dotnet new console --framework net10.0`)
- Estructura de carpetas: `Services/`, `Models/`, `logs/`
- `.env` + `.env.example` + `.gitignore`
- NuGet instalados: `DotNetEnv`, `Nethereum.Web3`, `Nethereum.JsonRpc.WebSocketClient`
- `Logger.cs` con niveles `RAW / DEBUG / INFO / WARNING / SUCCESS / ERROR`
  - Métodos especiales: `Signal()`, `Filter()`, `Reject()` con colores diferenciados
  - Log a consola + archivo diario en `logs/`
- `Program.cs` con carga completa de `.env`, validaciones críticas y banner

**Checks superados:**
- ✅ Bot arranca limpio
- ✅ Validación de `WSS_RPC_URL` y `PRIVATE_KEY` al inicio
- ✅ Logger escribe en consola y archivo simultáneamente

---

### ✅ FASE 1 — Ingesta de datos (WSS + Swap parsing)

**Completado:**
- `WssConnectionService.cs` — conexión WebSocket persistente a Alchemy Base Mainnet
- Reconexión automática ante caída WSS (loop con 5s de delay)
- Suscripción sin filtro server-side (`eth_subscribe("logs", {})`)
  - **Motivo:** Alchemy ignora filtros RPC-side en `eth_subscribe`
- Filtro manual por `topic0`:
  - Aerodrome V2 Basic Swap: `0xd78ad95fa46c994b6551d0da85fc275fe613ce37657fb8d5e3d130840159d822`
- Parseo correcto del evento Swap de Aerodrome V2 Basic:
  - `data = amount0In(32) + amount1In(32) + amount0Out(32) + amount1Out(32)`
  - `isBuy = amount1In > 0 && amount0In == 0`
- Buffer de 8192 bytes con lectura de mensajes multi-frame

**Checks superados:**
- ✅ `[WSS-RAW]` logs aparecen constantemente — eventos reales llegando
- ✅ BUY y SELL detectados correctamente
- ✅ Amounts parseados en ETH (dividiendo por 1e18)

**Descubrimiento clave:**
- Base Mainnet de noche dominada por bot de arbitraje `0x4752ba5dbc23f44d87826276bf6fd6b1c372ad24`

---

### ✅ FASE 2 — Signal Engine

**Completado:**
- `Models/SwapEvent.cs` — datos de cada swap detectado
- `Models/TokenSignal.cs` — señal agregada por pool con métricas calculadas
- `Services/SignalEngine.cs` — motor de acumulación y evaluación:
  - Buffer por pool: `ConcurrentDictionary<string, List<SwapEvent>>`
  - Limpieza automática de eventos > 5 minutos
  - Ventana híbrida: **30s** (micro-momentum) + **120s** (confirmación)
  - Cálculo de ratio buys/sells en ventana 60s
  - Unique wallets en ventana 60s (anti wash-trading)
  - Score simple 0-4:
    - +1 si ratio >= 3.0
    - +1 si wallets >= 5
    - +1 si swaps120 >= 10
    - +1 si buys > sells × 2
  - Cooldown por pool: 90s (evita spam del mismo pool)
  - Evento `OnSignalDetected` (async) cuando pool supera todos los umbrales

**Checks superados:**
- ✅ `[SIGNAL]` logs muestran métricas por pool
- ✅ `[REJECT]` logs especifican razón exacta del descarte
- ✅ `[SIGNAL ✅]` cuando pool supera todos los filtros

---

### ✅ FASE 2.6 — Fix señal (anti wash-trading)

**Completado:**
- Blacklist de wallets de arbitraje conocidas en `WssConnectionService.cs`:
  ```
  0x4752ba5dbc23f44d87826276bf6fd6b1c372ad24  (bot principal)
  0x2aa7d880b7ad5964c02b919074fb27a71a7ddd07
  0x8f10b468b06c6fd214b65f87778827f7d113f996
  0x7747f8d2a76bd6345cc29622a946a929647f2359
  0x0ca4dbb82d67c51760ae608e4efb6369d31ed272
  0x83d55acdc72027ed339d267eebaf9a41e47490d5
  0x0403795ead3079acfdd7d8d46cd2f134371e64e2
  0x4a012af2b05616fb390ed32452641c3f04633bb5
  ```
- Check de blacklist ANTES del Signal Engine — swaps ignorados antes de procesarse

**Checks superados:**
- ✅ `0x4752ba5d` ya no aparece en los logs
- ✅ Wallets reales distintas visibles tras filtrado

---

### ✅ FASE 3 — Pool Profile

**Completado:**
- `Services/DexScreenerService.cs`:
  - Consulta `https://api.dexscreener.com/latest/dex/pairs/base/{pool}`
  - Extrae: liquidez USD, precio, edad del pool, par WETH, volumen 24h, priceChange5m
  - Timeout 10s, manejo de errores silencioso
- `Services/PoolProfileService.cs`:
  - Filtro par WETH obligatorio
  - Rango de liquidez: $5K – $500K
  - Edad: > 3 minutos y < 24 horas
  - Enriquece `TokenSignal` con símbolo y address del token
  - Logs `[FILTER]` y `[REJECT]` con razón específica

**Checks superados:**
- ✅ Log `[FILTER] TOKEN | liq=$Xk | age=Xmin | weth=True ✅`
- ✅ Rechazos con razón: `no_weth_pair`, `low_liquidity`, `too_old`, `too_young`

---

### ✅ FASE 4 — GoPlus Security

**Completado:**
- `Services/GoPlusService.cs`:
  - Chain ID 8453 (Base Mainnet)
  - Endpoint: `https://api.gopluslabs.io/api/v1/token_security/8453`
  - Checks bloqueantes:
    - `is_mintable = 1`
    - `trading_cooldown = 1`
    - `transfer_pausable = 1`
    - `cannot_sell_all = 1`
    - `is_blacklisted = 1`
    - `is_honeypot = 1`
    - `buy_tax > 0%`
    - `sell_tax > 0%`
  - `is_open_source = 0` → advertencia no bloqueante
  - Si GoPlus falla → NO ENTRAR nunca

**Checks superados:**
- ✅ `[GOPLUS ✅]` cuando token limpio
- ✅ `[REJECT]` con razón específica del flag que falla

---

### ✅ FASE 5 — callStatic pre-trade

**Completado:**
- `Services/CallStaticService.cs`:
  - Usa `getAmountsOut` del Router de Aerodrome V2
  - Simula BUY: `WETH → token` — verifica tokens recibidos > 0
  - Simula SELL: `token → WETH` — verifica ETH devuelto > 0
  - Si sell devuelve 0 → REJECT honeypot técnico
  - Muestra porcentaje de recuperación estimado

**Checks superados:**
- ✅ `[CALLSTATIC ✅] TOKEN | recuperación: XX% — token vendible`
- ✅ `[REJECT] TOKEN | callstatic: sell devuelve 0 ETH — honeypot`

---

### ✅ FASE 6 — Post-buy monitoring

**Completado:**
- `Services/PostBuyMonitorService.cs`:
  - Compra inicial: `POSTBUY_INITIAL_SIZE_PCT%` (default 25%)
  - Ventana de observación: `POSTBUY_WINDOW_SECONDS` segundos (default 20s)
  - Checks de anomalía durante ventana:
    - PnL < -10% → salida inmediata
    - Liquidez colapsada (< $100) → salida inmediata
  - Si normal → completa el 75% restante
  - Si anomalía → vende tramo inicial y aborta posición

**Checks superados:**
- ✅ `[POSTBUY] observando...`
- ✅ `[POSTBUY] comportamiento normal ✅ → completando 75%`
- ✅ `[POSTBUY] anomalía detectada → salida inmediata`

---

### ✅ FASE 7 — Execution + Trailing escalones

**Completado:**
- `Services/TradingService.cs`:
  - `BuyAsync` y `SellAsync` en modo simulación
  - Stub para modo real (pendiente implementación Nethereum)
- `Services/PriceMonitorService.cs`:
  - Polling cada 3s via DexScreener
  - Trailing stop por escalones garantizados:
    - PnL < +15% → SL fijo en -15%
    - PnL ≥ +15% → stop anclado en +15%, timeout OFF
    - PnL ≥ +30% → stop anclado en +30%
    - PnL ≥ +50% → trailing 10% sobre pico (modo moon 🌙)
  - Timeout condicional: solo activo si PnL < +15%
  - Detección de precio plano: si no cambia en 60s y PnL < 5% → salida

---

### ✅ FASE 8 — Stats completas

**Completado:**
- `Services/StatsTracker.cs`:
  - Señales detectadas
  - Señales que pasan momentum filter
  - Señales que pasan pool profile
  - Señales que pasan GoPlus
  - Señales que pasan callStatic
  - Trades ejecutados
  - Post-buy completados vs abortados
  - Take Profit / Stop Loss
  - PnL bruto y neto (descontando gas)
  - `PrintSummary()` al parar el bot

---

## 📊 Estado actual — 29/04/2026

| Componente | Estado |
|:---|:---:|
| WSS Base conectado | ✅ |
| Ingesta Swap V2 Aerodrome | ✅ |
| Blacklist arbitraje | ✅ |
| Signal Engine 30s/120s | ✅ |
| Pool Profile DexScreener | ✅ |
| GoPlus Chain 8453 | ✅ |
| callStatic pre-trade | ✅ |
| Post-buy monitoring | ✅ |
| Trailing escalones | ✅ |
| Stats tracker | ✅ |
| Primer [CANDIDATO] real | 🔄 Pendiente |

---

## ⚙️ Configuración activa (`.env`)

```env
SIMULATION_MODE=true
AMOUNT_ETH_PER_TRADE=0.005
TP_PERCENTAGE=40
SL_PERCENTAGE=15
BREAK_EVEN_TRIGGER=15
TRAILING_STOP_PERCENT=10
MIN_SWAPS_30S=3
MIN_SWAPS_120S=6
MIN_BUY_SELL_RATIO=2.0
MIN_UNIQUE_TRADERS_60S=3
MIN_LIQUIDITY_USD=5000
MAX_LIQUIDITY_USD=500000
MAX_TOKEN_AGE_HOURS=24
MIN_POOL_AGE_MINUTES=3
MAX_LP_CONCENTRATION=0.80
POSTBUY_MONITORING_ENABLED=true
POSTBUY_INITIAL_SIZE_PCT=25
POSTBUY_WINDOW_SECONDS=20
CALLSTATIC_ENABLED=true
GAS_COST_BUY_ETH=0.00001
GAS_COST_SELL_ETH=0.00001
```

---

## 🎯 Próximos pasos

1. **Confirmar primer `[CANDIDATO]` → `[PASA FILTROS]` → `[SIM] COMPRA`**
2. Calibrar filtros según primeras señales reales
3. Añadir más wallets a la blacklist según se detecten
4. Actualizar README con arquitectura final real
5. Primera sesión completa con estadísticas

---

## 🧠 Lecciones aprendidas

- Alchemy ignora filtros RPC-side en `eth_subscribe` → siempre filtrar manualmente
- Base de noche dominada por bots de arbitraje con 1 wallet → blacklist crítica
- El topic0 del Swap V2 de Aerodrome Basic es diferente al de Uniswap V3 CL
- El parseo de amounts en Aerodrome V2 Basic: `amount0In + amount1In + amount0Out + amount1Out`
- `isBuy = amount1In > 0 && amount0In == 0` (WETH entra = compra)
- El evento `OnSignalDetected` debe ser `Func<TokenSignal, Task>` para soportar async

---

*Última actualización: 29/04/2026*