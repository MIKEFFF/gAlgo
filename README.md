# AegisTrader: Quantitative Multi-Factor Algorithmic Execution System

[![Platform](https://img.shields.io/badge/Platform-cTrader%20%2F%20cAlgo-blue.svg)](https://spotware.com/)
[![Language](https://img.shields.io/badge/Language-C%23%20%2F%20.NET-informational.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Quantitative Strategy](https://img.shields.io/badge/Strategy-Multi--Factor%20Systematic-success.svg)]()

**AegisTrader** is a high-performance, systematic quantitative trading engine engineered in C# for the **cTrader (cAlgo)** institutional trading ecosystem. The architecture implements a multi-stage signal generation pipeline combining price action microstructure analysis, trend baseline filtering, statistical volatility expansion boundaries, volume flow confirmation, dynamic risk budgeting, and real-time macroeconomic event avoidance.

---

## 💼 CV / Resume Highlights

If presenting this project on a resume or portfolio, here is a concise description and set of bullet points:

> **AegisTrader — Quantitative Algorithmic Execution Engine (C# / .NET / cTrader)**
> - Engineered an automated multi-factor systematic FX trading system in C# utilizing event-driven architecture and asynchronous market data feeds.
> - Implemented statistical volatility boundaries (Dual Bollinger Bands) and trend-filtering mechanisms (Hull Moving Average & Chaikin Money Flow) to identify high-probability mean-reversion and momentum setups.
> - Designed a dynamic risk budgeting module calculating real-time volatility-adjusted position sizing (ATR-normalized) and enforcing temporal drawdown constraints.
> - Integrated an automated macroeconomic event filter parsing calendar feeds to eliminate high-impact release exposure and mitigate tail risk.
> - Built a real-time post-trade performance analytics module computing Sharpe ratio, Ulcer/Martin index, maximum equity drawdown, and profit factors on termination.

---

## 🏛 System Architecture & Signal Pipeline

The execution engine processes incoming ticks through a hierarchical validation pipeline:

```
                  ┌─────────────────────────────────────┐
                  │       Real-Time Market Tick         │
                  └──────────────────┬──────────────────┘
                                     │
                    [ Session & Macro Event Filters ]
                                     │
             ┌───────────────────────┴───────────────────────┐
             ▼                                               ▼
    [ BUY Factor Pipeline ]                         [ SELL Factor Pipeline ]
  • Microstructure Wick Reversal                  • Microstructure Wick Reversal
  • Statistical Band Expansion (BB)               • Statistical Band Expansion (BB)
  • Dynamic Trigger Validation                    • Trend Baseline (HMA) + Volume (CMF)
             │                                               │
             └───────────────────────┬───────────────────────┘
                                     ▼
                     [ Dynamic Risk Budgeting Engine ]
                       ATR-Adjusted Position Sizing
                                     ▼
                      [ Normalized Order Execution ]
```

---

## 🔬 Quantitative Signal Architecture

### 1. Price Action Microstructure Reversal
- **Long Formation**: Identifies bearish exhaustion followed by bullish absorption with significant lower wick extension relative to prevailing volatility (`Symbol.PipSize`).
- **Short Formation**: Identifies bullish exhaustion followed by bearish rejection with upper wick extension confirming selling pressure.

### 2. Multi-Factor Confirmation Matrix
- **Trend Baseline Factor**: 50-period **Hull Moving Average (HMA)** establishes macro regime bias with reduced lag.
- **Statistical Dispersion & Expansion**: **Dual Bollinger Bands** evaluate volatility expansion (`Bandwidth(t) > Bandwidth(t-2)`) and ensure price has room for mean-reversion without entering saturated extremes.
- **Volume Flow Factor**: **Chaikin Money Flow (CMF)** measures institutional accumulation/distribution pressure.
- **Volatility Parity & Normalization**: 14-period **Average True Range (ATR)** standardizes dynamic stop distances and profit targets across various currency pairs.

### 3. Risk Budgeting & Capital Protection
- **Volatility-Parity Position Sizing**: Automatically sizes order volume as a fixed percentage of total equity (`RiskFraction`) divided by the ATR-derived stop distance.
- **Temporal Lifetime Limits**: Forces liquidation of dormant or stagnant positions exceeding maximum holding duration (`TradeLengthLimitHours`).
- **Macroeconomic Event Filter**: Parses economic calendar feeds (`calendar-event-list.csv`) to enforce trading blackouts around major central bank rate decisions, CPI releases, and NFP prints.

---

## 📊 Performance & Statistical Analytics

Upon robot termination or backtest completion (`OnStop()`), **AegisTrader** compiles a quantitative summary:

- **Total Return on Equity (%)**: Net percentage return relative to initial balance.
- **Profit Factor**: Ratio of gross profits to gross losses.
- **Annualized Sharpe Ratio**: Risk-adjusted excess return normalized by standard deviation of returns.
- **Martin (Ulcer Performance) Ratio**: Risk metric measuring return relative to depth and duration of drawdowns.
- **Maximum Drawdown**: Maximum peak-to-trough equity decline recorded during execution.
- **Win Rate (%)**: Proportion of profitable vs unprofitable closed trades.

---

## ⚙️ Parameters & Configuration

Parameters are structured into clean groups within the cTrader Automate panel:

| Group | Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| **General** | `System Status` | `string` | `"AegisTrader Active"` | Diagnostic status message |
| **Risk Budgeting** | `Risk Fraction (of Equity)` | `double` | `0.02` | Risk allocation per trade (e.g. `0.02` = 2% of equity) |
| **Risk Budgeting** | `TP Multiplier (ATR)` | `double` | `2.0` | ATR multiplier for Take Profit targets |
| **Risk Budgeting** | `SL Multiplier (ATR)` | `double` | `3.0` | ATR multiplier for Stop Loss targets |
| **Risk Budgeting** | `Long BB SL Multiplier` | `double` | `2.0` | Stop loss multiplier for Long Bollinger trades |
| **Risk Budgeting** | `Short BB SL Multiplier` | `double` | `2.0` | Stop loss multiplier for Short Bollinger trades |
| **Technical Factors**| `ATR Period` | `int` | `14` | Exponential Average True Range lookback |
| **Technical Factors**| `HMA Baseline Period` | `int` | `50` | Hull Moving Average baseline period |
| **Technical Factors**| `Chaikin Volume Period` | `int` | `19` | Chaikin Money Flow volume lookback |
| **Statistical Bands**| `Long BB Period` | `int` | `21` | Period for Long statistical bands |
| **Statistical Bands**| `Long BB StdDev` | `double` | `1.8` | Standard deviation multiplier (Long) |
| **Statistical Bands**| `Short BB Period` | `int` | `15` | Period for Short statistical bands |
| **Statistical Bands**| `Short BB StdDev` | `double` | `1.9` | Standard deviation multiplier (Short) |
| **Execution Windows**| `Session Start Hour (UTC)` | `int` | `1` | Session start hour |
| **Execution Windows**| `Session End Hour (UTC)` | `int` | `23` | Session end hour |
| **Execution Windows**| `Max Holding Period (Hours)`| `int` | `3` | Maximum position lifetime in hours |
| **Execution Windows**| `Max Holding Period (Mins)` | `int` | `0` | Additional position lifetime in minutes |
| **Macro Event Filter**| `News Blackout Window (Mins)`| `int` | `30` | Blackout duration before and after macro events |
| **Macro Event Filter**| `Enable News Event Filter` | `bool` | `true` | Toggle calendar event blackout |

---

## 📁 Repository Structure

```
├── src/
│   └── AegisTrader.cs                 # Main quantitative execution engine
├── data/
│   └── calendar-event-list.sample.csv # Sample macroeconomic calendar format
├── .gitignore                         # Comprehensive C#, .NET & cTrader ignore rules
├── CONTRIBUTING.md                    # Research and contribution guidelines
├── LICENSE                            # Open-source MIT License
└── README.md                          # Technical documentation and CV overview
```

---

## 🚀 Getting Started

### Requirements
- **cTrader Desktop** (Windows) or compatible cTrader Automate CLI.
- **.NET SDK** (managed automatically by cTrader).

### Running in cTrader
1. Launch **cTrader**.
2. Open the **Automate** tab.
3. Click **New cBot** and name it `AegisTrader`.
4. Replace the template with the contents of [`src/AegisTrader.cs`](src/AegisTrader.cs).
5. Press `Ctrl + B` to build the cBot.

### Economic Calendar Setup (Optional)
To enable macroeconomic event filtering:
1. Export high-impact economic news releases as a CSV.
2. Save the file to `%AppData%\cAlgo\excel\calendar-event-list.csv`.
3. Refer to [`data/calendar-event-list.sample.csv`](data/calendar-event-list.sample.csv) for column formatting.

---

## ⚠️ Disclaimer

> [!WARNING]
> **Trading financial instruments and leveraged foreign exchange carries substantial risk of capital loss.**
> This project is designed for quantitative research, backtesting, and educational demonstration. Past performance in backtesting is not indicative of future market results.

---

## 📜 License

This project is open-source under the [MIT License](LICENSE).
