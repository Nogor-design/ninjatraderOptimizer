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
        private Button btnCancel;
        private CheckBox chkBatchMode;
        private TextBlock txtModeStatus;
        private TextBlock txtTemplateCount;
        private StackPanel batchPanel;
        private Expander expander;
        private FileSystemWatcher commandWatcher;
        private bool cancelRequested;
        private object currentBatchTab;

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

            Grid actionGrid = new Grid { Margin = new Thickness(0, 10, 0, 5) };
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            btnStart = new Button { Content = "RUN BATCH BACKTEST", Height = 25, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 5, 0) };
            btnStart.Click += btnStart_Click;
            actionGrid.Children.Add(btnStart);

            btnCancel = new Button { Content = "CANCEL", Height = 25, Width = 70, IsEnabled = false, FontWeight = FontWeights.Bold };
            btnCancel.Click += btnCancel_Click;
            Grid.SetColumn(btnCancel, 1);
            actionGrid.Children.Add(btnCancel);
            batchPanel.Children.Add(actionGrid);

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
            cancelRequested = false;
            btnStart.IsEnabled = false;
            btnCancel.IsEnabled = true;

            try
            {
                await RunBatch();
            }
            finally
            {
                isRunning = false;
                cancelRequested = false;
                currentBatchTab = null;
                btnStart.IsEnabled = true;
                btnCancel.IsEnabled = false;
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (!isRunning)
                return;

            cancelRequested = true;
            btnCancel.IsEnabled = false;
            Log("Cancel requested; stopping after the active run is interrupted.");

            try
            {
                if (currentBatchTab != null)
                    StrategyAnalyzerAutomation.CloseTab(saWindow, currentBatchTab);
            }
            catch (Exception ex)
            {
                Log("Cancel close-tab error: " + Unwrap(ex));
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
                if (!isRunning || cancelRequested) break;

                string templateName = Path.GetFileNameWithoutExtension(path);
                XElement element = XElement.Load(path);
                int resultCountBeforeRun = 0;
                string originalInstrument = string.Empty;
                object batchTab = null;

                Log("Processing in new tab: " + templateName);
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        originalInstrument = StrategyAnalyzerAutomation.GetSelectedInstrumentOrInstrumentList(saWindow);
                        batchTab = StrategyAnalyzerAutomation.AddNewTab(saWindow);
                        currentBatchTab = batchTab;
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
                if (cancelRequested)
                    break;

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
                if (cancelRequested)
                {
                    CloseBatchTab(batchTab);
                    break;
                }

                if (!completed)
                    Log("Timed out waiting for results; exporting whatever is available.");

                ExportResults(templateName, destFolder);
                CloseBatchTab(batchTab);
            }

            if (cancelRequested)
                Log("Batch cancelled.");
            else
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
                if (cancelRequested)
                    return false;

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
                else if (currentCount > resultCountBeforeRun)
                {
                    if (currentCount == lastCount) stableCount++;
                    else stableCount = 0;

                    if (stableCount >= 20)
                    {
                        Log("Progress flag still busy, but results have been stable; continuing to export.");
                        return true;
                    }
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

        private void CloseBatchTab(object tab)
        {
            if (tab == null)
                return;

            Dispatcher.Invoke(() =>
            {
                try
                {
                    StrategyAnalyzerAutomation.CloseTab(saWindow, tab);
                    if (ReferenceEquals(currentBatchTab, tab))
                        currentBatchTab = null;
                }
                catch (Exception ex)
                {
                    Log("Close-tab error: " + Unwrap(ex));
                }
            });
        }

        private void ExportResults(string templateName, string destFolder)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    string subFolder = Path.Combine(destFolder, templateName);
                    if (Directory.Exists(subFolder))
                    {
                        foreach (string existingFile in Directory.GetFiles(subFolder, "*.csv"))
                            File.Delete(existingFile);
                    }
                    else
                        Directory.CreateDirectory(subFolder);

                    List<object> results = StrategyAnalyzerAutomation.GetSelectedResults(saWindow).ToList();

                    string settingsPath = Path.Combine(subFolder, "Settings.csv");
                    string summaryPath = Path.Combine(subFolder, "Summary.csv");
                    string tradesPath = Path.Combine(subFolder, "Trades.csv");
                    string ordersPath = Path.Combine(subFolder, "Orders.csv");
                    string executionsPath = Path.Combine(subFolder, "Executions.csv");
                    string analysisPath = Path.Combine(subFolder, "Analysis.csv");

                    WriteSettingsCsv(settingsPath, results);
                    WriteSummaryCsv(summaryPath, results);
                    WriteTradesCsv(tradesPath, results);
                    WriteOrdersCsv(ordersPath, results);
                    WriteExecutionsCsv(executionsPath, results);
                    WriteAnalysisCsv(analysisPath, results);

                    Log("Exported 6 internal CSV files to " + subFolder);
                }
                catch (Exception ex)
                {
                    Log("Export logic error: " + Unwrap(ex));
                }
            });
        }

        private void WriteSettingsCsv(string fileName, List<object> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Item,Value,");

            foreach (object result in results)
            {
                sb.AppendLine("Strategy parameters,,");
                foreach (KeyValuePair<string, string> parameter in ParseParameters(Convert.ToString(GetProperty(result, "ParametersString"), CultureInfo.InvariantCulture)))
                    AppendSettingRow(sb, parameter.Key, parameter.Value);

                sb.AppendLine("Data Series,,");
                AppendSettingRow(sb, "Start date", FormatNtDateOnly(GetProperty(result, "From")));
                AppendSettingRow(sb, "End date", FormatNtDateOnly(GetProperty(result, "To")));
                AppendSettingRow(sb, "Price based on", "Last");
                AppendSettingRow(sb, "Type", "Minute");
                AppendSettingRow(sb, "Value", "1");
                AppendSettingRow(sb, "Tick Replay", "False");

                sb.AppendLine("Setup,,");
                AppendSettingRow(sb, "Include commission", "False");
                AppendSettingRow(sb, "Commission template", string.Empty);
                AppendSettingRow(sb, "Label", Convert.ToString(GetProperty(result, "StrategyName"), CultureInfo.InvariantCulture));
                AppendSettingRow(sb, "Instrument", Convert.ToString(GetProperty(result, "Instrument"), CultureInfo.InvariantCulture));
            }
            File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
        }

        private void WriteSummaryCsv(string fileName, List<object> results)
        {
            var sb = new StringBuilder();

            foreach (object result in results)
            {
                object summary = GetProperty(result, "SummaryPerformancesCurrency") ?? GetProperty(result, "SummaryPerformances");
                object all = GetProperty(summary, "All");
                object longPerf = GetProperty(summary, "Long");
                object shortPerf = GetProperty(summary, "Short");

                sb.AppendLine("Performance,All trades,Long trades,Short trades,");
                AppendSummaryRow(sb, "Total net profit", all, longPerf, shortPerf, "TotalNetProfit", MetricFormat.Currency);
                AppendSummaryRow(sb, "Gross profit", all, longPerf, shortPerf, "GrossProfit", MetricFormat.Currency);
                AppendSummaryRow(sb, "Gross loss", all, longPerf, shortPerf, "GrossLoss", MetricFormat.Currency);
                AppendSummaryRow(sb, "Commission", all, longPerf, shortPerf, "Commission", MetricFormat.Currency);
                AppendSummaryRow(sb, "Profit factor", all, longPerf, shortPerf, "ProfitFactor", MetricFormat.Number);
                AppendSummaryRow(sb, "Max. drawdown", all, longPerf, shortPerf, "MaxDrawdown", MetricFormat.Currency);
                AppendSummaryRow(sb, "Sharpe ratio", all, longPerf, shortPerf, "SharpeRatio", MetricFormat.Number);
                AppendSummaryRow(sb, "Sortino ratio", all, longPerf, shortPerf, "SortinoRatio", MetricFormat.Number);
                AppendSummaryRow(sb, "Ulcer index", all, longPerf, shortPerf, "UlcerIndex", MetricFormat.Number);
                AppendSummaryRow(sb, "R squared", all, longPerf, shortPerf, "RSquared", MetricFormat.Number);
                AppendSummaryRow(sb, "Total Fees", all, longPerf, shortPerf, "Fee", MetricFormat.Currency);
                AppendSummaryRow(sb, "Probability", all, longPerf, shortPerf, "Probability", MetricFormat.Percent);
                AppendBlankSummaryRow(sb);
                AppendSingleSummaryRow(sb, "Start date", FormatNtDateOnly(GetProperty(result, "From")));
                AppendSingleSummaryRow(sb, "Start time", FormatNtTimeOnly(GetProperty(result, "From")));
                AppendSingleSummaryRow(sb, "End date", FormatNtDateOnly(GetProperty(result, "To")));
                AppendSingleSummaryRow(sb, "End time", FormatNtTimeOnly(GetProperty(result, "To")));
                AppendBlankSummaryRow(sb);
                AppendSummaryRow(sb, "Total # of trades", all, longPerf, shortPerf, "TotalNumTrades", MetricFormat.Integer);
                AppendSummaryRow(sb, "Percent profitable", all, longPerf, shortPerf, "PercentProfitable", MetricFormat.Percent);
                AppendSummaryRow(sb, "# of winning trades", all, longPerf, shortPerf, "NumWinningTrades", MetricFormat.Integer);
                AppendSummaryRow(sb, "# of losing trades", all, longPerf, shortPerf, "NumLosingTrades", MetricFormat.Integer);
                AppendSummaryRow(sb, "# of even trades", all, longPerf, shortPerf, "NumEvenTrades", MetricFormat.Integer);
                AppendBlankSummaryRow(sb);
                AppendSummaryRow(sb, "Total slippage", all, longPerf, shortPerf, "TotalSlippage", MetricFormat.Integer);
                AppendBlankSummaryRow(sb);
                AppendSummaryRow(sb, "Avg. trade", all, longPerf, shortPerf, "AverageTrade", MetricFormat.Currency);
                AppendSummaryRow(sb, "Avg. winning trade", all, longPerf, shortPerf, "AverageWinningTrade", MetricFormat.Currency);
                AppendSummaryRow(sb, "Avg. losing trade", all, longPerf, shortPerf, "AverageLosingTrade", MetricFormat.Currency);
                AppendSummaryRow(sb, "Ratio avg. win / avg. loss", all, longPerf, shortPerf, "RatioAvgWinAvgLoss", MetricFormat.Number);
                AppendBlankSummaryRow(sb);
                AppendSummaryRow(sb, "Max. consec. winners", all, longPerf, shortPerf, "MaxConsecWinners", MetricFormat.Integer);
                AppendSummaryRow(sb, "Max. consec. losers", all, longPerf, shortPerf, "MaxConsecLosers", MetricFormat.Integer);
                AppendSummaryRow(sb, "Largest winning trade", all, longPerf, shortPerf, "LargestWinningTrade", MetricFormat.Currency);
                AppendSummaryRow(sb, "Largest losing trade", all, longPerf, shortPerf, "LargestLosingTrade", MetricFormat.Currency);
                AppendBlankSummaryRow(sb);
                AppendSummaryRow(sb, "Avg. # of trades per day", all, longPerf, shortPerf, "AverageNumTradesPerDay", MetricFormat.Number);
                AppendSummaryRow(sb, "Avg. time in market", all, longPerf, shortPerf, "AverageTimeInMarket", MetricFormat.Minutes);
                AppendSummaryRow(sb, "Avg. bars in trade", all, longPerf, shortPerf, "AverageBarsInTrade", MetricFormat.Number);
            }

            File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
        }

        private void WriteTradesCsv(string fileName, List<object> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Trade number,Instrument,Account,Strategy,Market pos.,Qty,Entry price,Exit price,Entry time,Exit time,Entry name,Exit name,Profit,Cum. net profit,Commission,Clearing Fee,Exchange Fee,IP Fee,NFA Fee,MAE,MFE,ETD,Bars,");
            
            foreach (object result in results)
            {
                List<object> trades = GetTrades(result);

                double cumulativeProfit = 0;
                foreach (object trade in trades)
                {
                    object entry = GetProperty(trade, "Entry");
                    object exit = GetProperty(trade, "Exit");
                    double profit = Convert.ToDouble(GetProperty(trade, "ProfitCurrency"), CultureInfo.InvariantCulture);
                    double mfe = GetDouble(GetProperty(trade, "MfeCurrency"));
                    cumulativeProfit += profit;
                    
                    sb.AppendLine(string.Join(",", new[] {
                        Convert.ToString(GetProperty(trade, "TradeNumber"), CultureInfo.InvariantCulture),
                        FormatInstrumentName(GetProperty(entry, "Instrument")),
                        "Backtest",
                        Convert.ToString(GetProperty(result, "StrategyName"), CultureInfo.InvariantCulture),
                        Convert.ToString(GetProperty(entry, "MarketPosition"), CultureInfo.InvariantCulture),
                        Convert.ToString(GetProperty(trade, "Quantity")),
                        FormatPrice(GetProperty(entry, "Price")),
                        FormatPrice(GetProperty(exit, "Price")),
                        FormatNtDateTime(GetProperty(entry, "Time")),
                        FormatNtDateTime(GetProperty(exit, "Time")),
                        Convert.ToString(GetProperty(entry, "Name"), CultureInfo.InvariantCulture),
                        Convert.ToString(GetProperty(exit, "Name"), CultureInfo.InvariantCulture),
                        FormatCurrency(profit),
                        FormatCurrency(cumulativeProfit),
                        FormatCurrency(GetProperty(trade, "Commission")),
                        FormatCurrency(0),
                        FormatCurrency(0),
                        FormatCurrency(0),
                        FormatCurrency(0),
                        FormatCurrency(GetProperty(trade, "MaeCurrency")),
                        FormatCurrency(mfe),
                        FormatCurrency(mfe - profit),
                        FormatInteger(GetTradeBars(trade))
                    }.Select(EscapeCsv)));
                }
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
            sb.AppendLine("Period,#,Cum. net profit,Net profit,Gross profit,Gross loss,Commission,Cum. max. drawdown,Max. drawdown,% Win,Avg. trade,Avg. winner,Avg. loser,Lrg. winner,Lrg. loser,MTR,Avg. MAE,Avg. MFE,Avg. ETD,% Trade,");

            foreach (object result in results)
            {
                List<object> trades = GetTrades(result);
                if (trades.Count == 0) continue;

                double cumulativeProfit = 0;
                double peak = 0;
                foreach (var group in trades.GroupBy(t => GetTradeEntryTime(t).Date).OrderBy(g => g.Key))
                {
                    List<object> periodTrades = group.ToList();
                    double netProfit = periodTrades.Sum(t => GetDouble(GetProperty(t, "ProfitCurrency")));
                    double grossProfit = periodTrades.Where(t => GetDouble(GetProperty(t, "ProfitCurrency")) > 0).Sum(t => GetDouble(GetProperty(t, "ProfitCurrency")));
                    double grossLoss = periodTrades.Where(t => GetDouble(GetProperty(t, "ProfitCurrency")) < 0).Sum(t => GetDouble(GetProperty(t, "ProfitCurrency")));
                    double commission = periodTrades.Sum(t => GetDouble(GetProperty(t, "Commission")));
                    int winners = periodTrades.Count(t => GetDouble(GetProperty(t, "ProfitCurrency")) > 0);
                    int losers = periodTrades.Count(t => GetDouble(GetProperty(t, "ProfitCurrency")) < 0);
                    double previousCumulative = cumulativeProfit;
                    cumulativeProfit += netProfit;
                    peak = Math.Max(peak, previousCumulative);
                    double maxDrawdown = Math.Min(0, cumulativeProfit - peak);
                    peak = Math.Max(peak, cumulativeProfit);

                    sb.AppendLine(string.Join(",", new[]
                    {
                        group.Key.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
                        periodTrades.Count.ToString(CultureInfo.InvariantCulture),
                        FormatCurrency(cumulativeProfit),
                        FormatCurrency(netProfit),
                        FormatCurrency(grossProfit),
                        FormatCurrency(grossLoss),
                        FormatCurrency(commission),
                        FormatCurrency(maxDrawdown),
                        FormatCurrency(Math.Min(0, netProfit)),
                        FormatPercent((double)winners / periodTrades.Count),
                        FormatCurrency(netProfit / periodTrades.Count),
                        FormatCurrency(winners == 0 ? 0 : grossProfit / winners),
                        FormatCurrency(losers == 0 ? 0 : grossLoss / losers),
                        FormatCurrency(periodTrades.Max(t => GetDouble(GetProperty(t, "ProfitCurrency")))),
                        FormatCurrency(periodTrades.Min(t => GetDouble(GetProperty(t, "ProfitCurrency")))),
                        FormatNumber(periodTrades.Max(t => GetTradeMinutes(t))),
                        FormatCurrency(periodTrades.Average(t => GetDouble(GetProperty(t, "MaeCurrency")))),
                        FormatCurrency(periodTrades.Average(t => GetDouble(GetProperty(t, "MfeCurrency")))),
                        FormatCurrency(periodTrades.Average(t => GetDouble(GetProperty(t, "MfeCurrency")) - GetDouble(GetProperty(t, "ProfitCurrency")))),
                        FormatPercent((double)periodTrades.Count / trades.Count)
                    }.Select(EscapeCsv)));
                }
            }

            File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
        }

        private enum MetricFormat
        {
            Currency,
            Number,
            Percent,
            Integer,
            Minutes
        }

        private void AppendSettingRow(StringBuilder sb, string item, string value)
        {
            sb.AppendLine(string.Join(",", new[] { item, value, string.Empty }.Select(EscapeCsv)));
        }

        private void AppendSummaryRow(StringBuilder sb, string label, object all, object longPerf, object shortPerf, string propertyName, MetricFormat format)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                label,
                FormatMetricValue(GetProperty(all, propertyName), format),
                FormatMetricValue(GetProperty(longPerf, propertyName), format),
                FormatMetricValue(GetProperty(shortPerf, propertyName), format),
                string.Empty
            }.Select(EscapeCsv)));
        }

        private void AppendSingleSummaryRow(StringBuilder sb, string label, string value)
        {
            sb.AppendLine(string.Join(",", new[] { label, value, string.Empty, string.Empty, string.Empty }.Select(EscapeCsv)));
        }

        private void AppendBlankSummaryRow(StringBuilder sb)
        {
            sb.AppendLine(",,,,");
        }

        private string FormatMetricValue(object value, MetricFormat format)
        {
            if (format == MetricFormat.Currency) return FormatCurrency(value);
            if (format == MetricFormat.Percent) return FormatPercent(GetDouble(value));
            if (format == MetricFormat.Integer) return FormatInteger(value);
            if (format == MetricFormat.Minutes) return FormatNumber(value) + " min";
            return FormatNumber(value);
        }

        private List<KeyValuePair<string, string>> ParseParameters(string parameters)
        {
            var parsed = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(parameters)) return parsed;

            Match match = Regex.Match(parameters, @"^(.*?)\s*\((.*?)\)\s*$");
            if (!match.Success) return parsed;

            string[] values = match.Groups[1].Value.Split('/');
            string[] names = match.Groups[2].Value.Split(',');
            int count = Math.Min(values.Length, names.Length);
            for (int i = 0; i < count; i++)
            {
                string name = names[i].Trim();
                if (name.Length == 0) continue;
                parsed.Add(new KeyValuePair<string, string>(name, values[i].Trim()));
            }

            return parsed;
        }

        private List<object> GetTrades(object result)
        {
            object strategy = GetProperty(result, "ResultsStrategy");
            object performance = GetProperty(strategy, "SystemPerformance");
            IEnumerable trades = GetProperty(performance, "AllTrades") as IEnumerable;
            if (trades == null) return new List<object>();
            return trades.Cast<object>().OrderBy(GetTradeEntryTime).ToList();
        }

        private DateTime GetTradeEntryTime(object trade)
        {
            object time = GetProperty(GetProperty(trade, "Entry"), "Time");
            return time is DateTime dt ? dt : DateTime.MinValue;
        }

        private double GetTradeMinutes(object trade)
        {
            DateTime entry = GetTradeEntryTime(trade);
            object exitTime = GetProperty(GetProperty(trade, "Exit"), "Time");
            if (!(exitTime is DateTime exit) || entry == DateTime.MinValue) return 0;
            return Math.Round((exit - entry).TotalMinutes, 2);
        }

        private int GetTradeBars(object trade)
        {
            object entry = GetProperty(trade, "Entry");
            object exit = GetProperty(trade, "Exit");
            int entryBar = GetInt(GetProperty(entry, "BarIndex"));
            int exitBar = GetInt(GetProperty(exit, "BarIndex"));
            if (exitBar >= entryBar && entryBar >= 0) return exitBar - entryBar + 1;
            return 0;
        }

        private string FormatInstrumentName(object instrument)
        {
            string text = Convert.ToString(instrument, CultureInfo.InvariantCulture);
            const string suffix = " Globex";
            return text != null && text.EndsWith(suffix, StringComparison.Ordinal) ? text.Substring(0, text.Length - suffix.Length) : text;
        }

        private string FormatNtDateTime(object value)
        {
            if (value is DateTime dt) return dt.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private string FormatNtDateOnly(object value)
        {
            if (value is DateTime dt) return dt.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private string FormatNtTimeOnly(object value)
        {
            if (value is DateTime dt) return dt.ToString("h:mm tt", CultureInfo.InvariantCulture);
            return string.Empty;
        }

        private string FormatPrice(object value)
        {
            return GetDouble(value).ToString("0.00", CultureInfo.InvariantCulture);
        }

        private string FormatCurrency(object value)
        {
            double number = GetDouble(value);
            string formatted = "$" + Math.Abs(number).ToString("0.00", CultureInfo.InvariantCulture);
            return number < 0 ? "(" + formatted + ")" : formatted;
        }

        private string FormatPercent(double value)
        {
            return (value * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        private string FormatNumber(object value)
        {
            double number = GetDouble(value);
            return number.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private string FormatInteger(object value)
        {
            return GetInt(value).ToString(CultureInfo.InvariantCulture);
        }

        private double GetDouble(object value)
        {
            if (value == null) return 0;
            try
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return double.IsNaN(number) || double.IsInfinity(number) ? 0 : number;
            }
            catch
            {
                return 0;
            }
        }

        private int GetInt(object value)
        {
            if (value == null) return 0;
            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0;
            }
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
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;
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
