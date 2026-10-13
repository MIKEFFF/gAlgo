using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Collections;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    /// <summary>
    /// gAlgo: Quantitative Multi-Factor Algorithmic Execution System for cTrader / cAlgo.
    /// Combines price action microstructure validation, Hull MA trend baseline, Dual Bollinger Band 
    /// volatility expansion, Chaikin Money Flow volume filters, ATR-based risk budgeting, and macroeconomic news filtering.
    /// </summary>
    [Robot(AccessRights = AccessRights.FullAccess, AddIndicators = true)]
    public class gAlgo : Robot
    {
        #region Parameters

        [Parameter("System Status", Group = "General", DefaultValue = "gAlgo Active")]
        public string StatusMessage { get; set; }

        [Parameter("Risk Fraction (of Equity)", Group = "Risk Budgeting", DefaultValue = 0.02, MinValue = 0.001, MaxValue = 1.0)]
        public double RiskFraction { get; set; }

        [Parameter("TP Multiplier (ATR)", Group = "Risk Budgeting", DefaultValue = 2.0, MinValue = 0.1)]
        public double TpMultiplier { get; set; }

        [Parameter("SL Multiplier (ATR)", Group = "Risk Budgeting", DefaultValue = 3.0, MinValue = 0.1)]
        public double SlMultiplier { get; set; }

        [Parameter("Long BB SL Multiplier", Group = "Risk Budgeting", DefaultValue = 2.0, MinValue = 0.1)]
        public double SlMultiplierLongBB { get; set; }

        [Parameter("Short BB SL Multiplier", Group = "Risk Budgeting", DefaultValue = 2.0, MinValue = 0.1)]
        public double SlMultiplierShortBB { get; set; }

        [Parameter("ATR Period", Group = "Technical Factors", DefaultValue = 14, MinValue = 1)]
        public int AtrPeriod { get; set; }

        [Parameter("HMA Baseline Period", Group = "Technical Factors", DefaultValue = 50, MinValue = 2)]
        public int HmaPeriod { get; set; }

        [Parameter("Chaikin Volume Period", Group = "Technical Factors", DefaultValue = 19, MinValue = 2)]
        public int ChaikinPeriod { get; set; }

        [Parameter("Long BB Period", Group = "Statistical Bands", DefaultValue = 21, MinValue = 2)]
        public int BbPeriodsLong { get; set; }

        [Parameter("Long BB StdDev", Group = "Statistical Bands", DefaultValue = 1.8, MinValue = 0.1)]
        public double StdDevsLong { get; set; }

        [Parameter("Short BB Period", Group = "Statistical Bands", DefaultValue = 15, MinValue = 2)]
        public int BbPeriodsShort { get; set; }

        [Parameter("Short BB StdDev", Group = "Statistical Bands", DefaultValue = 1.9, MinValue = 0.1)]
        public double StdDevsShort { get; set; }

        [Parameter("Session Start Hour (UTC)", Group = "Execution Windows", DefaultValue = 1, MinValue = 0, MaxValue = 23)]
        public int StartHour { get; set; }

        [Parameter("Session End Hour (UTC)", Group = "Execution Windows", DefaultValue = 23, MinValue = 0, MaxValue = 23)]
        public int EndHour { get; set; }

        [Parameter("Max Holding Period (Hours)", Group = "Execution Windows", DefaultValue = 3, MinValue = 0)]
        public int TradeLengthLimitHours { get; set; }

        [Parameter("Max Holding Period (Minutes)", Group = "Execution Windows", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TradeLengthLimitMinutes { get; set; }

        [Parameter("News Blackout Window (Mins)", Group = "Macro Event Filter", DefaultValue = 30, MinValue = 0)]
        public int NewsBufferMinutes { get; set; }

        [Parameter("Enable News Event Filter", Group = "Macro Event Filter", DefaultValue = true)]
        public bool EnableNewsFilter { get; set; }

        #endregion

        #region Private Fields

        // Technical Indicators
        private AverageTrueRange _atr;
        private MovingAverage _hma;
        private ChaikinMoneyFlow _chaikin;
        private BollingerBands _bbLong;
        private BollingerBands _bbShort;

        // Signal State Management
        private bool _isBuySetup;
        private bool _isSellSetup;
        private bool _readyToTrade;
        private bool _validForBuy;
        private bool _validForSell;
        private Bar _setupCandle;

        // Macroeconomic Calendar
        private List<DateTime> _newsTimes = new List<DateTime>();

        // Quantitative Performance Tracking
        private double _totalProfit;
        private double _totalLoss;
        private double _maxDrawdown;
        private double _peakBalance;
        private int _winningTrades;
        private int _losingTrades;
        private double _previousEquity;
        private readonly List<double> _returns = new List<double>();

        #endregion

        #region Lifecycle & Execution Engine

        protected override void OnStart()
        {
            // Indicator initialization
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
            _hma = Indicators.HullMovingAverage(Bars.ClosePrices, HmaPeriod);
            _chaikin = Indicators.ChaikinMoneyFlow(ChaikinPeriod);
            _bbLong = Indicators.BollingerBands(Bars.ClosePrices, BbPeriodsLong, StdDevsLong, MovingAverageType.Simple);
            _bbShort = Indicators.BollingerBands(Bars.ClosePrices, BbPeriodsShort, StdDevsShort, MovingAverageType.Simple);

            // Signal states
            _isBuySetup = false;
            _isSellSetup = false;
            _readyToTrade = true;
            _validForBuy = true;
            _validForSell = true;

            // Macro calendar ingestion
            if (EnableNewsFilter)
            {
                string newsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cAlgo", "excel");
                Directory.CreateDirectory(newsFolder);
                string newsFile = Path.Combine(newsFolder, "calendar-event-list.csv");
                _newsTimes = LoadNewsTimes(newsFile);
                Print($"[gAlgo] Loaded {_newsTimes.Count} macroeconomic calendar events from '{newsFile}'");
            }

            // Portfolio tracking
            _peakBalance = Account.Balance;
            _previousEquity = Account.Equity;
            Positions.Closed += OnPositionClosed;

            Print($"[gAlgo] Quantitative execution engine initialized. {StatusMessage}");
        }

        protected override void OnBar()
        {
            _readyToTrade = true;
            _validForBuy = true;
            _validForSell = true;

            // Liquidate positions outside allowable market hours
            if (!IsWithinTradingHours())
            {
                foreach (var position in Positions.FindAll("Buy", SymbolName))
                    ClosePosition(position);
                foreach (var position in Positions.FindAll("BuyBB", SymbolName))
                    ClosePosition(position);
                foreach (var position in Positions.FindAll("Sell", SymbolName))
                    ClosePosition(position);
                foreach (var position in Positions.FindAll("SellBB", SymbolName))
                    ClosePosition(position);
            }
        }

        protected override void OnTick()
        {
            if (Bars.Count < 3) return;

            // Check holding duration constraints across calendar sessions
            CheckTradeLifetimeLimits();

            if (!IsWithinTradingHours() || !_readyToTrade) return;

            // Blackout check for high-impact macro releases
            if (EnableNewsFilter && IsNearNewsTime(Server.Time)) return;

            // Market Microstructure Snapshot
            var candle1 = Bars.Last(2); // Inception bar
            var candle2 = Bars.Last(1); // Confirmation bar
            var candle3 = Bars.Last(0); // Live price formation

            double currentAsk = Symbol.Ask;
            double currentBid = Symbol.Bid;
            double currentAtrPips = Math.Round(_atr.Result.Last(1) / Symbol.PipSize);

            // Minimum volatility threshold check
            if (currentAtrPips < 1) return;

            // Price Action Reversal Pattern Analysis
            CheckBuyPriceActionPattern(candle1, candle2);
            CheckSellPriceActionPattern(candle1, candle2);

            // Signal Invalidation: Adverse momentum check
            if (_isBuySetup && currentAsk >= candle3.Open + (candle1.Close - candle1.Low))
            {
                _validForBuy = false;
            }
            if (_isSellSetup && currentBid <= candle3.Open - (candle1.High - candle1.Close))
            {
                _validForSell = false;
            }

            if (_setupCandle != candle2) return;

            // Factor Indicators
            double baselineHma = _hma.Result.Last(0);
            double cmfVolume = _chaikin.Result.Last(0);

            // Position state validation
            var posBuyBB = Positions.Find("BuyBB", SymbolName);
            var posSell = Positions.Find("Sell", SymbolName);
            var posSellBB = Positions.Find("SellBB", SymbolName);

            // --- FACTOR 1: STATISTICAL MEAN-REVERSION BUY (Bollinger Reversal) ---
            if (_isBuySetup && _validForBuy && posBuyBB == null)
            {
                double triggerPriceBuy = candle1.Low;
                if (currentAsk <= triggerPriceBuy && IsBollingerConfirmedForBuy(candle1, candle2, candle3))
                {
                    double stopLossPips = SlMultiplierLongBB * currentAtrPips;
                    double takeProfitPips = Math.Abs(_bbLong.Main.Last(0) - currentBid) / Symbol.PipSize;
                    double volumeUnits = CalculatePositionUnits(stopLossPips);

                    if (volumeUnits > 0)
                    {
                        ExecuteMarketOrder(TradeType.Buy, SymbolName, volumeUnits, "BuyBB", stopLossPips, takeProfitPips);
                        _readyToTrade = false;
                    }
                }
            }

            // --- FACTOR 2: TREND-MOMENTUM SHORT (HMA Baseline + CMF Volume) ---
            if (_isSellSetup && _validForSell && posSell == null)
            {
                double triggerPriceSell = candle1.High;
                if (currentBid >= triggerPriceSell && cmfVolume < 0 && currentBid < baselineHma)
                {
                    double stopLossPips = SlMultiplier * currentAtrPips;
                    double takeProfitPips = TpMultiplier * currentAtrPips;
                    double volumeUnits = CalculatePositionUnits(stopLossPips);

                    if (volumeUnits > 0)
                    {
                        ExecuteMarketOrder(TradeType.Sell, SymbolName, volumeUnits, "Sell", stopLossPips, takeProfitPips);
                        _readyToTrade = false;
                    }
                }
            }

            // --- FACTOR 3: STATISTICAL MEAN-REVERSION SHORT (Bollinger Reversal) ---
            if (_isSellSetup && _validForSell && posSellBB == null && _readyToTrade)
            {
                double triggerPriceSell = candle1.High;
                if (currentBid >= triggerPriceSell && IsBollingerConfirmedForSell(candle1, candle2, candle3))
                {
                    double stopLossPips = SlMultiplierShortBB * currentAtrPips;
                    double takeProfitPips = Math.Abs(currentAsk - _bbShort.Main.Last(0)) / Symbol.PipSize;
                    double volumeUnits = CalculatePositionUnits(stopLossPips);

                    if (volumeUnits > 0)
                    {
                        ExecuteMarketOrder(TradeType.Sell, SymbolName, volumeUnits, "SellBB", stopLossPips, takeProfitPips);
                        _readyToTrade = false;
                    }
                }
            }

            _isBuySetup = false;
            _isSellSetup = false;
        }

        protected override void OnStop()
        {
            Positions.Closed -= OnPositionClosed;

            // Quantitative Performance Synthesis
            double totalTrades = _winningTrades + _losingTrades;
            double winRate = totalTrades > 0 ? (100.0 * _winningTrades / totalTrades) : 0.0;
            double totalReturns = Account.Balance > 0 ? ((Account.Equity - Account.Balance) / Account.Balance * 100.0) : 0.0;
            double profitFactor = _totalLoss > 0 ? (_totalProfit / _totalLoss) : (_totalProfit > 0 ? double.PositiveInfinity : 0.0);
            double martinRatio = _maxDrawdown > 0 ? ((Account.Equity - Account.Balance) / _maxDrawdown) : 0.0;
            double sharpeRatio = CalculateSharpeRatio();

            Print("
=======================================================");
            Print("           gALGO PERFORMANCE SUMMARY              ");
            Print("=======================================================");
            Print($"Total Closed Trades: {totalTrades}");
            Print($"Win Rate:            {winRate:F2}% ({_winningTrades} Wins / {_losingTrades} Losses)");
            Print($"Total Return on Eq:  {totalReturns:F2}%");
            Print($"Profit Factor:       {(double.IsPositiveInfinity(profitFactor) ? "INF" : profitFactor.ToString("F2"))}");
            Print($"Max Drawdown:        {_maxDrawdown:F2} {Account.Asset.Name}");
            Print($"Martin (Ulcer) Ratio:{martinRatio:F2}");
            Print($"Annualized Sharpe:   {sharpeRatio:F2}");
            Print("=======================================================
");
        }

        #endregion

        #region Quantitative Signal Processing

        /// <summary>
        /// Analyzes 2-candle price action reversal and wick distribution for bullish setups.
        /// </summary>
        private void CheckBuyPriceActionPattern(Bar candle1, Bar candle2)
        {
            double prevAtr = Math.Max(Symbol.PipSize, _atr.Result.Last(2));
            double roomForDoubt = 0.02 * prevAtr;

            bool candle1Bearish = candle1.Close <= (candle1.Open + roomForDoubt);
            bool candle1HasLowerWick = candle1.Low < candle1.Close;
            bool candle2CloseAboveCandle1Low = candle2.Close > candle1.Low;
            bool candle2LowTestsCandle1Low = candle2.Low <= candle1.Low;
            bool candle2Bullish = candle2.Close >= (candle2.Open - roomForDoubt);

            bool goodWick1 = (candle1.Close - candle1.Low) >= (0.35 * Symbol.PipSize);
            bool goodWick2 = (candle2.Close - candle2.Low) >= (0.40 * Symbol.PipSize);

            if (candle1Bearish && candle1HasLowerWick && candle2Bullish &&
                candle2CloseAboveCandle1Low && candle2LowTestsCandle1Low &&
                goodWick1 && goodWick2)
            {
                _isBuySetup = true;
                _setupCandle = candle2;
            }
        }

        /// <summary>
        /// Analyzes 2-candle price action reversal and wick distribution for bearish setups.
        /// </summary>
        private void CheckSellPriceActionPattern(Bar candle1, Bar candle2)
        {
            double prevAtr = Math.Max(Symbol.PipSize, _atr.Result.Last(2));
            double roomForDoubt = 0.02 * prevAtr;

            bool candle1Bullish = candle1.Close >= (candle1.Open - roomForDoubt);
            bool candle1HasUpperWick = candle1.High > candle1.Close;
            bool candle2CloseBelowCandle1High = candle2.Close < candle1.High;
            bool candle2HighTestsCandle1High = candle2.High >= candle1.High;
            bool candle2Bearish = candle2.Close <= (candle2.Open + roomForDoubt);

            bool goodWick1 = (candle1.High - candle1.Close) >= (0.35 * Symbol.PipSize);
            bool goodWick2 = (candle2.High - candle2.Close) >= (0.40 * Symbol.PipSize);

            if (candle1Bullish && candle1HasUpperWick && candle2Bearish &&
                candle2CloseBelowCandle1High && candle2HighTestsCandle1High &&
                goodWick1 && goodWick2)
            {
                _isSellSetup = true;
                _setupCandle = candle2;
            }
        }

        private bool IsBollingerConfirmedForBuy(Bar candle1, Bar candle2, Bar candle3)
        {
            bool nearLowerBand = candle1.Low <= _bbLong.Bottom.Last(2) || candle2.Low <= _bbLong.Bottom.Last(1);

            double currentBandwidth = _bbLong.Top.Last(0) - _bbLong.Bottom.Last(0);
            double previousBandwidth = _bbLong.Top.Last(2) - _bbLong.Bottom.Last(2);
            bool bandsExpanding = currentBandwidth > previousBandwidth;

            bool notOversold = candle3.Close > (_bbLong.Bottom.Last(0) + (5 * Symbol.PipSize));

            return nearLowerBand && bandsExpanding && notOversold;
        }

        private bool IsBollingerConfirmedForSell(Bar candle1, Bar candle2, Bar candle3)
        {
            bool nearUpperBand = candle1.High >= _bbShort.Top.Last(2) || candle2.High >= _bbShort.Top.Last(1);

            double currentBandwidth = _bbShort.Top.Last(0) - _bbShort.Bottom.Last(0);
            double previousBandwidth = _bbShort.Top.Last(2) - _bbShort.Bottom.Last(2);
            bool bandsExpanding = currentBandwidth > previousBandwidth;

            bool notOverbought = candle3.Close < (_bbShort.Top.Last(0) - (5 * Symbol.PipSize));

            return nearUpperBand && bandsExpanding && notOverbought;
        }

        #endregion

        #region Risk Allocation & Position Sizing

        /// <summary>
        /// Dynamically computes normalized position sizing matching exact portfolio equity risk parameters.
        /// </summary>
        private double CalculatePositionUnits(double stopLossPips)
        {
            if (stopLossPips <= 0 || Symbol.PipValue <= 0) return Symbol.VolumeInUnitsMin;

            double riskCapital = Account.Equity * RiskFraction;
            double calculatedUnits = riskCapital / (stopLossPips * Symbol.PipValue);

            return Symbol.NormalizeVolumeInUnits(calculatedUnits, RoundingMode.Down);
        }

        /// <summary>
        /// Enforces temporal risk bounds, liquidating stale trades exceeding holding constraints.
        /// </summary>
        private void CheckTradeLifetimeLimits()
        {
            if (TradeLengthLimitHours == 0 && TradeLengthLimitMinutes == 0) return;

            TimeSpan maxDuration = new TimeSpan(TradeLengthLimitHours, TradeLengthLimitMinutes, 0);
            foreach (var position in Positions)
            {
                if (position.SymbolName == SymbolName && (Server.Time - position.EntryTime) >= maxDuration)
                {
                    ClosePosition(position);
                }
            }
        }

        private bool IsWithinTradingHours()
        {
            int currentHour = Server.Time.Hour;
            if (StartHour <= EndHour)
            {
                return currentHour >= StartHour && currentHour <= EndHour;
            }
            return currentHour >= StartHour || currentHour <= EndHour;
        }

        private bool IsNearNewsTime(DateTime currentTime)
        {
            if (_newsTimes.Count == 0) return false;

            return _newsTimes.Any(newsTime =>
                currentTime >= newsTime.AddMinutes(-NewsBufferMinutes) &&
                currentTime <= newsTime.AddMinutes(NewsBufferMinutes)
            );
        }

        private List<DateTime> LoadNewsTimes(string filePath)
        {
            var times = new List<DateTime>();
            if (!File.Exists(filePath))
            {
                Print($"[gAlgo] Macroeconomic calendar file not found at '{filePath}'. Running without calendar blackout.");
                return times;
            }

            try
            {
                foreach (var line in File.ReadLines(filePath).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var columns = line.Split(',');
                    if (columns.Length > 1)
                    {
                        var rawDate = columns[1].Trim();
                        if (DateTime.TryParse(rawDate, out var parsedTime))
                        {
                            times.Add(parsedTime);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"[gAlgo] Error loading macro calendar: {ex.Message}");
            }
            return times;
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            var position = args.Position;
            if (position.SymbolName != SymbolName) return;

            double pnl = position.NetProfit;
            if (pnl > 0)
            {
                _totalProfit += pnl;
                _winningTrades++;
            }
            else
            {
                _totalLoss += Math.Abs(pnl);
                _losingTrades++;
            }

            double currentEquity = Account.Equity;
            if (_previousEquity > 0)
            {
                double periodReturn = (currentEquity - _previousEquity) / _previousEquity;
                _returns.Add(periodReturn);
            }
            _previousEquity = currentEquity;

            _peakBalance = Math.Max(_peakBalance, Account.Equity);
            _maxDrawdown = Math.Max(_maxDrawdown, _peakBalance - Account.Equity);
        }

        private double CalculateSharpeRatio()
        {
            if (_returns.Count < 2) return 0.0;

            double meanReturn = _returns.Average() * 252.0;
            double variance = _returns.Sum(r => Math.Pow(r - _returns.Average(), 2)) / (_returns.Count - 1);
            double stdDev = Math.Sqrt(variance) * Math.Sqrt(252.0);

            return stdDev > 0 ? (meanReturn / stdDev) : 0.0;
        }

        #endregion
    }
}
