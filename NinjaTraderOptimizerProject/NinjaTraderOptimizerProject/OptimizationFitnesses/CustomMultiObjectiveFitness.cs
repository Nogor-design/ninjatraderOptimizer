#region Using declarations
using NinjaTrader.NinjaScript;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Xml.Serialization;
#endregion

namespace NinjaTrader.NinjaScript.OptimizationFitnesses
{
    public class CustomMultiObjectiveFitness : OptimizationFitness
    {
        [NinjaScriptProperty]
        [Display(Name = "Min Trades", Description = "Minimum number of trades required", Order = 1, GroupName = "Filters")]
        public int MinTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Max Drawdown %", Description = "Maximum allowable drawdown percentage", Order = 2, GroupName = "Filters")]
        public double MaxDrawdownPct { get; set; }

        [Browsable(false)]
        public double Objective1 { get; set; }

        [Browsable(false)]
        public double Objective2 { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CustomMultiObjectiveFitness";
                MinTrades = 30;
                MaxDrawdownPct = 20.0;
            }
        }

        protected override void OnCalculatePerformanceValue(StrategyBase strategy)
        {
            if (strategy == null || strategy.SystemPerformance == null || strategy.SystemPerformance.AllTrades == null)
                return;

            var allTrades = strategy.SystemPerformance.AllTrades;
            var tradesPerformance = allTrades.TradesPerformance;

            // Hard Constraints
            if (allTrades.Count < MinTrades)
            {
                Value = 0;
                Objective1 = 0;
                Objective2 = 0;
                return;
            }

            double totalProfit = tradesPerformance.NetProfit;
            double profitFactor = tradesPerformance.ProfitFactor;
            double drawdownPct = Math.Abs(tradesPerformance.Percent.Drawdown);

            if (drawdownPct > MaxDrawdownPct)
            {
                Value = 0;
                Objective1 = totalProfit;
                Objective2 = 0;
                return;
            }

            // Example Multi-Objective Logic
            // Objective 1: Profitability (Net Profit)
            Objective1 = totalProfit;

            // Objective 2: Risk-Adjusted Return (Profit Factor)
            Objective2 = double.IsNaN(profitFactor) || double.IsInfinity(profitFactor) ? 0 : profitFactor;

            Value = Objective2; // Default simple ranking
        }
    }
}
