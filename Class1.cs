using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.Indicators;

namespace Bot_A
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class ElGranUKOILBot : Robot
    {
        [Parameter("Symbol Filter", DefaultValue = "UKOILm")]
        public string SymbolFilter { get; set; }

        [Parameter("Risk %", DefaultValue = 1.0, MinValue = 0.1, MaxValue = 2.0)]
        public double RiskPercent { get; set; }

        [Parameter("Trend MA Period (90)", DefaultValue = 90)]
        public int TrendMAPeriod { get; set; }

        [Parameter("Fast MA Period (3)", DefaultValue = 3)]
        public int FastMAPeriod { get; set; }

        [Parameter("Slow MA Period (6)", DefaultValue = 6)]
        public int SlowMAPeriod { get; set; }

        [Parameter("Stop Loss Pips", DefaultValue = 20, MinValue = 10)]
        public double StopLossPips { get; set; }

        [Parameter("Take Profit Pips", DefaultValue = 60, MinValue = 30)]
        public double TakeProfitPips { get; set; }

        [Parameter("Max Daily Trades", DefaultValue = 3)]
        public int MaxDailyTrades { get; set; }

        [Parameter("Focus Hours Start (EAT)", DefaultValue = 17)]  // 5 PM EAT
        public int FocusStartHour { get; set; }

        [Parameter("Focus Hours End (EAT)", DefaultValue = 22)]    // 10 PM EAT
        public int FocusEndHour { get; set; }

        private MovingAverage trendMA;
        private MovingAverage fastMA;
        private MovingAverage slowMA;
        private int dailyTrades;
        private DateTime lastTradeDay;

        protected override void OnStart()
        {
            // Validate symbol
            if (SymbolName != SymbolFilter)
            {
                Print($"❌ Bot configured for {SymbolFilter} only. Current: {SymbolName}");
                Stop();
                return;
            }

            // Initialize MAs
            // Note: In newer cTrader versions, MarketSeries is replaced by Bars
            trendMA = Indicators.MovingAverage(Bars.ClosePrices, TrendMAPeriod, MovingAverageType.Weighted);
            fastMA = Indicators.MovingAverage(Bars.ClosePrices, FastMAPeriod, MovingAverageType.Weighted);
            slowMA = Indicators.MovingAverage(Bars.ClosePrices, SlowMAPeriod, MovingAverageType.Weighted);

            Print($"✅ ElGran UKOIL Bot ACTIVE | Risk: {RiskPercent}% | SL: {StopLossPips}p | TP: {TakeProfitPips}p");
        }

        protected override void OnBar()
        {
            // Ensure enough data for indicators
            if (Bars.Count < Math.Max(TrendMAPeriod, SlowMAPeriod) + 5)
                return;

            // Daily reset
            if (Server.Time.Date != lastTradeDay)
            {
                dailyTrades = 0;
                lastTradeDay = Server.Time.Date;
            }

            // Focus time check (EAT window)
            int currentHour = Server.Time.Hour;
            if (currentHour < FocusStartHour || currentHour >= FocusEndHour)
                return;

            // Max trades check
            if (dailyTrades >= MaxDailyTrades)
                return;

            // No open positions
            if (Positions.Count > 0)
                return;

            // Trend filter: 90 WMA direction
            bool upTrend = trendMA.Result.Last(0) > trendMA.Result.Last(5);
            bool downTrend = trendMA.Result.Last(0) < trendMA.Result.Last(5);

            if (!upTrend && !downTrend)
                return;

            // Entry signals: 3 WMA > 6 WMA crossover
            bool bullCross = fastMA.Result.Last(1) <= slowMA.Result.Last(1) &&
                             fastMA.Result.Last(0) > slowMA.Result.Last(0) && upTrend;

            bool bearCross = fastMA.Result.Last(1) >= slowMA.Result.Last(1) &&
                             fastMA.Result.Last(0) < slowMA.Result.Last(0) && downTrend;

            if (bullCross)
            {
                ExecuteTrade(TradeType.Buy);
            }
            else if (bearCross)
            {
                ExecuteTrade(TradeType.Sell);
            }
        }

        private void ExecuteTrade(TradeType tradeType)
        {
            double volumeInUnits = CalculateVolume();

            var result = ExecuteMarketOrder(tradeType, SymbolName, volumeInUnits, "ElGranBot",
                                          StopLossPips, TakeProfitPips);

            if (result.IsSuccessful)
            {
                dailyTrades++;
                Print($"✅ TRADE #{dailyTrades} | {tradeType} | Vol: {volumeInUnits} | Equity: {Account.Equity:F2}");
            }
            else
            {
                Print($"❌ Trade failed: {result.Error}");
            }
        }

        private double CalculateVolume()
        {
            double riskAmount = Account.Equity * RiskPercent / 100;
            double volumeInUnits = riskAmount / (StopLossPips * Symbol.PipValue);
            return Symbol.NormalizeVolumeInUnits(volumeInUnits, RoundingMode.ToNearest);
        }

        protected override void OnStop()
        {
            Print("🛑 ElGran Bot STOPPED | Daily Trades: " + dailyTrades);
        }
    }
}