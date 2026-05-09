using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using NinjaTrader.Custom.AddOns.Automation;

namespace NinjaTraderAddOnProject
{
    public partial class BatchControl : UserControl
    {
        private bool isRunning;
        private readonly object saWindow;
        private INotifyPropertyChanged selectedTabProperties;

        private TextBox txtSourceFolder;
        private TextBox txtDestFolder;
        private TextBox outputBox;
        private Button btnStart;
        private CheckBox chkBatchMode;
        private TextBlock txtModeStatus;
        private TextBlock txtTemplateCount;
        private StackPanel batchPanel;
        private Expander expander;
        private FileSystemWatcher commandWatcher;

        public BatchControl(object sa)
        {
            saWindow = sa;
            InitializeUI();
            SubscribeToStrategyChanges();
            SetupIPC();
        }

        private void InitializeUI()
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));

            expander = new Expander
            {
                Header = "BATCH STRATEGY ANALYZER",
                IsExpanded = false,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };

            StackPanel main = new StackPanel { Margin = new Thickness(5, 3, 5, 5) };

            txtModeStatus = new TextBlock
            {
                Text = "Analyzer type: unknown",
                FontSize = 10,
                Foreground = Brushes.Silver,
                Margin = new Thickness(0, 0, 0, 4)
            };
            main.Children.Add(txtModeStatus);

            chkBatchMode = new CheckBox
            {
                Content = "Batch mode",
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4)
            };
            chkBatchMode.Checked += (s, e) => UpdateBatchPanelVisibility();
            chkBatchMode.Unchecked += (s, e) => UpdateBatchPanelVisibility();
            main.Children.Add(chkBatchMode);

            batchPanel = new StackPanel { Visibility = Visibility.Collapsed };

            batchPanel.Children.Add(new TextBlock { Text = "Template source folder", FontSize = 10, Foreground = Brushes.Silver });
            Grid sourceGrid = CreateFolderRow(out txtSourceFolder, out Button btnBrowseSource);
            btnBrowseSource.Click += (s, e) =>
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtSourceFolder.Text = dialog.SelectedPath;
                    RefreshTemplateCount();
                }
            };
            batchPanel.Children.Add(sourceGrid);

            txtTemplateCount = new TextBlock
            {
                Text = "0 templates found",
                FontSize = 10,
                Foreground = Brushes.Silver,
                Margin = new Thickness(0, 1, 0, 4)
            };
            batchPanel.Children.Add(txtTemplateCount);

            batchPanel.Children.Add(new TextBlock { Text = "Result export folder", FontSize = 10, Margin = new Thickness(0, 5, 0, 0), Foreground = Brushes.Silver });
            Grid destGrid = CreateFolderRow(out txtDestFolder, out Button btnBrowseDest);
            btnBrowseDest.Click += (s, e) =>
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    txtDestFolder.Text = dialog.SelectedPath;
            };
            batchPanel.Children.Add(destGrid);

            btnStart = new Button { Content = "RUN BATCH BACKTEST", Height = 25, Margin = new Thickness(0, 10, 0, 5), FontWeight = FontWeights.Bold };
            btnStart.Click += btnStart_Click;
            batchPanel.Children.Add(btnStart);

            outputBox = new TextBox
            {
                IsReadOnly = true,
                Height = 76,
                Margin = new Thickness(0, 5, 0, 5),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Black,
                Foreground = Brushes.Lime,
                FontSize = 9,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0)
            };
            batchPanel.Children.Add(outputBox);

            main.Children.Add(batchPanel);
            expander.Content = main;
            Content = expander;

            Loaded += (s, e) => RefreshForAnalyzerSelection();
        }

        private Grid CreateFolderRow(out TextBox textBox, out Button browseButton)
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            textBox = new TextBox
            {
                Margin = new Thickness(0, 2, 5, 2),
                FontSize = 10,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Gray
            };
            textBox.TextChanged += (s, e) => RefreshTemplateCount();

            browseButton = new Button { Content = "...", Width = 28, Margin = new Thickness(0, 2, 0, 2) };
            grid.Children.Add(textBox);
            grid.Children.Add(browseButton);
            Grid.SetColumn(browseButton, 1);
            return grid;
        }

        private void SubscribeToStrategyChanges()
        {
            try
            {
                var viewModel = saWindow.GetType().GetProperty("ViewModel", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saWindow) as INotifyPropertyChanged;
                if (viewModel == null)
                {
                    Log("Could not subscribe to Strategy Analyzer settings.");
                    return;
                }

                viewModel.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == "SelectedTab" || e.PropertyName == "Strategy" || e.PropertyName == "StrategyTemplate" || e.PropertyName == "ConverterSelectedTabProperties")
                        Dispatcher.Invoke(RefreshForAnalyzerSelection);
                };

                Dispatcher.BeginInvoke(new Action(SubscribeToSelectedTabProperties));
            }
            catch (Exception ex)
            {
                Log("Subscription error: " + ex.Message);
            }
        }

        private void SubscribeToSelectedTabProperties()
        {
            try
            {
                var props = StrategyAnalyzerAutomation.GetSelectedTabProperties(saWindow) as INotifyPropertyChanged;
                if (ReferenceEquals(props, selectedTabProperties))
                    return;

                if (selectedTabProperties != null)
                    selectedTabProperties.PropertyChanged -= SelectedTabProperties_PropertyChanged;

                selectedTabProperties = props;
                if (selectedTabProperties != null)
                    selectedTabProperties.PropertyChanged += SelectedTabProperties_PropertyChanged;
            }
            catch (Exception ex)
            {
                Log("Settings subscription error: " + ex.Message);
            }
        }

        private void SelectedTabProperties_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "BacktestType" || e.PropertyName == "Strategy" || e.PropertyName == "StrategyTemplate")
                Dispatcher.Invoke(RefreshForAnalyzerSelection);
        }

        private void RefreshForAnalyzerSelection()
        {
            SubscribeToSelectedTabProperties();

            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string output = Path.Combine(downloads, "output");
            if (!Directory.Exists(output))
                Directory.CreateDirectory(output);
            if (string.IsNullOrWhiteSpace(txtDestFolder.Text))
                txtDestFolder.Text = output;

            string strategyName = StrategyAnalyzerAutomation.GetSelectedStrategyName(saWindow);
            string backtestType = StrategyAnalyzerAutomation.GetSelectedBacktestType(saWindow);
            
            Log("Detected: " + (strategyName ?? "None") + " (" + (backtestType ?? "None") + ")");
            
            txtModeStatus.Text = "Analyzer type: " + (string.IsNullOrEmpty(backtestType) ? "unknown" : backtestType);
            btnStart.Content = backtestType == "Optimize" || backtestType == "MultiObjective" ? "RUN BATCH OPTIMIZATION" : "RUN BATCH BACKTEST";

            if (!string.IsNullOrEmpty(strategyName))
            {
                string ntTemplates = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "templates", "Strategy", strategyName);
                if (!isRunning && Directory.Exists(ntTemplates))
                {
                    txtSourceFolder.Text = ntTemplates;
                    Log("Source folder updated.");
                }
            }

            RefreshTemplateCount();
        }

        private void UpdateBatchPanelVisibility()
        {
            bool enabled = chkBatchMode.IsChecked == true;
            batchPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            expander.IsExpanded = enabled;
        }

        private void RefreshTemplateCount()
        {
            if (txtTemplateCount == null || txtSourceFolder == null)
                return;

            int count = Directory.Exists(txtSourceFolder.Text) ? Directory.GetFiles(txtSourceFolder.Text, "*.xml").Length : 0;
            txtTemplateCount.Text = count == 1 ? "1 template found" : count.ToString(CultureInfo.InvariantCulture) + " templates found";
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning) return;
            isRunning = true;
            btnStart.IsEnabled = false;

            try
            {
                await RunBatch();
            }
            finally
            {
                isRunning = false;
                btnStart.IsEnabled = true;
            }
        }

        private void SetupIPC()
        {
            try
            {
                string tempDir = @"C:\temp";
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                commandWatcher = new FileSystemWatcher(tempDir, "nt8_command.json");
                commandWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size;
                commandWatcher.Changed += OnCommandFileChanged;
                commandWatcher.Created += OnCommandFileChanged;
                commandWatcher.EnableRaisingEvents = true;
                Log("IPC watcher ready at C:\\temp\\nt8_command.json.");
            }
            catch (Exception ex)
            {
                Log("IPC setup error: " + ex.Message);
            }
        }

        private void OnCommandFileChanged(object sender, FileSystemEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(250);
                try
                {
                    string json = File.ReadAllText(e.FullPath);
                    if (json.IndexOf("RunBatch", StringComparison.OrdinalIgnoreCase) < 0)
                        return;

                    string sourceFolder = ExtractJsonString(json, "sourceFolder");
                    string destFolder = ExtractJsonString(json, "destFolder");

                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!string.IsNullOrWhiteSpace(sourceFolder))
                            txtSourceFolder.Text = sourceFolder;
                        if (!string.IsNullOrWhiteSpace(destFolder))
                            txtDestFolder.Text = destFolder;

                        chkBatchMode.IsChecked = true;
                        btnStart_Click(null, null);
                    }));
                }
                catch (Exception ex)
                {
                    Log("IPC command error: " + ex.Message);
                }
            });
        }

        private string ExtractJsonString(string json, string propertyName)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value.Replace("\\\\", "\\").Replace("\\\"", "\"") : null;
        }

        private async Task RunBatch()
        {
            string sourceFolder = string.Empty;
            string destFolder = string.Empty;
            Dispatcher.Invoke(() =>
            {
                sourceFolder = txtSourceFolder.Text;
                destFolder = txtDestFolder.Text;
            });

            if (!Directory.Exists(sourceFolder)) { Log("Invalid source folder."); return; }
            if (!Directory.Exists(destFolder)) { Log("Invalid destination folder."); return; }

            var templates = Directory.GetFiles(sourceFolder, "*.xml").ToList();
            if (templates.Count == 0) { Log("No .xml templates found."); return; }

            Log("Starting batch with " + templates.Count + " templates.");
            foreach (string path in templates)
            {
                if (!isRunning) break;

                string templateName = Path.GetFileNameWithoutExtension(path);
                XElement element = XElement.Load(path);
                int resultCountBeforeRun = 0;
                string originalInstrument = string.Empty;

                Log("Processing in new tab: " + templateName);
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        originalInstrument = StrategyAnalyzerAutomation.GetSelectedInstrumentOrInstrumentList(saWindow);
                        StrategyAnalyzerAutomation.AddNewTab(saWindow);
                        StrategyAnalyzerAutomation.LoadTemplate(saWindow, element);
                        StrategyAnalyzerAutomation.SetSelectedInstrumentOrInstrumentList(saWindow, originalInstrument);
                        Log("Loaded template state: " + StrategyAnalyzerAutomation.GetSelectedTemplateDebug(saWindow));
                        resultCountBeforeRun = StrategyAnalyzerAutomation.GetSelectedResultCount(saWindow);
                    }
                    catch (Exception ex)
                    {
                        Log("Setup error: " + Unwrap(ex));
                    }
                });

                // Small delay to allow UI to settle after template load
                await Task.Delay(1000);

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        StrategyAnalyzerAutomation.Run(saWindow);
                    }
                    catch (Exception ex)
                    {
                        Log("Run error: " + Unwrap(ex));
                    }
                });

                Log("Running backtest...");
                bool completed = await WaitForRunCompletion(resultCountBeforeRun, TimeSpan.FromMinutes(10));
                if (!completed)
                    Log("Timed out waiting for results; exporting whatever is available.");

                ExportResults(templateName, destFolder);
            }

            Log("Batch completed.");
        }

        private async Task<bool> WaitForRunCompletion(int resultCountBeforeRun, TimeSpan timeout)
        {
            DateTime deadline = DateTime.Now.Add(timeout);
            int stableCount = 0;
            int lastCount = resultCountBeforeRun;
            int elapsedSeconds = 0;

            while (DateTime.Now < deadline)
            {
                int currentCount = 0;
                bool busy = false;
                Dispatcher.Invoke(() =>
                {
                    currentCount = StrategyAnalyzerAutomation.GetSelectedResultCount(saWindow);
                    busy = StrategyAnalyzerAutomation.IsSelectedTabBusy(saWindow);
                });

                // Stability check: Wait for results to appear AND for the busy indicator to be clear for 3 consecutive seconds
                if (currentCount > resultCountBeforeRun && !busy)
                {
                    if (currentCount == lastCount) stableCount++;
                    else stableCount = 0;

                    if (stableCount >= 3) return true;
                }
                else
                {
                    stableCount = 0;
                }

                lastCount = currentCount;
                elapsedSeconds++;
                if (elapsedSeconds % 10 == 0)
                    Log("Waiting for run: results=" + currentCount.ToString(CultureInfo.InvariantCulture) + ", busy=" + busy.ToString(CultureInfo.InvariantCulture) + ".");
                await Task.Delay(1000);
            }
            return false;
        }

        private void ExportResults(string templateName, string destFolder)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    string subFolder = Path.Combine(destFolder, templateName);
                    if (!Directory.Exists(subFolder))
                        Directory.CreateDirectory(subFolder);

                    List<object> results = StrategyAnalyzerAutomation.GetSelectedResults(saWindow).ToList();

                    List<string> nativeExports = StrategyAnalyzerAutomation.ExportSelectedTradePerformanceGrids(saWindow, subFolder);
                    if (nativeExports.Count > 0)
                        Log("Native grid export wrote " + nativeExports.Count + " CSV file(s).");
                    else
                        Log("Native grid export unavailable; writing internal CSV files.");

                    string summaryPath = Path.Combine(subFolder, "Summary.csv");
                    string tradesPath = Path.Combine(subFolder, "Trades.csv");
                    string ordersPath = Path.Combine(subFolder, "Orders.csv");
                    string executionsPath = Path.Combine(subFolder, "Executions.csv");
                    string analysisPath = Path.Combine(subFolder, "Analysis.csv");

                    if (!File.Exists(summaryPath)) WriteSummaryCsv(summaryPath, results);
                    if (!File.Exists(tradesPath)) WriteTradesCsv(tradesPath, results);
                    if (!File.Exists(ordersPath)) WriteOrdersCsv(ordersPath, results);
                    if (!File.Exists(executionsPath)) WriteExecutionsCsv(executionsPath, results);
                    if (!File.Exists(analysisPath)) WriteAnalysisCsv(analysisPath, results);

                    Log("Exported comprehensive results to " + subFolder);
                }
                catch (Exception ex)
                {
                    Log("Export logic error: " + Unwrap(ex));
                }
            });
        }

        private void WriteSummaryCsv(string fileName, List<object> results)
        {
            var sb = new StringBuilder();
            string[] headers = new[]
            {
                "StrategyName", "Instrument", "From", "To", "Parameters", "TotalNetProfit", "GrossProfit",
                "GrossLoss", "ProfitFactor", "MaxDrawdown", "TotalNumTrades", "PercentProfitable",
                "AverageTrade", "SharpeRatio", "SortinoRatio"
            };
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

            foreach (object result in results)
            {
                object summary = GetProperty(result, "SummaryPerformancesCurrency") ?? GetProperty(result, "SummaryPerformances");
                object allPerformance = GetProperty(summary, "All");
                string[] values = new[]
                {
                    Convert.ToString(GetProperty(result, "StrategyName"), CultureInfo.InvariantCulture),
                    Convert.ToString(GetProperty(result, "Instrument"), CultureInfo.InvariantCulture),
                    Convert.ToString(GetProperty(result, "From"), CultureInfo.InvariantCulture),
                    Convert.ToString(GetProperty(result, "To"), CultureInfo.InvariantCulture),
                    Convert.ToString(GetProperty(result, "ParametersString"), CultureInfo.InvariantCulture),
                    FormatMetric(GetProperty(allPerformance, "TotalNetProfit")),
                    FormatMetric(GetProperty(allPerformance, "GrossProfit")),
                    FormatMetric(GetProperty(allPerformance, "GrossLoss")),
                    FormatMetric(GetProperty(allPerformance, "ProfitFactor")),
                    FormatMetric(GetProperty(allPerformance, "MaxDrawdown")),
                    FormatMetric(GetProperty(allPerformance, "TotalNumTrades")),
                    FormatMetric(GetProperty(allPerformance, "PercentProfitable")),
                    FormatMetric(GetProperty(allPerformance, "AverageTrade")),
                    FormatMetric(GetProperty(allPerformance, "SharpeRatio")),
                    FormatMetric(GetProperty(allPerformance, "SortinoRatio"))
                };
                sb.AppendLine(string.Join(",", values.Select(EscapeCsv)));
            }
            File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
        }

        private void WriteTradesCsv(string fileName, List<object> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ResultIdx,EntryTime,ExitTime,MarketPosition,Quantity,EntryPrice,ExitPrice,Profit,CumulativeProfit");
            
            int resultIdx = 0;
            foreach (object result in results)
            {
                object strategy = GetProperty(result, "ResultsStrategy");
                if (strategy == null) continue;
                
                object performance = GetProperty(strategy, "SystemPerformance");
                object allTrades = GetProperty(performance, "AllTrades");
                IEnumerable trades = allTrades as IEnumerable;
                if (trades == null) continue;

                double cumulativeProfit = 0;
                foreach (object trade in trades)
                {
                    double profit = Convert.ToDouble(GetProperty(trade, "ProfitCurrency"), CultureInfo.InvariantCulture);
                    cumulativeProfit += profit;
                    
                    sb.AppendLine(string.Join(",", new[] {
                        resultIdx.ToString(),
                        FormatDate(GetProperty(GetProperty(trade, "Entry"), "Time")),
                        FormatDate(GetProperty(GetProperty(trade, "Exit"), "Time")),
                        Convert.ToString(GetProperty(trade, "MarketPosition")),
                        Convert.ToString(GetProperty(trade, "Quantity")),
                        FormatMetric(GetProperty(GetProperty(trade, "Entry"), "Price")),
                        FormatMetric(GetProperty(GetProperty(trade, "Exit"), "Price")),
                        FormatMetric(profit),
                        FormatMetric(cumulativeProfit)
                    }.Select(EscapeCsv)));
                }
                resultIdx++;
            }
            File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
        }

        private void WriteOrdersCsv(string fileName, List<object> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ResultIdx,Time,Name,OrderType,OrderState,Instrument,Quantity,LimitPrice,StopPrice,AverageFillPrice");
            
            int resultIdx = 0;
            foreach (object result in results)
            {
                IEnumerable orders = GetProperty(result, "Orders") as IEnumerable;
                if (orders == null) continue;

                foreach (object order in orders)
                {
                    sb.AppendLine(string.Join(",", new[] {
                        resultIdx.ToString(),
                        FormatDate(GetProperty(order, "Time")),
                        Convert.ToString(GetProperty(order, "Name")),
                        Convert.ToString(GetProperty(order, "OrderType")),
                        Convert.ToString(GetProperty(order, "OrderState")),
                        Convert.ToString(GetProperty(order, "Instrument")),
                        Convert.ToString(GetProperty(order, "Quantity")),
                        FormatMetric(GetProperty(order, "LimitPrice")),
                        FormatMetric(GetProperty(order, "StopPrice")),
                        FormatMetric(GetProperty(order, "AverageFillPrice"))
                    }.Select(EscapeCsv)));
                }
                resultIdx++;
            }
            File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
        }

        private void WriteExecutionsCsv(string fileName, List<object> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ResultIdx,Time,Name,Instrument,MarketPosition,Quantity,Price,ExecutionId,OrderId");
            
            int resultIdx = 0;
            foreach (object result in results)
            {
                IEnumerable executions = GetProperty(result, "Executions") as IEnumerable;
                if (executions == null) continue;

                foreach (object exec in executions)
                {
                    sb.AppendLine(string.Join(",", new[] {
                        resultIdx.ToString(),
                        FormatDate(GetProperty(exec, "Time")),
                        Convert.ToString(GetProperty(exec, "Name")),
                        Convert.ToString(GetProperty(exec, "Instrument")),
                        Convert.ToString(GetProperty(exec, "MarketPosition")),
                        Convert.ToString(GetProperty(exec, "Quantity")),
                        FormatMetric(GetProperty(exec, "Price")),
                        Convert.ToString(GetProperty(exec, "ExecutionId")),
                        Convert.ToString(GetProperty(exec, "OrderId"))
                    }.Select(EscapeCsv)));
                }
                resultIdx++;
            }
            File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
        }

        private void WriteAnalysisCsv(string fileName, List<object> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ResultIdx,Metric,All,Long,Short");

            string[] metrics =
            {
                "TotalNetProfit", "GrossProfit", "GrossLoss", "ProfitFactor", "MaxDrawdown",
                "TotalNumTrades", "PercentProfitable", "AverageTrade", "AverageWinningTrade",
                "AverageLosingTrade", "LargestWinningTrade", "LargestLosingTrade", "SharpeRatio",
                "SortinoRatio", "UlcerIndex", "RSquared"
            };

            for (int resultIdx = 0; resultIdx < results.Count; resultIdx++)
            {
                object summary = GetProperty(results[resultIdx], "SummaryPerformancesCurrency") ?? GetProperty(results[resultIdx], "SummaryPerformances");
                object all = GetProperty(summary, "All");
                object longPerf = GetProperty(summary, "Long");
                object shortPerf = GetProperty(summary, "Short");

                foreach (string metric in metrics)
                {
                    sb.AppendLine(string.Join(",", new[]
                    {
                        resultIdx.ToString(CultureInfo.InvariantCulture),
                        metric,
                        FormatMetric(GetProperty(all, metric)),
                        FormatMetric(GetProperty(longPerf, metric)),
                        FormatMetric(GetProperty(shortPerf, metric))
                    }.Select(EscapeCsv)));
                }
            }

            File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
        }

        private object GetProperty(object instance, string propertyName)
        {
            return instance?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
        }

        private string FormatDate(object value)
        {
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private string FormatMetric(object value)
        {
            if (value == null) return string.Empty;
            if (value is double d) return d.ToString("0.####", CultureInfo.InvariantCulture);
            var formattable = value as IFormattable;
            return formattable != null ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
        }

        private string EscapeCsv(string value)
        {
            if (value == null) return string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private string Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;
            return ex.Message;
        }

        private void Log(string msg)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (outputBox == null)
                    return;

                outputBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "] " + msg + Environment.NewLine);
                outputBox.ScrollToEnd();
                NinjaTrader.Code.Output.Process("BatchStrategyOptimizer: " + msg, NinjaTrader.NinjaScript.PrintTo.OutputTab1);
            }));
        }
    }
}
