#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using System.Windows.Controls.WpfPropertyGrid;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
	public class BatchStrategyOptimizerAddOn : AddOnBase
	{
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Automated batch execution of Strategy Analyzer templates (Injected)";
				Name = "Batch Strategy Optimizer";
			}
		}

		protected override void OnWindowCreated(Window window)
		{
			if (window.GetType().Name == "StrategyAnalyzer")
			{
				InjectBatchControl(window);
			}
		}

		private void InjectBatchControl(Window saWindow)
		{
			saWindow.Dispatcher.BeginInvoke(new Action(() => {
				try {
					// Find the PropertyGrid in the StrategyAnalyzer
					var field = saWindow.GetType().GetField("propertyGrid", BindingFlags.NonPublic | BindingFlags.Instance);
					PropertyGrid pg = field?.GetValue(saWindow) as PropertyGrid;

					if (pg != null)
					{
						var parent = VisualTreeHelper.GetParent(pg) as Panel;
						if (parent != null)
						{
							// Robust injection: Use a DockPanel decorator to wrap the existing PropertyGrid
							int index = -1;
							for (int i = 0; i < parent.Children.Count; i++) {
								if (parent.Children[i] == pg) { index = i; break; }
							}

							if (index != -1)
							{
								parent.Children.RemoveAt(index);

								DockPanel dp = new DockPanel { LastChildFill = true };
								var batchControl = new NinjaTraderAddOnProject.BatchControl(saWindow);
								
								DockPanel.SetDock(batchControl, Dock.Top);
								dp.Children.Add(batchControl);
								dp.Children.Add(pg); // PropertyGrid fills the rest

								parent.Children.Insert(index, dp);
								NinjaTrader.Code.Output.Process("BatchStrategyOptimizer: Decorated Settings Panel.", PrintTo.OutputTab1);
							}
						}
					}
				} catch (Exception ex) {
					NinjaTrader.Code.Output.Process("BatchStrategyOptimizer Injection Error: " + ex.Message, PrintTo.OutputTab1);
				}
			}));
		}
	}
}
