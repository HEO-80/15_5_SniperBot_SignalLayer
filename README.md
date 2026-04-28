<div align="center">

# 🔥 15_5_SniperBot_SignalLayer — Microstructure Token Sniper

<img src="https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=csharp&logoColor=white"/>
<img src="https://img.shields.io/badge/.NET_10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white"/>
<img src="https://img.shields.io/badge/Base_Mainnet-0052FF?style=for-the-badge&logo=coinbase&logoColor=white"/>
<img src="https://img.shields.io/badge/Aerodrome-FF0420?style=for-the-badge&logo=aerodrome&logoColor=white"/>
<img src="https://img.shields.io/badge/GoPlus_Security-FF4444?style=for-the-badge&logo=shield&logoColor=white"/>
<img src="https://img.shields.io/badge/Alchemy_WSS-363636?style=for-the-badge&logo=alchemy&logoColor=white"/>

*Microstructure sniper — Flow detection · Anti wash-trading · Post-buy monitoring · callStatic · Trailing escalones*

</div>

---

> ⚠️ **ADVERTENCIA DE RIESGO**
> Este software opera en mercados de criptomonedas con dinero real. El trading algorítmico en Base Mainnet conlleva **riesgo de pérdida total del capital**. Por defecto el bot está en `SIMULATION_MODE=true` y no ejecuta transacciones reales. **Opera únicamente con capital que puedas permitirte perder.**

---

## 📝 Descripción

**15_5_SniperBot_SignalLayer** es la evolución del [15_SniperBot_L2_WSS](https://github.com/HEO-80/15_SniperBot_L2_WSS) y representa un **cambio de paradigma** en la detección de oportunidades.

En lugar de escuchar eventos de creación de pools (`PoolCreated` — 2-5 eventos/hora en Aerodrome), el Signal Layer escucha **comportamiento de mercado en tiempo real**: swaps del router, añadidos de liquidez y patrones de flujo — generando 50-200 señales/hora con calidad filtrable.

```
ANTES (Bot 15):   "evento de creación"
  Factory → PoolCreated → ¿es bueno? → comprar       [señal débil]

AHORA (Bot 15.5): "evento de comportamiento"
  Router → Swap explosivo → ¿es seguro? → comprar    [señal fuerte]
```

> En mercados reales, **la creación no da edge — el flujo sí.**

---

## 🆚 Posición en el ecosistema

| Bot | Red | Tipo | Señal | Estado |
|:---:|:---|:---|:---|:---:|
| 14 | BSC | Event sniper | PairCreated → nuevo token | ✅ Producción sim |
| 15 | Base | Infra sniper | PoolCreated Aerodrome | ⚠️ Señal débil |
| **15.5** | **Base** | **Microstructure sniper** | **Router swaps → momentum** | 🔄 En desarrollo |
| 16 | BSC+Base | Decision intelligence | Scoring + callStatic | ⏳ Próximo |

---

## 🔬 Pipeline de Decisión (10 Fases)

```
[WSS BASE] ──eth_subscribe("logs")──▶ [INGESTA RAW]
                                            │
                                    [SIGNAL ENGINE]
                                    acumula por token:
                                    swaps 30s + 120s
                                    buys vs sells
                                    unique wallets
                                            │
                                   ¿Momentum real?
                                   (anti wash-trading)
                                            │
                                   [POOL PROFILE]
                                   liquidez + edad
                                   LP concentration
                                            │
                                   [GOPLUS SECURITY]
                                   Chain ID 8453
                                            │
                                   [CALLSTATIC]
                                   buy + sell simulado
                                   honeypot check
                                            │
                                   [POST-BUY 25%]
                                   observar 10-30s
                                   anomalía → exit
                                   ok → completar size
                                            │
                                        ✅ COMPRA
                                            │
                                   [TRAILING ESCALONES]
                                   +15% / +30% / +50% moon
```

---

## 🏗️ Fases de implementación

### ✅ FASE 0 — Base (WSS + Logger + Config)

Objetivo: que el sistema "respire" de forma estable.

- WSS persistente con reconexión automática si cae
- Logger con niveles: `INFO` / `DEBUG` / `RAW`
- Config `.env` cargada y validada al arranque

**Checks:**
- Logs en tiempo real sin cortes
- Reconexión automática ante caída WSS
- Logs activables: `[WSS-RAW]`, `[SIGNAL]`, `[FILTER]`, `[REJECT_REASON]`

---

### ✅ FASE 1 — Ingesta de datos (Signal Input)

Objetivo: ver el flujo REAL de Base Mainnet.

- Listener WSS sin filtro RPC-side (Alchemy ignora filtros server-side)
- Filtro manual por `address` (router) y `topic0` (Swap / Mint)
- Parseo y almacenamiento de cada swap:

```csharp
// Por cada swap detectado
token, amountIn, amountOut, wallet (sender), timestamp, direction (buy/sell)
```

**Checks:**
- `[WSS-RAW]` aparece constantemente — swaps reales llegando
- No hay 0 eventos — si hay 0, el filtro está roto
- Logs tipo: `[WSS-RAW] token=0xABC | swap | buyer=0x123 | amountIn=0.01 ETH`

---

### 🔄 FASE 2 — Signal Engine (core del 15.5)

Objetivo: convertir ruido en señal.

```csharp
Dictionary<string, List<SwapEvent>> _swapBuffer
```

Por cada token acumula eventos y calcula en ventana híbrida:

| Ventana | Métrica | Umbral |
|:---|:---|:---:|
| 30s (micro) | nº swaps | >= `MIN_SWAPS_30S` |
| 120s (confirmación) | nº swaps | >= `MIN_SWAPS_120S` |
| 60s | ratio buys/sells | >= `MIN_BUY_SELL_RATIO` |
| 60s | unique wallets | >= `MIN_UNIQUE_TRADERS_60S` |

**Log de señal:**
```
[SIGNAL] TOKEN_X → swaps30=5 | swaps120=12 | ratio=2.5 | wallets=6 ✅
[SIGNAL] TOKEN_Y → swaps30=8 | swaps120=20 | ratio=1.1 | wallets=2 ❌ wash-trading
```

---

### ⏳ FASE 3 — Filtro de Momentum

Objetivo: de 100 tokens candidatos, que pasen 5-10 máximo.

```
swaps30   >= MIN_SWAPS_30S       (default: 3)
swaps120  >= MIN_SWAPS_120S      (default: 8)
ratio     >= MIN_BUY_SELL_RATIO  (default: 2.0)
wallets   >= MIN_UNIQUE_TRADERS  (default: 5)  ← anti wash-trading
```

**Calibración:**
- Si pasan > 20 tokens/hora → filtros demasiado débiles
- Si pasan 0 tokens/hora → filtros rotos o señal incorrecta
- Target: 5-15 candidatos/hora

---

### ⏳ FASE 4 — Pool Profile

Objetivo: evitar pools con liquidez falsa o controlada.

- Liquidez actual: $5K–$500K
- Edad del pool: < 24 horas, > 3 minutos
- Par con WETH obligatorio
- **LP concentration:** rechazar si 1 wallet > 80% del LP

**Log:**
```
[FILTER] TOKEN_X → liq=$45K | age=2h | LP ok ✅
[FILTER] TOKEN_Y → liq=$800K → REJECT: over max liq
[FILTER] TOKEN_Z → LP concentration 92% → REJECT: controlled pool
```

---

### ⏳ FASE 5 — GoPlus Security

Checks estándar sobre Base (Chain ID 8453):

| Check | Condición de rechazo |
|:---|:---|
| `is_mintable` | = 1 |
| `trading_cooldown` | = 1 |
| `transfer_pausable` | = 1 |
| `cannot_sell_all` | = 1 |
| `buy_tax` / `sell_tax` | > 0% |

Si GoPlus falla o no responde → **NO ENTRAR. Nunca asumir limpio.**

---

### ⏳ FASE 6 — callStatic Pre-Trade

Objetivo: detectar honeypots que GoPlus no ve.

```
1. Simular compra → X tokens recibidos
2. Simular venta de X tokens → Y ETH devuelto
3. Si Y == 0 → REJECT: honeypot técnico
4. Si llamada revierte → REJECT
```

**Log:**
```
[CALLSTATIC] TOKEN_X ✅ buy→sell ok | recuperado: 98.5%
[CALLSTATIC] TOKEN_Y ❌ sell devuelve 0 → honeypot
```

---

### ⏳ FASE 7 — Post-Buy Monitoring (edge real)

Objetivo: detectar rugs diferidos y taxes dinámicos que callStatic no ve.

```
1. Comprar 25% del size objetivo (0.00125 ETH)
2. Esperar 10-30 segundos
3. Monitorizar: precio, volumen, slippage
4. Decisión:
   → Comportamiento normal → completar 75% restante
   → Anomalía detectada   → salir inmediatamente
```

**Log:**
```
[POSTBUY] TOKEN_X | observando... comportamiento normal → completando size
[POSTBUY] TOKEN_Y | anomalía detectada (precio -15% en 10s) → exit inmediato
```

---

### ⏳ FASE 8 — Execution + Trailing Escalones

Heredado directamente del Bot 14 — no reinventar nada.

| PnL Alcanzado | Stop Garantizado | Comportamiento |
|:---|:---|:---|
| < +15% | SL fijo en -15% | Protección estándar |
| ≥ +15% | Stop anclado en **+15%** | Timeout desactivado |
| ≥ +30% | Stop anclado en **+30%** | Ganancia asegurada |
| ≥ +50% | Trailing **10%** sobre pico | Modo Moon |

---

### ⏳ FASE 9 — Stats y Métricas

Trackear obligatoriamente:

```
Señales detectadas por hora
Señales que pasan momentum filter
Señales que pasan pool profile
Señales que pasan GoPlus
Señales que pasan callStatic
Trades ejecutados
Post-buy: completados vs abortados
Win rate neto
PnL neto (con gas)
```

---

### ⏳ FASE 10 — Reason Logging (crítico para calibración)

Saber exactamente **por qué NO se entra** — aquí está la verdad del sistema.

```
[REJECT] TOKEN_X → low_swaps_30s (2 < 3)
[REJECT] TOKEN_Y → low_ratio (1.3 < 2.0)
[REJECT] TOKEN_Z → low_wallets (3 < 5) — posible wash-trading
[REJECT] TOKEN_A → goplus_fail
[REJECT] TOKEN_B → callstatic_honeypot
[REJECT] TOKEN_C → postbuy_anomaly
```

**Regla:** si el 95% de rechazos son por la misma razón → ese filtro está mal calibrado.

---

## 🔧 Configuración (`.env`)

```env
# Conexión
WSS_RPC_URL=wss://base-mainnet.g.alchemy.com/v2/TU_API_KEY
HTTP_RPC_URL=https://base-mainnet.g.alchemy.com/v2/TU_API_KEY
PRIVATE_KEY=tu_clave_privada_sin_0x
SNIPER_ADDRESS=
SIMULATION_MODE=true

# Capital y risk
AMOUNT_ETH_PER_TRADE=0.005
TP_PERCENTAGE=40
SL_PERCENTAGE=15
BREAK_EVEN_TRIGGER=15
TRAILING_STOP_PERCENT=10

# Signal Engine — ventana híbrida
MIN_SWAPS_30S=3
MIN_SWAPS_120S=8
MIN_BUY_SELL_RATIO=2.0
MIN_UNIQUE_TRADERS_60S=5

# Filtros de pool
MIN_LIQUIDITY_USD=5000
MAX_LIQUIDITY_USD=500000
MAX_TOKEN_AGE_HOURS=24
MIN_POOL_AGE_MINUTES=3
MAX_LP_CONCENTRATION=0.80

# Post-buy monitoring
POSTBUY_MONITORING_ENABLED=true
POSTBUY_INITIAL_SIZE_PCT=25
POSTBUY_WINDOW_SECONDS=20

# Seguridad
CALLSTATIC_ENABLED=true

# Gas L2
GAS_COST_BUY_ETH=0.00001
GAS_COST_SELL_ETH=0.00001
```

---

## 🚀 Instalación

**Prerrequisitos:** .NET 10.0 SDK · Burner wallet con ETH en Base · API Key Alchemy con Base Mainnet

```bash
git clone https://github.com/HEO-80/15_5_SniperBot_SignalLayer.git
cd 15_5_SniperBot_SignalLayer/15_5_SniperBot_SignalLayer
dotnet restore
cp .env.example .env
# Edita .env con tus valores
dotnet run
```

---

## 📁 Estructura del Proyecto

```
15_5_SniperBot_SignalLayer/
├── 15_5_SniperBot_SignalLayer/
│   ├── Program.cs                       # Entrypoint + config
│   ├── Services/
│   │   ├── WssConnectionService.cs      # WSS handler + reconexión
│   │   ├── SignalEngine.cs              # Core — acumula swaps, calcula señales
│   │   ├── MomentumFilterService.cs     # Filtros 30s/120s/ratio/wallets
│   │   ├── PoolProfileService.cs        # Liquidez, edad, LP concentration
│   │   ├── GoPlusService.cs             # Auditoría GoPlus (Chain 8453)
│   │   ├── CallStaticService.cs         # Simulación pre-trade
│   │   ├── PostBuyMonitorService.cs     # Monitoring post-compra 25%
│   │   ├── PriceMonitorService.cs       # Trailing stop por escalones
│   │   ├── TradingService.cs            # Ejecución de swaps en Base
│   │   ├── GasOracleL2.cs              # Gas EIP-1559 L2
│   │   ├── Logger.cs                    # INFO / DEBUG / RAW
│   │   └── StatsTracker.cs             # Métricas completas de sesión
│   ├── Models/
│   │   ├── SwapEvent.cs                # Datos de un swap detectado
│   │   ├── TokenSignal.cs              # Señal agregada por token
│   │   ├── MonitoringConfig.cs         # Config trailing/TP/SL
│   │   └── FilterConfig.cs             # Config filtros
│   └── logs/                           # Logs en runtime (.gitignore)
├── .env.example
├── .gitignore
└── README.md
```

---

## ⚠️ Riesgos conocidos y mitigaciones

| Riesgo | Impacto | Mitigación |
|:---|:---:|:---|
| Wash trading / bots entre wallets | Alto | `MIN_UNIQUE_TRADERS_60S=5` |
| Ventana temporal manipulable | Medio | Ventana híbrida 30s + 120s |
| Liquidez controlada por 1 entidad | Alto | `MAX_LP_CONCENTRATION=0.80` |
| Honeypot técnico | Alto | callStatic pre-trade |
| Rug diferido / tax dinámico | Medio | Post-buy monitoring 25% |
| Señal alta pero mercado muerto | Medio | Stats + calibración manual |

---

## 🧠 Lecciones del Bot 15

- Alchemy ignora filtros RPC-side en `eth_subscribe("logs")` → filtrar siempre manualmente
- Aerodrome V2 en Base genera muy pocos `PoolCreated` → señal insuficiente para trading
- La arquitectura WSS es correcta — el problema era la **fuente de señal**, no la infraestructura
- `[WSS-RAW]` logs son críticos para diagnóstico — sin ellos no sabes si recibes datos o no

---

## ⚖️ Disclaimer

Este proyecto es para **investigación y aprendizaje en DeFi** exclusivamente. Los autores no se responsabilizan de pérdidas financieras ni daños derivados del uso de este software.

---

## 🧑‍💻 Autor

**Héctor Oviedo** — Backend Developer & DeFi Researcher

[![LinkedIn](https://img.shields.io/badge/LinkedIn-0077B5?style=for-the-badge&logo=linkedin&logoColor=white)](https://www.linkedin.com/in/hectorob/)
[![GitHub](https://img.shields.io/badge/GitHub-181717?style=for-the-badge&logo=github&logoColor=white)](https://github.com/HEO-80)

---

<div align="center">
  <sub>Built with ☕ and MEV research · <strong>Héctor Oviedo</strong> · Zaragoza, España</sub>
</div>