# Contributing to AegisTrader

We welcome research contributions, optimizations, and discussions regarding **AegisTrader**.

## Development Principles
- **Algorithmic Rigor**: Maintain mathematical clarity and prevent lookahead / survivorship bias in indicator signals.
- **Performance & Efficiency**: Ensure all per-tick routines remain performant and avoid unnecessary allocations.
- **Risk Integrity**: Protect capital preservation logic and boundary constraints.

## Testing
- Conduct backtests using tick-level historical data across diverse market regimes (trending, mean-reverting, high-volatility).
- Validate metrics (Sharpe, Ulcer Index, Drawdown) across sample and out-of-sample periods.
