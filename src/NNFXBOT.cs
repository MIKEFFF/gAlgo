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
    [Robot(AccessRights = AccessRights.FullAccess, AddIndicators = true)]
    public class NNFXBOT : Robot
    {
        [Parameter("Status Message", DefaultValue = "NNFX Bot Active")]
        public string Message { get; set; }

        private double _totalProfit, _totalLoss, _maxDrawdown, _peakBalance;
        private int _winningTrades, _losingTrades;
        private readonly List<double> _returns = new List<double>();
        private double _previousEquity;

        private AverageTrueRange atr;
        private bool isBuy;
        private bool isSell;
        private bool ready;
        private bool validForBuy;
        private bool validForSell;

        private Bar Candle2;

        // Bollinger Bands
        private BollingerBands _bbLong;
        private BollingerBands _bbShort;

        // Chaikin Money Flow
        private ChaikinMoneyFlow chaikin;

        // Hull Moving Average
        private MovingAverage hma;

        // News Events
        private List<DateTime> _newsTimes = new List<DateTime>();

        [Parameter("Buffer Minutes", DefaultValue = 30)]
        public int BufferMinutes { get; set; }

        [Parameter("Risk Fraction", DefaultValue = 0.15)]
        public double risk { get; set; }

        [Parameter("TP Multiplier", DefaultValue = 2)]
        public double TPmultiplier { get; set; }

        [Parameter("SL Multiplier", DefaultValue = 3)]
        public double SLmultiplier { get; set; }

        [Parameter("Long SL Pips", DefaultValue = 2)]
        public double SLPipsLong { get; set; }

        [Parameter("Short SL Pips", DefaultValue = 2)]
        public double SLPipsShort { get; set; }

        [Parameter("HMA Period", DefaultValue = 50)]
        public int HmaPeriod { get; set; }

        [Parameter("Chaikin Period", DefaultValue = 19)]
        public int ChaikinPeriod { get; set; }

        [Parameter("Trade Length Limit (Hours)", DefaultValue = 3)]
        public int TradeLengthLimitHours { get; set; }

        [Parameter("Trade Length Limit (Minutes)", DefaultValue = 0)]
        public int TradeLengthLimitMinutes { get; set; }

        [Parameter("Long BB Period", DefaultValue = 21)]
        public int BbPeriodsLong { get; set; }

        [Parameter("Short BB Period", DefaultValue = 15)]
        public int BbPeriodsShort { get; set; }

        [Parameter("Long Standard Deviations", DefaultValue = 1.8)]
        public double StdDevsLong { get; set; }

        [Parameter("Short Standard Deviations", DefaultValue = 1.9)]
        public double StdDevsShort { get; set; }

        private Dictionary<int, double> _entryMiddleBands = new Dictionary<int, double>();
        private Dictionary<int, double> _highestProfit = new Dictionary<int, double>();

        protected override void OnStart()
        {
            // Load indicators on startup
            atr = Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            isBuy = false;
            isSell = false;
            ready = true;
            validForBuy = true;
            validForSell = true;

            // News data loading
            string newsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cAlgo", "excel");
            Directory.CreateDirectory(newsFolder);
            string newsFile = Path.Combine(newsFolder, "calendar-event-list.csv");
            _newsTimes = LoadNewsTimes(newsFile);
            Print($"Loaded {_newsTimes.Count} news events");

            // Chaikin Money Flow
            chaikin = Indicators.ChaikinMoneyFlow(ChaikinPeriod);

            // Hull Moving Average
            hma = Indicators.HullMovingAverage(Bars.ClosePrices, HmaPeriod);

            // Bollinger Bands
            _bbLong = Indicators.BollingerBands(
                Bars.ClosePrices,
                BbPeriodsLong,
                StdDevsLong,
                MovingAverageType.Simple
            );

            _bbShort = Indicators.BollingerBands(
                Bars.ClosePrices,
                BbPeriodsShort,
                StdDevsShort,
                MovingAverageType.Simple
            );

            _peakBalance = Account.Balance;
            _previousEquity = Account.Equity;

            // Correct event subscription
            Positions.Closed += PositionsOnClosed;
            foreach (var news in _newsTimes)
            {
                Print(news);
            }
        }

        protected override void OnBar()
        {
            ready = true;
            validForBuy = true;
            validForSell = true;
            if (!isCorrectHours())
            {
                foreach (var position in Positions)
                {
                    ClosePosition(position);
                }
            }
        }

        private bool isCorrectHours()
        {
            DateTime now = Server.Time;
            TimeSpan startTime = new TimeSpan(1, 0, 0); // 01:00 AM
            TimeSpan endTime = new TimeSpan(23, 0, 0);  // 23:00 PM

            return now.TimeOfDay >= startTime && now.TimeOfDay <= endTime;
        }

        private bool isPossibleBuy(Bar candle1, Bar candle2)
        {
            var PrevATR = Math.Round(atr.Result.Last(2));
            double wickSize = 0.3 * PrevATR;
            double roomForDoubt = 0.02 * PrevATR;

            // First candle bearish or doji
            bool candle1Bearish = candle1.Close <= candle1.Open + roomForDoubt;

            // Candle 1 has wick
            bool candle1HasWick = candle1.Low < candle1.Close;

            // Second candle closes above candle 1 low
            bool candle2CloseIsAboveCandle1Low = candle2.Close > candle1.Low;

            // Second candle made lower or equal low
            bool candle2LowIsEqualOrBelowCandle1Low = candle2.Low <= candle1.Low;

            // Second candle is bullish or doji
            bool candle2IsBullish = candle2.Close >= candle2.Open - roomForDoubt;

            // Large enough wicks
            bool goodWickcandle1 = candle1.Close - candle1.Low >= 0.35;
            bool goodWickcandle2 = candle2.Close - candle2.Low >= 0.4;
            bool goodWicks = goodWickcandle1 & goodWickcandle2;

            if (candle2IsBullish & candle1Bearish & candle2CloseIsAboveCandle1Low & candle2LowIsEqualOrBelowCandle1Low & goodWicks)
            {
                isBuy = true;
                Candle2 = Bars.Last(1);
                return true;
            }
            return false;
        }

        private bool isPossibleSell(Bar candle1, Bar candle2)
        {
            var PrevATR = Math.Round(atr.Result.Last(2));
            double wickSize = 0.3 * PrevATR;
            double roomForDoubt = 0.02 * PrevATR;

            // First candle bullish
            bool candle1Bullish = candle1.Close >= candle1.Open - roomForDoubt;

            // Candle 1 has wick
            bool candle1HasWick = candle1.High > candle1.Close;

            // Second candle closes below candle 1 high
            bool candle2CloseIsBelowCandle1High = candle2.Close < candle1.High;

            // Second candle made higher or equal high
            bool candle2HighIsEqualOrAboveCandle1High = candle2.High >= candle1.High;

            // Second candle is bearish or doji
            bool candle2IsBearish = candle2.Close <= candle2.Open + roomForDoubt;

            // Large enough wicks
            bool goodWickcandle1 = candle1.High - candle1.Close >= 0.35;
            bool goodWickcandle2 = candle2.High - candle2.Close >= 0.4;
            bool goodWicks = goodWickcandle1 & goodWickcandle2;

            if (candle2IsBearish & candle1Bullish & candle2CloseIsBelowCandle1High & candle2HighIsEqualOrAboveCandle1High & goodWicks)
            {
                isSell = true;
                Candle2 = Bars.Last(1);
                return true;
            }
            return false;
        }

        protected override void OnTick()
        {
            if (Bars.Count < 3)
            {
                return;
            }

            var PrevATR = Math.Round(atr.Result.Last(1) / Symbol.PipSize);

            // Current price
            double currentPriceForBuy = Symbol.Ask;
            double currentPriceForSell = Symbol.Bid;

            // 3 candles in pattern
            var candle1 = Bars.Last(2);
            var candle2 = Bars.Last(1);
            var candle3 = Bars.Last(0);

            // Chaikin Money Flow
            var volume = chaikin.Result.Last(0);

            // Hull Moving Average
            var baseline = hma.Result.Last(0);

            // Trigger Price
            double triggerPriceForBuy = candle1.Low;
            double triggerPriceForSell = candle1.High;

            // Recognition of the pattern from first two candles
            isPossibleBuy(candle1, candle2);
            isPossibleSell(candle1, candle2);

            // Trading hours check
            bool correctHours = isCorrectHours();

            var existingPositionBuy = Positions.Find("Buy", SymbolName);
            var existingPositionBuyBB = Positions.Find("BuyBB", SymbolName);
            var existingPositionSell = Positions.Find("Sell", SymbolName);
            var existingPositionSellBB = Positions.Find("SellBB", SymbolName);

            // Closes position if it exceeds max holding time
            TimeSpan tradeLengthLimit = new TimeSpan(TradeLengthLimitHours, TradeLengthLimitMinutes, 0);
            foreach (var position in Positions)
            {
                if (Server.Time.TimeOfDay - position.EntryTime.TimeOfDay >= tradeLengthLimit && position.LastUpdateTime == position.EntryTime)
                {
                    ClosePosition(position);
                }
            }

            // If no trade setup verified on 2nd candle of pattern, return
            if (Candle2 != candle2)
            {
                return;
            }

            // Invalidation check if price runs contrary to setup
            if (isBuy && currentPriceForBuy >= candle3.Open + (candle1.Close - candle1.Low))
            {
                validForBuy = false;
            }
            if (isSell && currentPriceForSell <= candle3.Open - (candle1.High - candle1.Close))
            {
                validForSell = false;
            }

            // SL and TP calculations
            double takeProfitATR = TPmultiplier * PrevATR;
            double stopLossATR = SLmultiplier * PrevATR;

            double stopLossForBuyBB = SLPipsLong * PrevATR;
            double takeProfitForBuyBB = Math.Abs(_bbLong.Main.Last(0) - Symbol.Bid) / Symbol.PipSize;

            double stopLossForSellBB = SLPipsShort * PrevATR;
            double takeProfitForSellBB = Math.Abs(Symbol.Ask - _bbShort.Main.Last(0)) / Symbol.PipSize;

            // Trade Amounts (ATR & Bollinger Band risk sizing)
            double tradeAmountATR = getCorrectTradeAmount(stopLossATR);
            double tradeAmountBuyBB = getCorrectTradeAmount(stopLossForBuyBB);
            double tradeAmountSellBB = getCorrectTradeAmount(stopLossForSellBB);

            bool isNewsTime = IsNearNewsTime(Server.Time) && PrevATR > 500;

            if (correctHours && PrevATR > 100 && ready && !isNewsTime)
            {
                if (existingPositionBuyBB == null && isPossibleBuy(candle1, candle2) && currentPriceForBuy <= triggerPriceForBuy && IsBollingerConfirmedForBuy(candle1, candle2, candle3))
                {
                    ExecuteMarketOrder(TradeType.Buy, SymbolName, tradeAmountBuyBB, "BuyBB", stopLossForBuyBB, takeProfitForBuyBB);
                    ready = false;
                }

                if (existingPositionSell == null && isPossibleSell(candle1, candle2) && currentPriceForSell >= triggerPriceForSell && volume < 0 && currentPriceForSell < baseline)
                {
                    ExecuteMarketOrder(TradeType.Sell, SymbolName, tradeAmountATR, "Sell", stopLossATR, takeProfitATR);
                    ready = false;
                }
                if (existingPositionSellBB == null && isPossibleSell(candle1, candle2) && currentPriceForSell >= triggerPriceForSell && IsBollingerConfirmedForSell(candle1, candle2, candle3))
                {
                    ExecuteMarketOrder(TradeType.Sell, SymbolName, tradeAmountSellBB, "SellBB", stopLossForSellBB, takeProfitForSellBB);
                    ready = false;
                }
            }

            isBuy = false;
            isSell = false;
        }

        protected override void OnStop()
        {
            Print("NNFXBOT stopped.");
            // Unsubscribe from event
            Positions.Closed -= PositionsOnClosed;

            // Calculate metrics
            double totalReturns = (Account.Equity - Account.Balance) / Account.Balance * 100;
            double profitFactor = _totalLoss > 0 ? _totalProfit / _totalLoss : double.PositiveInfinity;
            double martinRatio = _maxDrawdown > 0 ? (Account.Equity - Account.Balance) / _maxDrawdown : double.PositiveInfinity;
            double sharpeRatio = CalculateSharpeRatio();

            // Print Strategy Performance Report
            Print("
=== STRATEGY PERFORMANCE REPORT ===");
            Print($"Total Returns: {totalReturns:F2}%");
            Print($"Profit Factor: {profitFactor:F2}");
            Print($"Martin Ratio: {martinRatio:F2}");
            Print($"Sharpe Ratio: {sharpeRatio:F2}");
            Print($"Max Drawdown: {_maxDrawdown:F2} {Account.Asset.Name}");
            Print($"Win Rate: {(100.0 * _winningTrades / Math.Max(1, (_winningTrades + _losingTrades))):F1}%");
            Print("===================================");
        }

        private bool IsBollingerConfirmedForBuy(Bar candle1, Bar candle2, Bar candle3)
        {
            // 1. Pattern forms near lower band
            bool isNearLowerBand = candle1.Low <= _bbLong.Bottom.Last(2) ||
                                  candle2.Low <= _bbLong.Bottom.Last(1);

            // 2. Bands are expanding (volatility increasing)
            bool bandsExpanding = (_bbLong.Top.Last(0) - _bbLong.Bottom.Last(0)) >
                                (_bbLong.Top.Last(2) - _bbLong.Bottom.Last(2));

            // 3. Price isn't at extreme lower band (allows mean reversion)
            bool notOversold = candle3.Close > _bbLong.Bottom.Last(0) + (5 * Symbol.PipSize);

            return isNearLowerBand & bandsExpanding & notOversold;
        }

        private bool IsBollingerConfirmedForSell(Bar candle1, Bar candle2, Bar candle3)
        {
            // 1. Pattern forms near Upper band
            bool isNearUpperBand = candle1.High >= _bbShort.Top.Last(2) ||
                                  candle2.High >= _bbShort.Top.Last(1);

            // 2. Bands are expanding (volatility increasing)
            bool bandsExpanding = (_bbShort.Top.Last(0) - _bbShort.Bottom.Last(0)) >
                                (_bbShort.Top.Last(2) - _bbShort.Bottom.Last(2));

            // 3. Price isn't at extreme upper band (allows mean reversion)
            bool notOverbought = candle3.Close < _bbShort.Top.Last(0) + (5 * Symbol.PipSize);

            return isNearUpperBand & bandsExpanding & notOverbought;
        }

        // Extracting news times from CSV
        private List<DateTime> LoadNewsTimes(string filePath)
        {
            var times = new List<DateTime>();
            if (!File.Exists(filePath))
            {
                Print($"News file not found at '{filePath}'. Running without news filter.");
                return times;
            }

            try
            {
                // Read CSV, skip header (assumes DateTime is in second column [1])
                foreach (var line in File.ReadLines(filePath).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var columns = line.Split(',');
                    if (columns.Length > 1)
                    {
                        var dateParts = columns[1].Trim().Split('/');
                        if (dateParts.Length >= 3)
                        {
                            var month = dateParts[0];
                            var day = dateParts[1];
                            var yearAndTime = dateParts[2].Trim().Split(' ');
                            var year = yearAndTime[0];
                            var timeOfDay = yearAndTime.Length > 1 ? (" " + yearAndTime[1]) : "";
                            var formattedDate = $"{day}/{month}/{year}{timeOfDay}";

                            if (DateTime.TryParse(formattedDate, out var newsTime))
                            {
                                times.Add(newsTime);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print($"ERROR loading news: {ex.Message}");
            }
            return times;
        }

        private bool IsNearNewsTime(DateTime currentTime)
        {
            return _newsTimes.Any(newsTime =>
                currentTime >= newsTime.AddMinutes(0 * BufferMinutes) &&
                currentTime <= newsTime.AddMinutes(1 * BufferMinutes)
            );
        }

        private double getCorrectTradeAmount(double stopLoss)
        {
            var tradeAmount = (Account.Equity * risk) / (stopLoss * Symbol.PipValue);
            return Symbol.NormalizeVolumeInUnits(tradeAmount, RoundingMode.Down);
        }

        private double CalculateSharpeRatio()
        {
            if (_returns.Count < 2) return 0;

            double meanReturn = _returns.Average() * 252; // Annualize
            double stdDev = CalculateStandardDeviation() * Math.Sqrt(252); // Annualize

            return stdDev == 0 ? 0 : (meanReturn - risk) / stdDev;
        }

        private double CalculateStandardDeviation()
        {
            if (_returns.Count < 2) return 0;
            double mean = _returns.Average();
            double sumOfSquares = _returns.Sum(x => Math.Pow(x - mean, 2));
            return Math.Sqrt(sumOfSquares / (_returns.Count - 1));
        }

        private void PositionsOnClosed(PositionClosedEventArgs args)
        {
            var position = args.Position;
            double pnl = position.NetProfit;

            // Track profit/loss
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

            // Track returns for Sharpe ratio
            double currentEquity = Account.Equity;
            double dailyReturn = _previousEquity != 0 ? (currentEquity - _previousEquity) / _previousEquity : 0;
            _returns.Add(dailyReturn);
            _previousEquity = currentEquity;

            // Update drawdown
            _peakBalance = Math.Max(_peakBalance, Account.Equity);
            _maxDrawdown = Math.Max(_maxDrawdown, _peakBalance - Account.Equity);
        }

        private double CalculatePositionSize(double StopLossPips)
        {
            double volume = 100 / (StopLossPips * GetPipValue());
            return Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
        }

        private double GetPipValue()
        {
            return (Symbol.PipSize / Symbol.TickSize) * Symbol.TickValue;
        }
    }
}
