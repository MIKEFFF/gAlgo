# NNFXBOT: cTrader Algorithmic Trading Strategy

[![Platform](https://img.shields.io/badge/Platform-cTrader%20%2F%20cAlgo-blue.svg)](https://spotware.com/)
[![Language](https://img.shields.io/badge/Language-C%23%20%2F%20.NET-informational.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**NNFXBOT** is an automated algorithmic trading robot (cBot) designed for **cTrader / cAlgo** (C#). Inspired by the **No Nonsense Forex (NNFX)** framework, the algorithm combines price action candle analysis, multi-layered technical indicator confirmation, volatility-adjusted position sizing, news event avoidance, and automated performance analytics.

---

## Architecture & Strategy Overview

The robot executes trades through a strict multi-stage confirmation pipeline:

```
                  ┌──────────────────────────────┐
                  │          New Tick            │
                  └──────────────┬───────────────┘
                                 │
                 [ Trading Hours & News Check ]
                                 │
             ┌───────────────────┴───────────────────┐
             ▼                                       ▼
    [ BUY Setup Check ]                     [ SELL Setup Check ]
  • 2-Candle Price Action Pattern         • 2-Candle Price Action Pattern
  • Bollinger Band Expansion & Bounds     • Bollinger Band Expansion & Bounds
  • Trigger Price Confirmation            • HMA Baseline + Chaikin Money Flow
             │                                       │
             └───────────────────┬───────────────────┘
                                 ▼
                     [ Dynamic Risk Sizing ]
                  ATR-Based Stop Loss & Sizing
                                 ▼
                    [ Market Order Execution ]
```

### 1. Price Action Recognition (2-Candle Reversal Pattern)
- **Long (`isPossibleBuy`)**: Identifies a bearish/doji candle followed by a bullish candle whose low touches/exceeds the previous low, with significant lower wicks confirming buying pressure.
- **Short (`isPossibleSell`)**: Identifies a bullish candle followed by a bearish candle with upper wick rejection confirming selling pressure.

### 2. Multi-Indicator Confirmation System
- **Baseline Filter**: **Hull Moving Average (HMA)** determines the macro trend direction.
- **Volatility & Expansion Filter**: **Dual Bollinger Bands** (Long & Short configurations) verify volatility expansion (`Bandwidth(t) > Bandwidth(t-2)`) and ensure price has room for mean reversion.
- **Volume Filter**: **Chaikin Money Flow (CMF)** confirms institutional volume direction.
- **Volatility Normalization**: 14-period **Average True Range (ATR)** standardizes Stop Loss, Take Profit, and pattern sizing across different assets.

### 3. Risk Management & Execution
- **Dynamic Lot Sizing**: Computes position volume based on fixed account equity risk fraction (`Risk Fraction`, default: `0.15` / 15%) divided by the ATR stop distance in pips.
- **Trade Duration Limits**: Automatically closes stagnant trades if they exceed a configured time threshold (`TradeLengthLimitHours`).
- **Trading Hours Filter**: Restricts execution to active market sessions (`01:00` - `23:00` server time).
- **Economic News Calendar Filter**: Skips entries or adjusts volatility thresholds around high-impact economic calendar releases (`calendar-event-list.csv`).

---

## Performance & Statistical Analytics

Upon stopping or backtest completion (`OnStop()`), **NNFXBOT** automatically analyzes closed trades and logs a performance report:

- **Total Returns (%)**: Net percentage return relative to initial balance.
- **Profit Factor**: Gross profits divided by gross losses.
- **Sharpe Ratio**: Annualized return-to-volatility ratio ($R_f$ adjusted).
- **Martin Ratio (Ulcer Performance Index)**: Annualized return relative to maximum drawdown.
- **Maximum Drawdown**: Peak equity drawdown recorded in account currency.
- **Win Rate (%)**: Percentage of winning vs losing trades.

---

## Strategy Parameters

All parameters can be tuned directly inside the cTrader Automate UI:

| Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- |
| `Status Message` | `string` | `"NNFX Bot Active"` | Diagnostic message output in cTrader logs |
| `Buffer Minutes` | `int` | `30` | News blackout buffer window in minutes |
| `Risk Fraction` | `double` | `0.15` | Risk fraction of equity per trade |
| `TP Multiplier` | `double` | `2.0` | ATR multiplier for Take Profit targets |
| `SL Multiplier` | `double` | `3.0` | ATR multiplier for Stop Loss targets |
| `Long SL Pips` | `double` | `2.0` | Multiplier for Long Bollinger trade Stop Loss |
| `Short SL Pips` | `double` | `2.0` | Multiplier for Short Bollinger trade Stop Loss |
| `HmaPeriod` | `int` | `50` | Hull Moving Average baseline period |
| `ChaikinPeriod` | `int` | `19` | Chaikin Money Flow period |
| `TradeLengthLimitHours` | `int` | `3` | Maximum hours to hold an unmodified trade |
| `TradeLengthLimitMinutes` | `int` | `0` | Additional minutes for trade lifetime limit |
| `BbPeriodsLong` | `int` | `21` | Period for Long Bollinger Bands |
| `BbPeriodsShort` | `int` | `15` | Period for Short Bollinger Bands |
| `StdDevsLong` | `double` | `1.8` | Standard deviation multiplier for Long BB |
| `StdDevsShort` | `double` | `1.9` | Standard deviation multiplier for Short BB |

---

## Project Structure

```
├── src/
│   └── NNFXBOT.cs                    # Main cTrader Robot (cBot) source code
├── data/
│   └── calendar-event-list.sample.csv # Example economic news calendar format
├── .gitignore                        # C# / .NET / cTrader git ignore definitions
├── CONTRIBUTING.md                   # Contribution and testing guidelines
├── LICENSE                           # MIT License
└── README.md                         # Project documentation
```

---

## Getting Started

### Prerequisites
- **[cTrader Desktop](https://spotware.com/)** (Windows) or compatible cTrader Automate environment.
- **.NET 6.0 / .NET Framework** (managed automatically by cTrader).

### Installation in cTrader
1. Open **cTrader**.
2. Navigate to the **Automate** tab (robot icon on the left sidebar).
3. Click **New cBot** and name it `NNFXBOT`.
4. Copy and paste the contents of [`src/NNFXBOT.cs`](src/NNFXBOT.cs) into the cTrader code editor.
5. Click **Build** (`Ctrl + B` or the hammer icon).

### News Calendar Setup (Optional)
To enable the high-impact news filter:
1. Export or download your economic calendar CSV (e.g. from ForexFactory, DailyFX, or Investing.com).
2. Place the CSV file at:
   - **Windows**: `%AppData%\cAlgo\excel\calendar-event-list.csv`
   - Refer to [`data/calendar-event-list.sample.csv`](data/calendar-event-list.sample.csv) for expected date formats (`MM/dd/yyyy HH:mm:ss`).

---

## Backtesting & Optimization

1. In cTrader Automate, select `NNFXBOT`.
2. Add an instance to your target symbol (e.g. `EURUSD`, `GBPUSD`) and desired timeframe (e.g., `h1` or `m15`).
3. Set your backtest period, tick data quality (**Tick Data from Server (accurate)** recommended), and initial balance.
4. Click **Backtesting** to run historical simulations and review the **Strategy Performance Report** in the output log.

---

## Disclaimer

> [!WARNING]
> **Trading Foreign Exchange (Forex) and Contracts for Difference (CFDs) carries a high level of risk to your capital.** 
> This software is provided for educational, research, and development purposes only. Past backtest performance is no guarantee of future results. Always thoroughly test any trading algorithm on a demo account before risking real funds.

---

## License

This project is open-source and licensed under the [MIT License](LICENSE).
