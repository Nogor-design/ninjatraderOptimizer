using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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

        public BatchControl(object sa)
        {
            saWindow = sa;
            InitializeUI();
            SubscribeToStrategyChanges();
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
            txtModeStatus.Text = "Analyzer type: " + (string.IsNullOrEmpty(backtestType) ? "unknown" : backtestType);
            btnStart.Content = backtestType == "Optimize" || backtestType == "MultiObjective" ? "RUN BATCH OPTIMIZATION" : "RUN BATCH BACKTEST";

            if (!string.IsNullOrEmpty(strategyName))
            {
                string ntTemplates = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "templates", "Strategy", strategyName);
                if (Directory.Exists(ntTemplates) && !string.Equals(txtSourceFolder.Text, ntTemplates, StringComparison.OrdinalIgnoreCase))
                {
                    txtSourceFolder.Text = ntTemplates;
                    Log("Loaded template folder for " + strategyName + " (" + backtestType + ").");
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

                Log("Processing in new tab: " + templateName);
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        StrategyAnalyzerAutomation.AddNewTab(saWindow);
                        StrategyAnalyzerAutomation.LoadTemplate(saWindow, element);
                        resultCountBeforeRun = StrategyAnalyzerAutomation.GetSelectedResultCount(saWindow);
                    }
                    catch (Exception ex)
                    {
                        Log("Setup error: " + Unwrap(ex));
                    }
                });

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

                Log("Running...");
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
            while (DateTime.Now < deadline)
            {
                int resultCount = 0;
                bool busy = false;
                Dispatcher.Invoke(() =>
                {
                    resultCount = StrategyAnalyzerAutomation.GetSelectedResultCount(saWindow);
                    busy = StrategyAnalyzerAutomation.IsSelectedTabBusy(saWindow);
                });

                if (resultCount > resultCountBeforeRun && !busy)
                    return true;

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
                    string fileName = Path.Combine(subFolder, "Summary.csv");
                    WriteSummaryCsv(fileName, results);
                    Log("Exported " + results.Count + " result rows to " + fileName);
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

        private object GetProperty(object instance, string propertyName)
        {
            return instance?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
        }

        private string FormatMetric(object value)
        {
            if (value == null) return string.Empty;
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
            }));
        }
    }
}
