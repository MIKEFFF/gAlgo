# gAlgo: Quantitative Multi-Factor Algorithmic Execution System

[![Platform](https://img.shields.io/badge/Platform-cTrader%20%2F%20cAlgo-blue.svg)](https://spotware.com/)
[![Language](https://img.shields.io/badge/Language-C%23-informational.svg)](https://learn.microsoft.com/csharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Quantitative Strategy](https://img.shields.io/badge/Strategy-Multi--Factor%20Systematic-success.svg)]()

**gAlgo** is a systematic quantitative algorithmic trading engine engineered in C# for the **cTrader (cAlgo)** platform. The architecture implements an automated multi-stage execution pipeline combining price action microstructure analysis, trend baseline filtering, statistical volatility expansion boundaries, volume flow confirmation, dynamic risk budgeting, and real-time macroeconomic event avoidance.

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

Upon robot termination or backtest completion (`OnStop()`), **gAlgo** compiles a quantitative summary:

- **Total Return on Equity (%)**: Net percentage return relative to initial balance.
- **Profit Factor**: Ratio of gross profits to gross losses.
- **Annualized Sharpe Ratio**: Risk-adjusted excess return normalized by standard deviation of returns.
- **Martin (Ulcer Performance) Ratio**: Risk metric measuring return relative to depth and duration of drawdowns.
- **Maximum Drawdown**: Maximum peak-to-trough equity decline recorded during execution.
- **Win Rate (%)**: Proportion of profitable vs unprofitable closed trades.

---

## 💡 Research Reflection: Backtesting & Overfitting Analysis

A central phase of developing **gAlgo** involved extensive historical simulation using **5 years worth of tick and bar data**. While initial testing yielded compelling results, subsequent evaluation provided essential insights into the realities and pitfalls of quantitative strategy development:

### 1. In-Sample Optimization & High Expectations
During the initial backtesting phase spanning 5 years of historical market data, the multi-factor model produced strong in-sample equity curves, high win rates, and attractive Sharpe ratios. The confluence of HMA trend baseline, dual Bollinger statistical boundaries, and price-action wick confirmations appeared to effectively capture recurring market patterns.

### 2. The Overfitting (Curve-Fitting) Trap
Through continuous optimization—fine-tuning indicator lookbacks (HMA periods, Chaikin parameters, Bollinger standard deviations) and adjusting trade lifetime rules—the strategy inadvertently fell into **overfitting**. Rather than identifying a universal, persistent structural market inefficiency, the parameters were calibrated too specifically to the unique market regimes, noise, and volatility structures of that 5-year sample.

### 3. Out-of-Sample Performance Degradation
When subsequently subjected to rigorous **out-of-sample data** (unseen market periods and differing macroeconomic regimes), the strategy did not perform well:
- Signal reliability deteriorated significantly, leading to false breakouts and premature exits.
- Drawdowns exceeded historical in-sample limits, while the profit factor compressed toward breakeven.
- The tightly fitted rules proved too fragile to adapt to structural regime shifts and varying volatility environments.

### 4. Key Takeaways & Quantitative Takeaways
- **The Danger of Over-Parameterization**: Adding numerous indicators and confirmation filters creates excessive degrees of freedom, making it easy to fit noise rather than true market signal.
- **Importance of Walk-Forward Validation**: In-sample backtesting alone is insufficient. Rigorous out-of-sample validation, walk-forward analysis, and Monte Carlo stress testing are mandatory before assessing real-world viability.
- **Primacy of Robustness Over Optimization**: A simpler model with fewer tunable parameters is far more likely to generalize across market cycles than a heavily optimized multi-layered rule set.

---

## ⚙️ Parameters & Configuration

Parameters are structured into clean groups within the cTrader Automate panel:

| Group | Parameter | Type | Default | Description |
| :--- | :--- | :--- | :--- | :--- |
| **General** | `System Status` | `string` | `"gAlgo Active"` | Diagnostic status message |
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
│   └── gAlgo.cs                  # Main quantitative execution engine
├── data/
│   └── calendar-event-list.sample.csv # Sample macroeconomic calendar format
├── .gitignore                         # Comprehensive C# & cTrader ignore rules
├── CONTRIBUTING.md                    # Research and contribution guidelines
├── LICENSE                            # Open-source MIT License
└── README.md                          # Technical documentation
```

---

## 🚀 Getting Started

### Requirements
- **cTrader Desktop** (Windows) or compatible cTrader Automate CLI.
- **C# Build Environment** (managed automatically by cTrader).

### Running in cTrader
1. Launch **cTrader**.
2. Open the **Automate** tab.
3. Click **New cBot** and name it `gAlgo`.
4. Replace the template with the contents of [`src/gAlgo.cs`](src/gAlgo.cs).
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
