# 🔥 15.5 — SniperBot Signal Layer (Base Mainnet)

> **Documento de diseño y arquitectura — v2**
> Incorpora feedback de revisión técnica experta 28/04/2026

---

## 🧠 Por qué existe el 15.5

El Bot 15 tiene buena infraestructura WSS pero escucha el canal equivocado:

| Bot | Señal escuchada | Problema |
|:---|:---|:---|
| Bot 14 (BSC) | PairCreated en PancakeSwap | ✅ BSC genera 50-100 pares nuevos/hora |
| Bot 15 (Base) | PoolCreated en Aerodrome | ❌ Aerodrome genera 2-5 pools nuevos/hora |
| **Bot 15.5** | **Router Swaps + Liquidity Adds** | ✅ 50-200 eventos/hora — señal de comportamiento |

**El 15 no está mal construido — está mirando la puerta equivocada.**

---

## 🎯 Cambio de paradigma

```
ANTES (Bot 14/15):  "evento de creación"
  Factory → PoolCreated → ¿es bueno? → comprar

AHORA (Bot 15.5):   "evento de comportamiento"
  Router → Swap explosivo → ¿es seguro? → comprar
```

En mercados reales, **la creación no da edge — el flujo sí.**

El 15.5 es el primer bot del ecosistema que deja de ser "sniper de eventos" y pasa a ser **"sniper de comportamiento"** — microstructure trading básico.

---

## 📡 Arquitectura — 3 Fuentes de Señal

### Señal 1 — Router Swaps (Principal)

Escuchar el evento `Swap` del Router de Aerodrome en lugar del Factory:

```
Router Aerodrome V2: 0xcF77a3Ba9A5CA399B7c97c74d54e5b1Beb874E43
Evento: Swap(address sender, address to, int256 amount0, int256 amount1, ...)
```

**Lógica:**
- Acumular swaps por token en ventana temporal **30s (micro) + 120s (confirmación)**
- Calcular ratio compras/ventas en tiempo real
- **Filtrar por unique traders** — no solo volumen (ver sección Riesgos)

### Señal 2 — Liquidity Adds (Confirmación)

Escuchar eventos `Mint` (añadir liquidez) en pools de Aerodrome:

**Lógica:**
- Si alguien añade liquidez significativa a un pool pequeño → señal de confianza
- Correlacionar con Señal 1 para confirmar momentum real vs artificial

### Señal 3 — Pool Profile Filter (Anti-rug + Calidad)

Para tokens detectados por Señal 1:
- Pool < 24 horas de antigüedad
- Pool > 3 minutos de vida (anti-rug del minuto 0)
- Liquidez $5K–$500K
- **Liquidez reciente vs histórica** — no solo cantidad (ver sección Riesgos)

---

## 🔬 Pipeline de Decisión

```
[WSS BASE] ──eth_subscribe("logs")──▶ [SIGNAL ENGINE]
                                            │
                              ┌─────────────┼─────────────┐
                              ▼             ▼             ▼
                     [SWAPS 30s+120s]  [LIQ ADDS]   [POOL PROFILE]
                              │             │             │
                     unique traders    correlación    liq quality
                     ratio buy/sell    confirmación   edad + conc.
                              │             │             │
                              └─────────────┴─────────────┘
                                            │
                                   ¿Momentum REAL?
                                   (no wash trading)
                                            │
                              ┌─────────────▼─────────────┐
                              │      GOPLUS SECURITY       │
                              │   (Chain ID 8453 Base)     │
                              └─────────────┬─────────────┘
                                            │
                              ┌─────────────▼─────────────┐
                              │       CALLSTATIC           │
                              │    buy + sell simulado     │
                              │    honeypot técnico        │
                              └─────────────┬─────────────┘
                                            │
                              ┌─────────────▼─────────────┐
                              │  POST-BUY MONITORING 30s  │
                              │  compra 25% → observar    │
                              │  si ok → completar size   │
                              └─────────────┬─────────────┘
                                            │
                                         ✅ COMPRA COMPLETA
                                            │
                              ┌─────────────▼─────────────┐
                              │    TRAILING ESCALONES      │
                              │  +15% / +30% / +50% moon  │
                              └───────────────────────────┘
```

---

## ⚠️ Riesgos identificados y mitigaciones

### Riesgo 1 — Sobrecarga de señal / Wash Trading (CRÍTICO)

**Problema:** Base L2 tiene mucho wash trading en microcaps. Bots entre wallets generan volumen artificial que parece momentum pero no lo es.

**Síntoma:** "parece momentum pero es auto-trading o bots entre wallets"

**Mitigación implementada:**
```
NO solo: volumen de swaps >= umbral
SINO:    unique_traders_60s >= MIN_UNIQUE_TRADERS (mínimo 5 wallets distintas)
         Y volumen >= umbral
```

Añadir al `.env`:
```env
MIN_UNIQUE_TRADERS_60S=5    # Mínimo de wallets distintas en la ventana
```

### Riesgo 2 — Ventana temporal mal calibrada

**Problema:** 60s puede ser demasiado corto (manipulable) o perder movimientos reales.

**Solución — Ventana híbrida:**
```
Micro-momentum:  30s  → detección rápida
Confirmación:   120s  → validación de tendencia

Solo entra si AMBAS ventanas confirman ratio >= 2.0
```

### Riesgo 3 — Calidad de liquidez vs cantidad

**Problema:** Muchos pools "parecen vivos" pero están controlados por 1 entidad. En Base esto es especialmente común.

**Checks de calidad de liquidez:**
- Liquidez añadida en las últimas 2 horas (no liquidez histórica estancada)
- Concentración LP — si 1 wallet tiene > 80% del LP → señal de riesgo
- Verificar que la liquidez NO se retiró parcialmente en los últimos 10 min

### Riesgo 4 — callStatic insuficiente solo

**Problema:** callStatic detecta honeypot técnico pero NO detecta rug diferido, tax dinámico ni blacklist temporal.

**Solución — Post-buy monitoring window:**
```
1. Comprar 25% del size objetivo (0.00125 ETH en lugar de 0.005)
2. Esperar 10-30 segundos observando precio y volumen
3. Si comportamiento normal → completar el 75% restante
4. Si anomalía → salir inmediatamente con pérdida mínima
```

Esto detecta taxes dinámicos y rugs diferidos que callStatic no ve.

---

## 📊 Evolución del ecosistema

| Nivel | Bot | Tipo de sistema |
|:---:|:---|:---|
| 1 | Bot 14 | Event sniper — creación de par |
| 2 | Bot 15 | Infra sniper — WSS L2 |
| **3** | **Bot 15.5** | **Microstructure sniper — flow detection** |
| 4 | Bot 16 | Decision intelligence — scoring + simulation |
| 5 | Bot 17 (futuro) | Market context engine — contexto + detección + decisión |

---

## 🔧 Configuración `.env` (propuesta)

```env
# Conexión
WSS_RPC_URL=wss://base-mainnet.g.alchemy.com/v2/TU_API_KEY
HTTP_RPC_URL=https://base-mainnet.g.alchemy.com/v2/TU_API_KEY
PRIVATE_KEY=tu_clave_privada_sin_0x
SNIPER_ADDRESS=direccion_contrato_en_base
SIMULATION_MODE=true

# Capital y risk
AMOUNT_ETH_PER_TRADE=0.005
TP_PERCENTAGE=40
SL_PERCENTAGE=15
BREAK_EVEN_TRIGGER=15
TRAILING_STOP_PERCENT=10

# Signal Engine — ventana híbrida
MIN_SWAPS_30S=3                  # Micro-momentum: mínimo swaps en 30s
MIN_SWAPS_120S=8                 # Confirmación: mínimo swaps en 120s
MIN_BUY_SELL_RATIO=2.0           # Ratio compras/ventas mínimo
MIN_UNIQUE_TRADERS_60S=5         # Anti wash-trading: wallets distintas

# Filtros de pool
MIN_LIQUIDITY_USD=5000
MAX_LIQUIDITY_USD=500000
MAX_TOKEN_AGE_HOURS=24
MIN_POOL_AGE_MINUTES=3
MAX_LP_CONCENTRATION=0.80        # Rechazar si 1 wallet > 80% del LP

# Post-buy monitoring
POSTBUY_MONITORING_ENABLED=true
POSTBUY_INITIAL_SIZE_PCT=25      # % del size a comprar inicialmente
POSTBUY_WINDOW_SECONDS=20        # Segundos de observación antes de completar

# Seguridad
CALLSTATIC_ENABLED=true

# Gas L2
GAS_COST_BUY_ETH=0.00001
GAS_COST_SELL_ETH=0.00001
```

---

## 📋 Plan de implementación

| Fase | Tarea | Prioridad | Novedad vs Bot 15 |
|:---:|:---|:---:|:---:|
| 1 | WSS heredado del Bot 15 | ✅ | No |
| 2 | **Signal Engine: acumular Swap events por token** | 🔴 Crítico | Sí |
| 3 | **Ventana híbrida 30s + 120s** | 🔴 Crítico | Sí |
| 4 | **Unique traders filter (anti wash-trading)** | 🔴 Crítico | Sí |
| 5 | GoPlus por token con momentum confirmado | 🟡 Alto | No |
| 6 | **callStatic pre-trade** | 🟡 Alto | Sí |
| 7 | **Post-buy monitoring window (25% size inicial)** | 🟡 Alto | Sí |
| 8 | **Liquidity quality check** | 🟡 Alto | Sí |
| 9 | Trailing stop por escalones (heredar de Bot 14) | ✅ | No |
| 10 | Primera operación simulada completa | ⏳ | — |

---

## 🚀 Lo que viene después — Bot 16 (contexto de mercado)

El feedback experto apunta al siguiente salto real:

```
Ahora (15.5):   detección → decisión → ejecución
Futuro (16):    detección → CONTEXTO DE MERCADO → decisión → ejecución
```

El Bot 16 no es otro sniper — es un **sistema de scoring dinámico** que decide si el 15.5 debe o no operar en ese momento de mercado. Incluye:
- ¿Hay alta actividad general en Base o está muerto?
- ¿El token detectado está en tendencia con el mercado o contra él?
- ¿Las últimas N operaciones del día fueron positivas o negativas?

Esto convierte el sistema de "siempre caza" a "caza cuando las condiciones son favorables".

---

## 🔗 Posición en el ecosistema

| Bot | Red | Señal | Estado |
|:---:|:---|:---|:---:|
| 14 | BSC | PairCreated → nuevo token | ✅ En producción (sim) |
| 15 | Base | PoolCreated Aerodrome | ⚠️ Señal débil — pausado |
| **15.5** | **Base** | **Router swaps → momentum** | **🔄 Diseño v2** |
| 16 | BSC+Base | Bot 14 V3 + callStatic + scoring | ⏳ Próximo |

---

## 📅 Prerequisitos antes de codificar

1. ✅ Bot 14 corriendo en simulación varios días → baseline de resultados
2. ✅ Bot 15 diagnóstico completado → problema confirmado: señal débil, no infra
3. ⏳ Verificar Router address correcto de Aerodrome V2 en Base y ABI del evento `Swap`
4. ⏳ Confirmar que Alchemy permite `eth_subscribe("logs")` con volumen alto de swaps sin rate limit

**Tiempo estimado de desarrollo:** 3-4 sesiones de trabajo

---

*Documento creado: 28/04/2026 — v1*
*Actualizado: 28/04/2026 — v2 (incorpora feedback técnico experto)*
