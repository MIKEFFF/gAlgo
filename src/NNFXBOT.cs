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
    /// NNFXBOT: Multi-indicator algorithmic trading robot inspired by the No Nonsense Forex (NNFX) framework.
    /// Combines price action candlestick confirmation, Hull MA baseline trend, Dual Bollinger Bands, 
    /// Chaikin Money Flow volume, ATR risk management, and economic news avoidance.
    /// </summary>
    [Robot(AccessRights = AccessRights.FullAccess, AddIndicators = true)]
    public class NNFXBOT : Robot
    {
        #region Parameters

        [Parameter("Status Message", Group = "General", DefaultValue = "NNFX Bot Active")]
        public string StatusMessage { get; set; }

        [Parameter("Risk Fraction (of Equity)", Group = "Risk Management", DefaultValue = 0.02, MinValue = 0.001, MaxValue = 1.0)]
        public double RiskFraction { get; set; }

        [Parameter("TP Multiplier (ATR)", Group = "Risk Management", DefaultValue = 2.0, MinValue = 0.1)]
        public double TpMultiplier { get; set; }

        [Parameter("SL Multiplier (ATR)", Group = "Risk Management", DefaultValue = 3.0, MinValue = 0.1)]
        public double SlMultiplier { get; set; }

        [Parameter("Long BB SL Multiplier", Group = "Risk Management", DefaultValue = 2.0, MinValue = 0.1)]
        public double SlMultiplierLongBB { get; set; }

        [Parameter("Short BB SL Multiplier", Group = "Risk Management", DefaultValue = 2.0, MinValue = 0.1)]
        public double SlMultiplierShortBB { get; set; }

        [Parameter("ATR Period", Group = "Indicators", DefaultValue = 14, MinValue = 1)]
        public int AtrPeriod { get; set; }

        [Parameter("HMA Baseline Period", Group = "Indicators", DefaultValue = 50, MinValue = 2)]
        public int HmaPeriod { get; set; }

        [Parameter("Chaikin Period", Group = "Indicators", DefaultValue = 19, MinValue = 2)]
        public int ChaikinPeriod { get; set; }

        [Parameter("Long BB Period", Group = "Bollinger Bands", DefaultValue = 21, MinValue = 2)]
        public int BbPeriodsLong { get; set; }

        [Parameter("Long BB StdDev", Group = "Bollinger Bands", DefaultValue = 1.8, MinValue = 0.1)]
        public double StdDevsLong { get; set; }

        [Parameter("Short BB Period", Group = "Bollinger Bands", DefaultValue = 15, MinValue = 2)]
        public int BbPeriodsShort { get; set; }

        [Parameter("Short BB StdDev", Group = "Bollinger Bands", DefaultValue = 1.9, MinValue = 0.1)]
        public double StdDevsShort { get; set; }

        [Parameter("Start Hour (UTC/Server)", Group = "Trading Hours", DefaultValue = 1, MinValue = 0, MaxValue = 23)]
        public int StartHour { get; set; }

        [Parameter("End Hour (UTC/Server)", Group = "Trading Hours", DefaultValue = 23, MinValue = 0, MaxValue = 23)]
        public int EndHour { get; set; }

        [Parameter("Trade Max Lifetime (Hours)", Group = "Trade Execution", DefaultValue = 3, MinValue = 0)]
        public int TradeLengthLimitHours { get; set; }

        [Parameter("Trade Max Lifetime (Minutes)", Group = "Trade Execution", DefaultValue = 0, MinValue = 0, MaxValue = 59)]
        public int TradeLengthLimitMinutes { get; set; }

        [Parameter("News Buffer (Minutes)", Group = "News Filter", DefaultValue = 30, MinValue = 0)]
        public int NewsBufferMinutes { get; set; }

        [Parameter("Enable News Filter", Group = "News Filter", DefaultValue = true)]
        public bool EnableNewsFilter { get; set; }

        #endregion

        #region Private Fields

        // Indicator references
        private AverageTrueRange _atr;
        private MovingAverage _hma;
        private ChaikinMoneyFlow _chaikin;
        private BollingerBands _bbLong;
        private BollingerBands _bbShort;

        // Trade state tracking
        private bool _isBuySetup;
        private bool _isSellSetup;
        private bool _readyToTrade;
        private bool _validForBuy;
        private bool _validForSell;
        private Bar _setupCandle;

        // News calendar
        private List<DateTime> _newsTimes = new List<DateTime>();

        // Performance & statistics
        private double _totalProfit;
        private double _totalLoss;
        private double _maxDrawdown;
        private double _peakBalance;
        private int _winningTrades;
        private int _losingTrades;
        private double _previousEquity;
        private readonly List<double> _returns = new List<double>();

        #endregion

        #region cBot Lifecycle

        protected override void OnStart()
        {
            // Initialize technical indicators
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
            _hma = Indicators.HullMovingAverage(Bars.ClosePrices, HmaPeriod);
            _chaikin = Indicators.ChaikinMoneyFlow(ChaikinPeriod);
            _bbLong = Indicators.BollingerBands(Bars.ClosePrices, BbPeriodsLong, StdDevsLong, MovingAverageType.Simple);
            _bbShort = Indicators.BollingerBands(Bars.ClosePrices, BbPeriodsShort, StdDevsShort, MovingAverageType.Simple);

            // Reset flags
            _isBuySetup = false;
            _isSellSetup = false;
            _readyToTrade = true;
            _validForBuy = true;
            _validForSell = true;

            // Load economic news calendar if enabled
            if (EnableNewsFilter)
            {
                string newsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cAlgo", "excel");
                Directory.CreateDirectory(newsFolder);
                string newsFile = Path.Combine(newsFolder, "calendar-event-list.csv");
                _newsTimes = LoadNewsTimes(newsFile);
                Print($"[NNFXBOT] Loaded {_newsTimes.Count} news events from '{newsFile}'");
            }

            // Performance tracking initialization
            _peakBalance = Account.Balance;
            _previousEquity = Account.Equity;
            Positions.Closed += OnPositionClosed;

            Print($"[NNFXBOT] Initialized successfully. {StatusMessage}");
        }

        protected override void OnBar()
        {
            _readyToTrade = true;
            _validForBuy = true;
            _validForSell = true;

            // Close open positions outside allowed trading session
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

            // Check max trade lifetime timeouts (handles cross-midnight timestamps correctly)
            CheckTradeLifetimeLimits();

            if (!IsWithinTradingHours() || !_readyToTrade) return;

            // Check news blackout
            if (EnableNewsFilter && IsNearNewsTime(Server.Time)) return;

            // Price & Bar Context
            var candle1 = Bars.Last(2); // Previous candle
            var candle2 = Bars.Last(1); // Setup confirmation candle
            var candle3 = Bars.Last(0); // Current active candle

            double currentAsk = Symbol.Ask;
            double currentBid = Symbol.Bid;
            double currentAtrPips = Math.Round(_atr.Result.Last(1) / Symbol.PipSize);

            // Ensure minimum market volatility
            if (currentAtrPips < 1) return;

            // Evaluate 2-candle price action reversal patterns
            CheckBuyPriceActionPattern(candle1, candle2);
            CheckSellPriceActionPattern(candle1, candle2);

            // Invalidation check if price runs contrary to setup trigger
            if (_isBuySetup && currentAsk >= candle3.Open + (candle1.Close - candle1.Low))
            {
                _validForBuy = false;
            }
            if (_isSellSetup && currentBid <= candle3.Open - (candle1.High - candle1.Close))
            {
                _validForSell = false;
            }

            // Ensure pattern matches the last confirmed bar
            if (_setupCandle != candle2) return;

            // Indicator values
            double baselineHma = _hma.Result.Last(0);
            double cmfVolume = _chaikin.Result.Last(0);

            // Existing positions
            var posBuyBB = Positions.Find("BuyBB", SymbolName);
            var posSell = Positions.Find("Sell", SymbolName);
            var posSellBB = Positions.Find("SellBB", SymbolName);

            // --- BUY EXECUTION ---
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

            // --- SELL EXECUTION (Baseline + CMF) ---
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

            // --- SELL EXECUTION (Bollinger Reversal) ---
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

            // Performance metrics computation
            double totalTrades = _winningTrades + _losingTrades;
            double winRate = totalTrades > 0 ? (100.0 * _winningTrades / totalTrades) : 0.0;
            double totalReturns = Account.Balance > 0 ? ((Account.Equity - Account.Balance) / Account.Balance * 100.0) : 0.0;
            double profitFactor = _totalLoss > 0 ? (_totalProfit / _totalLoss) : (_totalProfit > 0 ? double.PositiveInfinity : 0.0);
            double martinRatio = _maxDrawdown > 0 ? ((Account.Equity - Account.Balance) / _maxDrawdown) : 0.0;
            double sharpeRatio = CalculateSharpeRatio();

            Print("
==========================================");
            Print("       NNFXBOT PERFORMANCE REPORT         ");
            Print("==========================================");
            Print($"Total Closed Trades: {totalTrades}");
            Print($"Win Rate:            {winRate:F2}% ({_winningTrades}W / {_losingTrades}L)");
            Print($"Total Returns:       {totalReturns:F2}%");
            Print($"Profit Factor:       {(double.IsPositiveInfinity(profitFactor) ? "INF" : profitFactor.ToString("F2"))}");
            Print($"Max Drawdown:        {_maxDrawdown:F2} {Account.Asset.Name}");
            Print($"Martin (UPI) Ratio:  {martinRatio:F2}");
            Print($"Sharpe Ratio:        {sharpeRatio:F2}");
            Print("==========================================
");
        }

        #endregion

        #region Strategy Logic & Pattern Recognition

        /// <summary>
        /// Identifies a 2-candle bullish rejection/reversal pattern.
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

            // Minimum wick requirement (0.35 pips / points relative to asset)
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
        /// Identifies a 2-candle bearish rejection/reversal pattern.
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

            // Minimum wick requirement (0.35 pips / points relative to asset)
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
            // 1. Reversal forms near lower Bollinger Band
            bool nearLowerBand = candle1.Low <= _bbLong.Bottom.Last(2) || candle2.Low <= _bbLong.Bottom.Last(1);

            // 2. Bands are expanding (volatility increasing)
            double currentBandwidth = _bbLong.Top.Last(0) - _bbLong.Bottom.Last(0);
            double previousBandwidth = _bbLong.Top.Last(2) - _bbLong.Bottom.Last(2);
            bool bandsExpanding = currentBandwidth > previousBandwidth;

            // 3. Price is not completely compressed against extreme lower band
            bool notOversold = candle3.Close > (_bbLong.Bottom.Last(0) + (5 * Symbol.PipSize));

            return nearLowerBand && bandsExpanding && notOversold;
        }

        private bool IsBollingerConfirmedForSell(Bar candle1, Bar candle2, Bar candle3)
        {
            // 1. Reversal forms near upper Bollinger Band
            bool nearUpperBand = candle1.High >= _bbShort.Top.Last(2) || candle2.High >= _bbShort.Top.Last(1);

            // 2. Bands are expanding (volatility increasing)
            double currentBandwidth = _bbShort.Top.Last(0) - _bbShort.Bottom.Last(0);
            double previousBandwidth = _bbShort.Top.Last(2) - _bbShort.Bottom.Last(2);
            bool bandsExpanding = currentBandwidth > previousBandwidth;

            // 3. Price is not completely compressed against extreme upper band
            bool notOverbought = candle3.Close < (_bbShort.Top.Last(0) - (5 * Symbol.PipSize));

            return nearUpperBand && bandsExpanding && notOverbought;
        }

        #endregion

        #region Risk Management & Helper Methods

        /// <summary>
        /// Calculates position volume (units) normalized to broker requirements based on account equity risk.
        /// </summary>
        private double CalculatePositionUnits(double stopLossPips)
        {
            if (stopLossPips <= 0 || Symbol.PipValue <= 0) return Symbol.VolumeInUnitsMin;

            double riskCapital = Account.Equity * RiskFraction;
            double calculatedUnits = riskCapital / (stopLossPips * Symbol.PipValue);

            return Symbol.NormalizeVolumeInUnits(calculatedUnits, RoundingMode.Down);
        }

        /// <summary>
        /// Checks and closes positions exceeding the maximum configured lifetime.
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
            // Handling overnight session wrapping (e.g. 22:00 to 06:00)
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
                Print($"[NNFXBOT] News calendar file not found at '{filePath}'. News filter will be bypassed.");
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
                Print($"[NNFXBOT] Error reading news CSV: {ex.Message}");
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
