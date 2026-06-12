#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.IO;
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
		private FileSystemWatcher compileCommandWatcher;
		private System.Windows.Threading.DispatcherTimer compileCommandPollTimer;
		private readonly object compileCommandSync = new object();
		private string compileCommandLastJson;
		private bool compileCommandProcessing;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "Automated batch execution of Strategy Analyzer templates (Injected)";
				Name = "Batch Strategy Optimizer";
			}
			else if (State == State.Active)
			{
				SetupCompileObserverIpc();
			}
			else if (State == State.Terminated)
			{
				if (compileCommandWatcher != null)
				{
					compileCommandWatcher.EnableRaisingEvents = false;
					compileCommandWatcher.Dispose();
					compileCommandWatcher = null;
				}
				if (compileCommandPollTimer != null)
				{
					compileCommandPollTimer.Stop();
					compileCommandPollTimer = null;
				}
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

		private void SetupCompileObserverIpc()
		{
			try
			{
				string tempDir = @"C:\temp";
				if (!Directory.Exists(tempDir))
					Directory.CreateDirectory(tempDir);

				compileCommandWatcher = new FileSystemWatcher(tempDir, "nt8_command.json");
				compileCommandWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName;
				compileCommandWatcher.Changed += OnCompileCommandFileChanged;
				compileCommandWatcher.Created += OnCompileCommandFileChanged;
				compileCommandWatcher.Renamed += OnCompileCommandFileChanged;
				compileCommandWatcher.EnableRaisingEvents = true;

				compileCommandPollTimer = new System.Windows.Threading.DispatcherTimer();
				compileCommandPollTimer.Interval = TimeSpan.FromSeconds(1);
				compileCommandPollTimer.Tick += (sender, args) => ProcessCompileCommandFile(Path.Combine(tempDir, "nt8_command.json"));
				compileCommandPollTimer.Start();
				NinjaTrader.Code.Output.Process("BatchStrategyOptimizer: global ObserveCompile watcher ready.", PrintTo.OutputTab1);
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process("BatchStrategyOptimizer ObserveCompile IPC setup error: " + ex.Message, PrintTo.OutputTab1);
			}
		}

		private void OnCompileCommandFileChanged(object sender, FileSystemEventArgs e)
		{
			ProcessCompileCommandFile(e.FullPath);
		}

		private void ProcessCompileCommandFile(string path)
		{
			_ = Task.Run(async () =>
			{
				await Task.Delay(250);
				try
				{
					if (!File.Exists(path))
						return;
					string json = File.ReadAllText(path);
					bool isCompile = NinjaTraderAddOnProject.CompileObserverService.IsObserveCompileCommand(json);
					bool isLifecycle = NinjaTraderAddOnProject.StrategyLifecycleService.IsLifecycleCommand(json);
					if (!isCompile && !isLifecycle)
						return;
					lock (compileCommandSync)
					{
						if (compileCommandProcessing || string.Equals(compileCommandLastJson, json, StringComparison.Ordinal))
							return;
						compileCommandProcessing = true;
						compileCommandLastJson = json;
					}
					if (isCompile)
						await NinjaTraderAddOnProject.CompileObserverService.ObserveCompile(
							json,
							message => NinjaTrader.Code.Output.Process("BatchStrategyOptimizer: " + message, PrintTo.OutputTab1));
					else
						await NinjaTraderAddOnProject.StrategyLifecycleService.Handle(
							json,
							message => NinjaTrader.Code.Output.Process("BatchStrategyOptimizer: " + message, PrintTo.OutputTab1));
				}
				catch (Exception ex)
				{
					NinjaTrader.Code.Output.Process("BatchStrategyOptimizer ObserveCompile command error: " + ex.Message, PrintTo.OutputTab1);
				}
				finally
				{
					lock (compileCommandSync)
						compileCommandProcessing = false;
				}
			});
		}
	}
}
