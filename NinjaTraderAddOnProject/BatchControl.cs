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
        private CheckBox chkRollingDateRange;
        private TextBox txtRollingDays;
        private CheckBox chkCloseTempTabs;
        private CheckBox chkOverwriteOutput;
        private StackPanel batchPanel;
        private Expander expander;
        private FileSystemWatcher commandWatcher;
        private static readonly object IpcCommandSync = new object();
        private static string acceptedRunBatchId;
        private static string acceptedRunBatchPayload;
        private bool cancelRequested;
        private object currentBatchTab;

        // Status heartbeat state written to C:\temp\nt8_status.json so the
        // TA Foundation /optimizer web UI can track progress.
        private const string StatusFilePath = @"C:\temp\nt8_status.json";
        private string currentRunId;
        private string currentDestFolder;
        private string currentTemplateName;
        private int currentCompletedCount;
        private int currentTotalCount;
        private string lastErrorMessage;
        private string requestedInstrument;
        private int requestedTimeoutSeconds = 600;

        private class BatchRunRecord
        {
            public string TemplateName { get; set; }
            public string Status { get; set; }
            public string Strategy { get; set; }
            public string Instrument { get; set; }
            public string TotalNetProfit { get; set; }
            public string Trades { get; set; }
            public string ProfitFactor { get; set; }
            public string MaxDrawdown { get; set; }
            public string BacktestStart { get; set; }
            public string BacktestEnd { get; set; }
            public DateTime RunStartTime { get; set; }
            public DateTime? RunEndTime { get; set; }
            public string OutputFolder { get; set; }
            public string Error { get; set; }
        }

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

            Grid rollingDateGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            rollingDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rollingDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rollingDateGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            chkRollingDateRange = new CheckBox
            {
                Content = "Use last",
                Foreground = Brushes.White,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            chkRollingDateRange.Checked += (s, e) => txtRollingDays.IsEnabled = true;
            chkRollingDateRange.Unchecked += (s, e) => txtRollingDays.IsEnabled = false;
            rollingDateGrid.Children.Add(chkRollingDateRange);

            txtRollingDays = new TextBox
            {
                Text = "30",
                Width = 42,
                IsEnabled = false,
                FontSize = 10,
                Margin = new Thickness(5, 0, 4, 0),
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                Foreground = Brushes.White,
                BorderBrush = Brushes.Gray,
                HorizontalContentAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(txtRollingDays, 1);
            rollingDateGrid.Children.Add(txtRollingDays);

            rollingDateGrid.Children.Add(new TextBlock
            {
                Text = "days",
                FontSize = 10,
                Foreground = Brushes.Silver,
                VerticalAlignment = VerticalAlignment.Center
            });
            Grid.SetColumn(rollingDateGrid.Children[2], 2);
            batchPanel.Children.Add(rollingDateGrid);

            chkCloseTempTabs = new CheckBox
            {
                Content = "Close temporary tabs after each run",
                IsChecked = true,
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0)
            };
            batchPanel.Children.Add(chkCloseTempTabs);

            chkOverwriteOutput = new CheckBox
            {
                Content = "Overwrite existing output",
                IsChecked = true,
                Foreground = Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            };
            batchPanel.Children.Add(chkOverwriteOutput);

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

            Loaded += (s, e) =>
            {
                RefreshForAnalyzerSelection();
                SetupIPC();
            };
            Unloaded += (s, e) => DisposeIPC();
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
                    Log(StrategyAnalyzerAutomation.CloseTab(saWindow, currentBatchTab));
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
                if (commandWatcher != null)
                    return;

                string tempDir = @"C:\temp";
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                commandWatcher = new FileSystemWatcher(tempDir, "nt8_command.json");
                commandWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName;
                commandWatcher.Changed += OnCommandFileChanged;
                commandWatcher.Created += OnCommandFileChanged;
                commandWatcher.Renamed += OnCommandFileChanged;
                commandWatcher.Deleted += OnCommandFileDeleted;
                commandWatcher.EnableRaisingEvents = true;
                Log("IPC watcher ready at C:\\temp\\nt8_command.json (cancel-on-delete enabled).");
            }
            catch (Exception ex)
            {
                Log("IPC setup error: " + ex.Message);
            }
        }

        private void DisposeIPC()
        {
            FileSystemWatcher watcher = commandWatcher;
            commandWatcher = null;
            if (watcher == null)
                return;
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnCommandFileChanged;
                watcher.Created -= OnCommandFileChanged;
                watcher.Renamed -= OnCommandFileChanged;
                watcher.Deleted -= OnCommandFileDeleted;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                Log("IPC watcher disposal error: " + ex.Message);
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
                    string action = ExtractJsonString(json, "action");

                    if (action != null && action.Equals("ObserveCompile", StringComparison.OrdinalIgnoreCase))
                    {
                        // ObserveCompile is handled by the global AddOn watcher
                        // so it works even before Strategy Analyzer is opened.
                        return;
                    }

                    // Explicit cancel payload from the web UI (the python
                    // side may write {"action":"Cancel",...} instead of
                    // deleting the file). Cancel only makes sense when a
                    // batch is in flight; otherwise treat as no-op.
                    if (action != null && action.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isRunning && !cancelRequested)
                            RequestCancelFromIpc("explicit Cancel action from IPC payload");
                        return;
                    }

                    if (json.IndexOf("RunBatch", StringComparison.OrdinalIgnoreCase) < 0)
                        return;

                    string sourceFolder = ExtractJsonString(json, "sourceFolder");
                    string destFolder = ExtractJsonString(json, "destFolder");
                    string runId = ExtractJsonString(json, "runId");
                    string instrument = ExtractJsonString(json, "instrument");
                    int timeoutSeconds = Math.Max(5, ExtractJsonInt(json, "timeoutSeconds", 600));
                    // Honor the IPC payload's request to close per-template tabs
                    // after each run. The Python web side always sets this to
                    // true so accumulated Strategy Analyzer tabs don't blow up
                    // NinjaTrader's memory across a multi-stage recipe. Manual
                    // GUI runs keep using the checkbox state unchanged.
                    bool? requestedCloseTempTabs = ExtractJsonBool(json, "closeTempTabs");

                    if (!TryAcceptRunBatch(runId, json))
                    {
                        Log("Ignored duplicate RunBatch IPC command"
                            + (string.IsNullOrWhiteSpace(runId) ? "." : " for " + runId + "."));
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(runId))
                        currentRunId = runId;
                    requestedInstrument = string.IsNullOrWhiteSpace(instrument) ? null : instrument;
                    requestedTimeoutSeconds = timeoutSeconds;

                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!string.IsNullOrWhiteSpace(sourceFolder))
                            txtSourceFolder.Text = sourceFolder;
                        if (!string.IsNullOrWhiteSpace(destFolder))
                            txtDestFolder.Text = destFolder;

                        if (requestedCloseTempTabs.HasValue && chkCloseTempTabs != null)
                            chkCloseTempTabs.IsChecked = requestedCloseTempTabs.Value;

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

        private bool TryAcceptRunBatch(string runId, string json)
        {
            lock (IpcCommandSync)
            {
                if (!string.IsNullOrWhiteSpace(runId))
                {
                    if (string.Equals(acceptedRunBatchId, runId, StringComparison.Ordinal))
                        return false;
                    acceptedRunBatchId = runId;
                    acceptedRunBatchPayload = json;
                    return true;
                }

                if (string.Equals(acceptedRunBatchPayload, json, StringComparison.Ordinal))
                    return false;
                acceptedRunBatchPayload = json;
                acceptedRunBatchId = null;
                return true;
            }
        }

        private void OnCommandFileDeleted(object sender, FileSystemEventArgs e)
        {
            // The web UI's cancel path unlinks C:\temp\nt8_command.json.
            // Treat the deletion as a cancel signal *only* if a batch is in
            // flight — a delete during idle is fine (e.g. operator cleanup
            // between sessions).
            _ = Task.Run(async () =>
            {
                // Tiny debounce: some editors atomically save by
                // delete+rename, which would otherwise fire a spurious
                // cancel between a Run dispatch and the next Created event.
                await Task.Delay(150);
                if (File.Exists(e.FullPath))
                    return;
                if (isRunning && !cancelRequested)
                    RequestCancelFromIpc("nt8_command.json deleted while batch running");
            });
        }

        private void RequestCancelFromIpc(string reason)
        {
            try
            {
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    cancelRequested = true;
                    if (btnCancel != null)
                        btnCancel.IsEnabled = false;
                    Log("Cancel requested via IPC: " + reason +
                        ". Stopping after the current template; the in-flight optimization will finish in NT.");
                }));
            }
            catch (Exception ex)
            {
                Log("RequestCancelFromIpc error: " + Unwrap(ex));
            }
        }

        private string ExtractJsonString(string json, string propertyName)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value.Replace("\\\\", "\\").Replace("\\\"", "\"") : null;
        }

        private int ExtractJsonInt(string json, string propertyName, int defaultValue)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*(?<value>-?\\d+)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return defaultValue;
            int value;
            return int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : defaultValue;
        }

        // Parse a JSON boolean field (true/false). Returns null when the field
        // is absent or unparseable, so callers can distinguish "not specified"
        // from "explicitly false".
        private bool? ExtractJsonBool(string json, string propertyName)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*(?<value>true|false)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return null;
            return match.Groups["value"].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // Status heartbeat: writes C:\temp\nt8_status.json for TA Foundation.
        // ------------------------------------------------------------------
        private void WriteStatus(string state)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"runId\":").Append(EncodeJsonString(currentRunId)).Append(",");
                sb.Append("\"state\":").Append(EncodeJsonString(state)).Append(",");
                sb.Append("\"currentTemplate\":").Append(EncodeJsonString(currentTemplateName)).Append(",");
                sb.Append("\"completed\":").Append(currentCompletedCount.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"total\":").Append(currentTotalCount.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"lastError\":").Append(EncodeJsonString(lastErrorMessage)).Append(",");
                sb.Append("\"timeoutSeconds\":").Append(requestedTimeoutSeconds.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"heartbeatUtc\":").Append(EncodeJsonString(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))).Append(",");
                sb.Append("\"outputRoot\":").Append(EncodeJsonString(currentDestFolder));
                sb.Append("}");

                string tempDir = Path.GetDirectoryName(StatusFilePath);
                if (!Directory.Exists(tempDir))
                    Directory.CreateDirectory(tempDir);

                // Atomic write: write a sibling temp file then replace.
                string tmpPath = StatusFilePath + ".tmp";
                File.WriteAllText(tmpPath, sb.ToString(), new UTF8Encoding(false));
                try
                {
                    if (File.Exists(StatusFilePath))
                        File.Replace(tmpPath, StatusFilePath, null);
                    else
                        File.Move(tmpPath, StatusFilePath);
                }
                catch (IOException)
                {
                    // Reader may have the file open; fall back to plain copy.
                    File.Copy(tmpPath, StatusFilePath, true);
                    try { File.Delete(tmpPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                // Status writing is best-effort; never break the batch over it.
                try { Log("WriteStatus error: " + ex.Message); } catch { }
            }
        }

        // ------------------------------------------------------------------
        // Compile observer: NinjaTrader auto-compiles strategies after Python
        // installs them into bin\Custom\Strategies. ObserveCompile does not
        // force compilation; it waits for the auto-compile to settle, checks
        // whether the strategy type is visible, and exports matching compiler
        // errors from recent NinjaTrader log/trace files when available.
        // ------------------------------------------------------------------
        private class CompileErrorRecord
        {
            public string File { get; set; }
            public int? Line { get; set; }
            public int? Column { get; set; }
            public string Code { get; set; }
            public string Message { get; set; }
            public string Raw { get; set; }
            public string Source { get; set; }
        }

        private async Task ObserveCompile(string json)
        {
            string runId = ExtractJsonString(json, "runId");
            string sourceFile = ExtractJsonString(json, "sourceFile");
            string strategyName = ExtractJsonString(json, "strategyName");
            string outputDir = ExtractJsonString(json, "outputDir");
            int timeoutSeconds = Math.Max(5, ExtractJsonInt(json, "timeoutSeconds", 120));
            int quietSeconds = Math.Max(1, ExtractJsonInt(json, "waitForQuietSeconds", 3));

            if (string.IsNullOrWhiteSpace(runId))
                runId = "compile_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(strategyName) && !string.IsNullOrWhiteSpace(sourceFile))
                strategyName = Path.GetFileNameWithoutExtension(sourceFile);
            if (string.IsNullOrWhiteSpace(outputDir))
                outputDir = @"C:\ta_foundation\nt_compile_loop\compiler_errors";

            Directory.CreateDirectory(outputDir);
            Log("ObserveCompile started for " + (strategyName ?? sourceFile ?? "(unknown strategy)") + ".");
            WriteCompileStatus(runId, "starting", strategyName, sourceFile, outputDir, false, 0, null, null, null);

            DateTime sourceWriteTime = File.Exists(sourceFile)
                ? File.GetLastWriteTime(sourceFile)
                : DateTime.Now;
            DateTime observeFrom = sourceWriteTime.AddMinutes(-2);
            DateTime deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            DateTime lastSourceWrite = sourceWriteTime;
            List<CompileErrorRecord> errors = new List<CompileErrorRecord>();

            while (DateTime.Now < deadline)
            {
                WriteCompileStatus(runId, "waiting_for_auto_compile", strategyName, sourceFile, outputDir, false, 0, null, null, null);

                if (File.Exists(sourceFile))
                {
                    DateTime currentWrite = File.GetLastWriteTime(sourceFile);
                    if (currentWrite > lastSourceWrite)
                        lastSourceWrite = currentWrite;
                }

                await Task.Delay(TimeSpan.FromSeconds(quietSeconds));
                WriteCompileStatus(runId, "observing", strategyName, sourceFile, outputDir, false, 0, null, null, null);
                errors = FindRecentCompileErrors(strategyName, sourceFile, observeFrom);
                if (errors.Count > 0)
                {
                    string csvPath = Path.Combine(outputDir, runId + "_errors.csv");
                    string textPath = Path.Combine(outputDir, runId + "_errors.txt");
                    string resultPath = Path.Combine(outputDir, runId + "_compile_result.json");
                    WriteCompileErrorsCsv(csvPath, errors);
                    WriteCompileErrorsText(textPath, errors);
                    WriteCompileResultJson(resultPath, runId, "failed", strategyName, sourceFile, outputDir, errors, csvPath, textPath, "Compiler errors observed.");
                    WriteCompileStatus(runId, "failed", strategyName, sourceFile, outputDir, false, errors.Count, csvPath, textPath, errors[0].Message);
                    Log("ObserveCompile failed for " + strategyName + " with " + errors.Count.ToString(CultureInfo.InvariantCulture) + " observed compiler errors.");
                    return;
                }

                if (IsStrategyTypeVisible(strategyName))
                {
                    string csvPath = Path.Combine(outputDir, runId + "_errors.csv");
                    string textPath = Path.Combine(outputDir, runId + "_errors.txt");
                    string resultPath = Path.Combine(outputDir, runId + "_compile_result.json");
                    WriteCompileErrorsCsv(csvPath, errors);
                    WriteCompileErrorsText(textPath, errors);
                    WriteCompileResultJson(resultPath, runId, "succeeded", strategyName, sourceFile, outputDir, errors, csvPath, textPath, null);
                    WriteCompileStatus(runId, "succeeded", strategyName, sourceFile, outputDir, true, 0, csvPath, textPath, null);
                    Log("ObserveCompile succeeded for " + strategyName + ".");
                    return;
                }

                await Task.Delay(1000);
            }

            string timeoutCsv = Path.Combine(outputDir, runId + "_errors.csv");
            string timeoutText = Path.Combine(outputDir, runId + "_errors.txt");
            string timeoutResult = Path.Combine(outputDir, runId + "_compile_result.json");
            string message = "No matching compiler errors were observed, but the strategy type is not visible in loaded NinjaTrader assemblies.";
            WriteCompileErrorsCsv(timeoutCsv, errors);
            WriteCompileErrorsText(timeoutText, errors);
            WriteCompileResultJson(timeoutResult, runId, "timed_out", strategyName, sourceFile, outputDir, errors, timeoutCsv, timeoutText, message);
            WriteCompileStatus(runId, "timed_out", strategyName, sourceFile, outputDir, false, errors.Count, timeoutCsv, timeoutText, message);
            Log("ObserveCompile timed out for " + strategyName + ": " + message);
        }

        private void WriteCompileStatus(
            string runId,
            string state,
            string strategyName,
            string sourceFile,
            string outputDir,
            bool compiled,
            int errorCount,
            string errorsCsv,
            string errorsText,
            string lastError)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("{");
                sb.Append("\"runId\":").Append(EncodeJsonString(runId)).Append(",");
                sb.Append("\"workerKind\":\"compile_observer\",");
                sb.Append("\"state\":").Append(EncodeJsonString(state)).Append(",");
                sb.Append("\"strategyName\":").Append(EncodeJsonString(strategyName)).Append(",");
                sb.Append("\"sourceFile\":").Append(EncodeJsonString(sourceFile)).Append(",");
                sb.Append("\"compiled\":").Append(compiled ? "true" : "false").Append(",");
                sb.Append("\"errorCount\":").Append(errorCount.ToString(CultureInfo.InvariantCulture)).Append(",");
                sb.Append("\"errorsCsv\":").Append(EncodeJsonString(errorsCsv)).Append(",");
                sb.Append("\"errorsText\":").Append(EncodeJsonString(errorsText)).Append(",");
                sb.Append("\"lastError\":").Append(EncodeJsonString(lastError)).Append(",");
                sb.Append("\"heartbeatUtc\":").Append(EncodeJsonString(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))).Append(",");
                sb.Append("\"outputRoot\":").Append(EncodeJsonString(outputDir));
                sb.Append("}");
                AtomicWriteText(StatusFilePath, sb.ToString());
            }
            catch (Exception ex)
            {
                try { Log("WriteCompileStatus error: " + ex.Message); } catch { }
            }
        }

        private void AtomicWriteText(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, text, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path))
                    File.Replace(tmpPath, path, null);
                else
                    File.Move(tmpPath, path);
            }
            catch (IOException)
            {
                File.Copy(tmpPath, path, true);
                try { File.Delete(tmpPath); } catch { }
            }
        }

        private bool IsStrategyTypeVisible(string strategyName)
        {
            if (string.IsNullOrWhiteSpace(strategyName))
                return false;
            string fullName = "NinjaTrader.NinjaScript.Strategies." + strategyName.Trim();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.GetType(fullName, false, true) != null)
                        return true;
                }
                catch { }
            }
            return false;
        }

        private List<CompileErrorRecord> FindRecentCompileErrors(string strategyName, string sourceFile, DateTime observeFrom)
        {
            List<CompileErrorRecord> records = new List<CompileErrorRecord>();
            string documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8");
            foreach (string folder in new[] { Path.Combine(documents, "log"), Path.Combine(documents, "trace") })
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (string path in Directory.GetFiles(folder, "*.txt").OrderByDescending(p => File.GetLastWriteTime(p)).Take(8))
                {
                    try
                    {
                        if (File.GetLastWriteTime(path) < observeFrom)
                            continue;
                        records.AddRange(ParseCompileErrorsFromText(path, ReadTail(path, 512 * 1024), strategyName, sourceFile));
                    }
                    catch { }
                }
            }

            return records
                .GroupBy(r => (r.File ?? "") + "|" + (r.Line.HasValue ? r.Line.Value.ToString(CultureInfo.InvariantCulture) : "") + "|" + (r.Column.HasValue ? r.Column.Value.ToString(CultureInfo.InvariantCulture) : "") + "|" + (r.Code ?? "") + "|" + (r.Message ?? ""))
                .Select(g => g.First())
                .ToList();
        }

        private string ReadTail(string path, int maxBytes)
        {
            FileInfo info = new FileInfo(path);
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long start = Math.Max(0, info.Length - maxBytes);
                stream.Seek(start, SeekOrigin.Begin);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private List<CompileErrorRecord> ParseCompileErrorsFromText(string sourcePath, string text, string strategyName, string sourceFile)
        {
            List<CompileErrorRecord> records = new List<CompileErrorRecord>();
            string fileName = string.IsNullOrWhiteSpace(sourceFile) ? "" : Path.GetFileName(sourceFile);
            string strategy = strategyName ?? "";
            Regex csRegex = new Regex(@"(?<file>[A-Za-z]:\\[^:\r\n]+?\.cs|[\w\-. ]+\.cs)?(?:\((?<line>\d+)\s*,\s*(?<column>\d+)\))?.*?(?<code>CS\d{4})\s*:\s*(?<message>.+)", RegexOptions.IgnoreCase);

            foreach (string rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                bool mentionsStrategy = (!string.IsNullOrWhiteSpace(strategy) && line.IndexOf(strategy, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrWhiteSpace(fileName) && line.IndexOf(fileName, StringComparison.OrdinalIgnoreCase) >= 0);
                bool looksLikeCompileError = Regex.IsMatch(line, @"CS\d{4}", RegexOptions.IgnoreCase)
                    || (mentionsStrategy && line.IndexOf("compile", StringComparison.OrdinalIgnoreCase) >= 0 && line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!looksLikeCompileError)
                    continue;

                Match match = csRegex.Match(line);
                if (match.Success)
                {
                    records.Add(new CompileErrorRecord
                    {
                        File = string.IsNullOrWhiteSpace(match.Groups["file"].Value) ? fileName : Path.GetFileName(match.Groups["file"].Value.Trim()),
                        Line = NullableInt(match.Groups["line"].Value),
                        Column = NullableInt(match.Groups["column"].Value),
                        Code = match.Groups["code"].Value.Trim(),
                        Message = match.Groups["message"].Value.Trim(),
                        Raw = line,
                        Source = sourcePath
                    });
                }
                else
                {
                    records.Add(new CompileErrorRecord
                    {
                        File = fileName,
                        Line = null,
                        Column = null,
                        Code = "",
                        Message = line,
                        Raw = line,
                        Source = sourcePath
                    });
                }
            }
            return records;
        }

        private int? NullableInt(string value)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return null;
        }

        private void WriteCompileErrorsCsv(string path, List<CompileErrorRecord> errors)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("NinjaScript File,Error,Code,Line,Column,Source,Raw");
            foreach (CompileErrorRecord error in errors)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    EscapeCsv(error.File),
                    EscapeCsv(error.Message),
                    EscapeCsv(error.Code),
                    EscapeCsv(error.Line.HasValue ? error.Line.Value.ToString(CultureInfo.InvariantCulture) : ""),
                    EscapeCsv(error.Column.HasValue ? error.Column.Value.ToString(CultureInfo.InvariantCulture) : ""),
                    EscapeCsv(error.Source),
                    EscapeCsv(error.Raw)
                }));
            }
            AtomicWriteText(path, sb.ToString());
        }

        private void WriteCompileErrorsText(string path, List<CompileErrorRecord> errors)
        {
            StringBuilder sb = new StringBuilder();
            foreach (CompileErrorRecord error in errors)
            {
                sb.Append(error.File ?? "");
                if (error.Line.HasValue || error.Column.HasValue)
                    sb.Append("(").Append(error.Line.HasValue ? error.Line.Value.ToString(CultureInfo.InvariantCulture) : "0").Append(",").Append(error.Column.HasValue ? error.Column.Value.ToString(CultureInfo.InvariantCulture) : "0").Append(")");
                if (!string.IsNullOrWhiteSpace(error.Code))
                    sb.Append(" ").Append(error.Code).Append(":");
                sb.Append(" ").Append(error.Message ?? error.Raw ?? "");
                sb.AppendLine();
            }
            AtomicWriteText(path, sb.ToString());
        }

        private void WriteCompileResultJson(
            string path,
            string runId,
            string state,
            string strategyName,
            string sourceFile,
            string outputDir,
            List<CompileErrorRecord> errors,
            string csvPath,
            string textPath,
            string lastError)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"schemaVersion\":1,");
            sb.Append("\"runId\":").Append(EncodeJsonString(runId)).Append(",");
            sb.Append("\"state\":").Append(EncodeJsonString(state)).Append(",");
            sb.Append("\"strategyName\":").Append(EncodeJsonString(strategyName)).Append(",");
            sb.Append("\"sourceFile\":").Append(EncodeJsonString(sourceFile)).Append(",");
            sb.Append("\"compiled\":").Append(state == "succeeded" ? "true" : "false").Append(",");
            sb.Append("\"errorCount\":").Append(errors.Count.ToString(CultureInfo.InvariantCulture)).Append(",");
            sb.Append("\"errorsCsv\":").Append(EncodeJsonString(csvPath)).Append(",");
            sb.Append("\"errorsText\":").Append(EncodeJsonString(textPath)).Append(",");
            sb.Append("\"lastError\":").Append(EncodeJsonString(lastError)).Append(",");
            sb.Append("\"outputRoot\":").Append(EncodeJsonString(outputDir)).Append(",");
            sb.Append("\"errors\":[");
            for (int i = 0; i < errors.Count; i++)
            {
                CompileErrorRecord error = errors[i];
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append("\"file\":").Append(EncodeJsonString(error.File)).Append(",");
                sb.Append("\"line\":").Append(error.Line.HasValue ? error.Line.Value.ToString(CultureInfo.InvariantCulture) : "null").Append(",");
                sb.Append("\"column\":").Append(error.Column.HasValue ? error.Column.Value.ToString(CultureInfo.InvariantCulture) : "null").Append(",");
                sb.Append("\"code\":").Append(EncodeJsonString(error.Code)).Append(",");
                sb.Append("\"message\":").Append(EncodeJsonString(error.Message)).Append(",");
                sb.Append("\"raw\":").Append(EncodeJsonString(error.Raw)).Append(",");
                sb.Append("\"source\":").Append(EncodeJsonString(error.Source));
                sb.Append("}");
            }
            sb.Append("],");
            sb.Append("\"heartbeatUtc\":").Append(EncodeJsonString(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
            sb.Append("}");
            AtomicWriteText(path, sb.ToString());
        }

        private static string EncodeJsonString(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private async Task RunBatch()
        {
            string sourceFolder = string.Empty;
            string destFolder = string.Empty;
            bool useRollingDateRange = false;
            bool closeTempTabs = true;
            bool overwriteOutput = true;
            int rollingDays = 0;
            int timeoutSeconds = 600;
            Dispatcher.Invoke(() =>
            {
                sourceFolder = txtSourceFolder.Text;
                destFolder = txtDestFolder.Text;
                useRollingDateRange = chkRollingDateRange.IsChecked == true;
                closeTempTabs = chkCloseTempTabs.IsChecked == true;
                overwriteOutput = chkOverwriteOutput.IsChecked == true;
                timeoutSeconds = Math.Max(5, requestedTimeoutSeconds);
                if (useRollingDateRange)
                    int.TryParse(txtRollingDays.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out rollingDays);
            });

            if (!Directory.Exists(sourceFolder)) { Log("Invalid source folder."); return; }
            if (string.IsNullOrWhiteSpace(destFolder)) { Log("Invalid destination folder."); return; }
            if (useRollingDateRange && rollingDays <= 0) { Log("Invalid rolling backtest days."); return; }

            try
            {
                Directory.CreateDirectory(destFolder);
            }
            catch (Exception ex)
            {
                Log("Could not create destination folder: " + Unwrap(ex));
                return;
            }

            var templates = Directory.GetFiles(sourceFolder, "*.xml", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            if (templates.Count == 0) { Log("No .xml templates found in source folder or subfolders."); return; }

            // Was the contract explicitly supplied via the IPC payload?
            // If yes, we use it to override every template's instrument
            // (operators sometimes want to run the same templates against
            // a different instrument). If no, the template's own
            // <InstrumentOrInstrumentList> wins — clobbering it with the
            // currently-selected tab's instrument has caused silent
            // contract drops where e.g. an `NQ 06-26` template was loaded
            // as `NQ`, producing zero trades.
            bool instrumentExplicit = !string.IsNullOrWhiteSpace(requestedInstrument);
            string selectedInstrument = instrumentExplicit
                ? requestedInstrument
                : GetSelectedInstrumentForBatch();
            if (string.IsNullOrWhiteSpace(selectedInstrument))
            {
                ShowBatchWarning("Please select an instrument in the Strategy Analyzer before starting the batch.");
                Log("Batch not started: select an instrument in the Strategy Analyzer first.");
                return;
            }
            Log("Batch instrument source: " + (instrumentExplicit ? "IPC payload" : "current tab")
                + " (" + selectedInstrument + ")."
                + (instrumentExplicit ? " Templates will be forced to this instrument." : " Templates' <InstrumentOrInstrumentList> values will be preserved."));

            var summaryRecords = new List<BatchRunRecord>();
            string batchSummaryPath = Path.Combine(destFolder, "BatchRunSummary.csv");
            WriteBatchRunSummaryCsv(batchSummaryPath, summaryRecords);
            DateTime rollingTo = DateTime.Today;
            DateTime rollingFrom = rollingTo.AddDays(-rollingDays);

            Log("Starting batch with " + templates.Count + " templates.");
            Log("Per-template timeout: " + timeoutSeconds.ToString(CultureInfo.InvariantCulture) + " seconds.");
            if (useRollingDateRange)
                Log("Rolling date range enabled: " + rollingFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " to " + rollingTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".");

            currentDestFolder = destFolder;
            currentTotalCount = templates.Count;
            currentCompletedCount = 0;
            currentTemplateName = null;
            lastErrorMessage = null;
            WriteStatus("starting");

            // Deferred close of the previously-completed tab. Closing each
            // tab immediately after its export raced NT's post-run UI
            // dispatcher chain (StrategyAnalyzerViewModel.RunEntryDetails
            // -> SetTabUiEnabled), which threw NullReferenceException on the
            // disposed tab and panicked NT. The tab is held until the next
            // template's run is in flight, by which point NT has drained
            // those callbacks.
            object pendingCloseTab = null;

            foreach (string path in templates)
            {
                if (!isRunning || cancelRequested) break;

                string templateName = Path.GetFileNameWithoutExtension(path);
                currentTemplateName = templateName;
                WriteStatus("running");
                string outputName = GetSafeOutputName(templateName, summaryRecords.Count + 1, destFolder);
                XElement element = null;
                int resultCountBeforeRun = 0;
                string originalInstrument = string.Empty;
                object batchTab = null;
                var record = new BatchRunRecord
                {
                    TemplateName = templateName,
                    Status = "Started",
                    RunStartTime = DateTime.Now,
                    OutputFolder = Path.Combine(destFolder, outputName)
                };
                summaryRecords.Add(record);
                WriteBatchRunSummaryCsv(batchSummaryPath, summaryRecords);

                try
                {
                    element = XElement.Load(path);
                }
                catch (Exception ex)
                {
                    record.Status = "TemplateLoadError";
                    record.Error = Unwrap(ex);
                    record.RunEndTime = DateTime.Now;
                    Log("Template load error: " + record.Error);
                    lastErrorMessage = "TemplateLoadError: " + record.Error;
                    WriteStatus("running");
                    WriteBatchRunSummaryCsv(batchSummaryPath, summaryRecords);
                    continue;
                }

                if (useRollingDateRange)
                    ApplyRollingBacktestDates(element, rollingFrom, rollingTo);
                PopulateBatchRunRecordBacktestDates(record, element);

                Log("Processing in new tab: " + templateName);
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        originalInstrument = selectedInstrument;
                        batchTab = StrategyAnalyzerAutomation.AddNewTab(saWindow);
                        currentBatchTab = batchTab;
                        StrategyAnalyzerAutomation.LoadTemplate(saWindow, element);
                        // Only override the loaded template's instrument when
                        // the IPC payload explicitly requested one. Otherwise
                        // trust whatever <InstrumentOrInstrumentList> the
                        // template carried (LoadTemplate already applied it).
                        if (instrumentExplicit)
                        {
                            StrategyAnalyzerAutomation.SetSelectedInstrumentOrInstrumentList(saWindow, originalInstrument);
                        }
                        Log("Loaded template state: " + StrategyAnalyzerAutomation.GetSelectedTemplateDebug(saWindow));
                        resultCountBeforeRun = StrategyAnalyzerAutomation.GetSelectedResultCount(saWindow);
                    }
                    catch (Exception ex)
                    {
                        record.Status = "SetupError";
                        record.Error = Unwrap(ex);
                        record.RunEndTime = DateTime.Now;
                        Log("Setup error: " + record.Error);
                    }
                });

                if (record.Status == "SetupError")
                {
                    lastErrorMessage = "SetupError: " + record.Error;
                    WriteStatus("running");
                    CloseBatchTabIfEnabled(batchTab, closeTempTabs);
                    if (pendingCloseTab != null)
                    {
                        CloseBatchTabIfEnabled(pendingCloseTab, closeTempTabs);
                        pendingCloseTab = null;
                    }
                    WriteBatchRunSummaryCsv(batchSummaryPath, summaryRecords);
                    continue;
                }

                // Allow the UI to settle after the template load, then dispatch the
                // run. Heavier strategies (e.g. PantheonMaster, 60+ optimization
                // parameters) intermittently take longer than a single fixed delay
                // to make the freshly-loaded Optimize tab the active, *runnable* tab.
                // A one-shot RunCommand.CanExecute check then loses that race and the
                // template is wrongly skipped as "Run command was not executable"
                // (observed wedging batches at ~50-75 templates). Poll for runnability
                // across several short, UI-yielding attempts instead of a single shot.
                await Task.Delay(1000);
                if (cancelRequested)
                {
                    record.Status = "Cancelled";
                    record.RunEndTime = DateTime.Now;
                    WriteBatchRunSummaryCsv(batchSummaryPath, summaryRecords);
                    break;
                }

                const int maxRunAttempts = 12; // ~6s of extra settle budget beyond the 1s above
                bool dispatched = false;
                Exception runException = null;
                for (int attempt = 1; attempt <= maxRunAttempts; attempt++)
                {
                    if (cancelRequested)
                        break;
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            dispatched = StrategyAnalyzerAutomation.Run(saWindow);
                        }
                        catch (Exception ex)
                        {
                            runException = ex;
                        }
                    });
                    if (dispatched || runException != null)
                        break;
                    if (attempt < maxRunAttempts)
                        await Task.Delay(500); // yield the UI thread so the new tab can become runnable
                }

                if (cancelRequested)
                {
                    record.Status = "Cancelled";
                    record.RunEndTime = DateTime.Now;
                    WriteBatchRunSummaryCsv(batchSummaryPath, summaryRecords);
                    break;
                }

                if (dispatched)
                {
                    record.Status = "Running";
                }
                else
                {
                    record.Status = "RunError";
                    record.Error = runException != null
                        ? Unwrap(runException)
                        : "Strategy Analyzer Run command was not executable after "
                            + maxRunAttempts.ToString(CultureInfo.InvariantCulture) + " attempts.";
                    record.RunEndTime = DateTime.Now;
                    Log("Run error: " + record.Error);
                }

                if (record.Status == "RunError")
                {
                    lastErrorMessage = "RunError: " + record.Error;
                    WriteStatus("running");
                    CloseBatchTabIfEnabled(batchTab, closeTempTabs);
                    if (pendingCloseTab != null)
                    {
                        CloseBatchTabIfEnabled(pendingCloseTab, closeTempTabs);
                        pendingCloseTab = null;
                    }
                    WriteBatchRunSummaryCsv(batchSummaryPath, summaryRecords);
                    continue;
                }

                // New run is in flight; safe to close the previously-completed
                // tab now. NT's post-run callback chain on that tab has had
                // the template-load settle delay plus a Run dispatch to drain.
                if (pendingCloseTab != null)
                {
                    CloseBatchTabIfEnabled(pendingCloseTab, closeTempTabs);
                    pendingCloseTab = null;
                }

                Log("Running backtest...");
                bool completed = await WaitForRunCompletion(resultCountBeforeRun, TimeSpan.FromSeconds(timeoutSeconds));
                if (cancelRequested)
                {
                    record.Status = "Cancelled";
                    record.RunEndTime = DateTime.Now;
                    // Defer close to the post-loop flush; closing here races
                    // NT's in-flight run callbacks.
                    pendingCloseTab = batchTab;
                    WriteBatchRunSummaryCsv(batchSummaryPath, summaryRecords);
                    break;
                }

                if (!completed)
                {
                    record.Status = "TimedOut";
                    record.Error = "Timed out after " + timeoutSeconds.ToString(CultureInfo.InvariantCulture) + " seconds waiting for Strategy Analyzer results.";
                    lastErrorMessage = record.Error;
                    Log("Timed out waiting for results; exporting whatever is available.");
                }

                bool exported = ExportResults(templateName, outputName, destFolder, record, overwriteOutput);
                if (exported && record.Status != "TimedOut")
                    record.Status = "Completed";
                else if (!exported && record.Status != "TimedOut")
                {
                    record.Status = "ExportError";
                    lastErrorMessage = "ExportError on " + templateName;
                }

                record.RunEndTime = DateTime.Now;
                WriteBatchRunSummaryCsv(batchSummaryPath, summaryRecords);
                // Defer the close to the next iteration's mid-run dispatch
                // point so NT's RunEntryDetails -> SetTabUiEnabled callback
                // chain finishes on this tab before it gets disposed.
                pendingCloseTab = batchTab;

                currentCompletedCount++;
                WriteStatus("running");
            }

            // Flush the last deferred close with a delay so the final
            // template's post-run callbacks have time to drain. Cancel
            // forces close regardless of closeTempTabs to match the original
            // immediate-close semantics on the cancel path.
            if (pendingCloseTab != null)
            {
                if (closeTempTabs || cancelRequested)
                {
                    await Task.Delay(2000);
                    CloseBatchTab(pendingCloseTab);
                }
                pendingCloseTab = null;
            }

            currentTemplateName = null;
            if (cancelRequested)
            {
                Log("Batch cancelled.");
                WriteStatus("cancelled");
            }
            else
            {
                Log("Batch completed.");
                WriteStatus("finished");
            }
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
                if (elapsedSeconds % 2 == 0)
                    WriteStatus("running");
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
                    string diag = StrategyAnalyzerAutomation.CloseTab(saWindow, tab);
                    Log(diag);
                    if (ReferenceEquals(currentBatchTab, tab))
                        currentBatchTab = null;
                }
                catch (Exception ex)
                {
                    Log("Close-tab error: " + Unwrap(ex));
                }
            });
        }

        private void CloseBatchTabIfEnabled(object tab, bool closeTempTabs)
        {
            if (closeTempTabs)
                CloseBatchTab(tab);
        }

        private string GetSelectedInstrumentForBatch()
        {
            string instrument = string.Empty;
            Dispatcher.Invoke(() =>
            {
                try
                {
                    instrument = StrategyAnalyzerAutomation.GetSelectedInstrumentOrInstrumentList(saWindow);
                }
                catch (Exception ex)
                {
                    Log("Instrument check error: " + Unwrap(ex));
                }
            });
            return instrument;
        }

        private void ShowBatchWarning(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                MessageBox.Show(Window.GetWindow(this), message, "Batch Strategy Analyzer", MessageBoxButton.OK, MessageBoxImage.Warning);
            }));
        }

        private bool ExportResults(string templateName, string outputName, string destFolder, BatchRunRecord record, bool overwriteOutput)
        {
            bool exported = false;
            Dispatcher.Invoke(() =>
            {
                try
                {
                    string subFolder = Path.Combine(destFolder, outputName);
                    if (!Directory.Exists(subFolder))
                        Directory.CreateDirectory(subFolder);

                    List<object> results = StrategyAnalyzerAutomation.GetSelectedResults(saWindow).ToList();
                    PopulateBatchRunRecord(record, results, subFolder);

                    string settingsPath = Path.Combine(subFolder, "Settings.csv");
                    string summaryPath = Path.Combine(subFolder, "Summary.csv");
                    string tradesPath = Path.Combine(subFolder, "Trades.csv");
                    string ordersPath = Path.Combine(subFolder, "Orders.csv");
                    string executionsPath = Path.Combine(subFolder, "Executions.csv");
                    string analysisPath = Path.Combine(subFolder, "Analysis.csv");
                    string optimizationPath = Path.Combine(subFolder, outputName + "_Optimization.csv");
                    var outputFiles = new[] { settingsPath, summaryPath, tradesPath, ordersPath, executionsPath, analysisPath, optimizationPath };

                    if (!overwriteOutput)
                    {
                        List<string> existingOutputs = outputFiles.Where(File.Exists).Select(Path.GetFileName).ToList();
                        if (existingOutputs.Count > 0)
                        {
                            string message = "Output exists and overwrite is disabled: " + string.Join(", ", existingOutputs);
                            if (record != null)
                                record.Error = message;
                            Log(message);
                            return;
                        }
                    }
                    else
                    {
                        foreach (string existingFile in Directory.GetFiles(subFolder, "*.csv"))
                            File.Delete(existingFile);
                    }

                    WriteSettingsCsv(settingsPath, results);
                    WriteSummaryCsv(summaryPath, results);
                    WriteTradesCsv(tradesPath, results);
                    WriteOrdersCsv(ordersPath, results);
                    WriteExecutionsCsv(executionsPath, results);
                    WriteAnalysisCsv(analysisPath, results);
                    WriteOptimizationCsv(optimizationPath, results);

                    Log("Exported 7 internal CSV files to " + subFolder);
                    exported = true;
                }
                catch (Exception ex)
                {
                    string message = Unwrap(ex);
                    if (record != null)
                        record.Error = message;
                    Log("Export logic error: " + message);
                }
            });
            return exported;
        }

        private void PopulateBatchRunRecord(BatchRunRecord record, List<object> results, string outputFolder)
        {
            if (record == null)
                return;

            record.OutputFolder = outputFolder;
            object result = results.FirstOrDefault();
            if (result == null)
                return;

            object summary = GetProperty(result, "SummaryPerformancesCurrency") ?? GetProperty(result, "SummaryPerformances");
            object all = GetProperty(summary, "All");

            record.Strategy = Convert.ToString(GetProperty(result, "StrategyName"), CultureInfo.InvariantCulture);
            record.Instrument = Convert.ToString(GetProperty(result, "Instrument"), CultureInfo.InvariantCulture);
            record.TotalNetProfit = FormatCurrency(GetProperty(all, "TotalNetProfit"));
            record.Trades = FormatInteger(GetProperty(all, "TotalNumTrades"));
            record.ProfitFactor = FormatNumber(GetProperty(all, "ProfitFactor"));
            record.MaxDrawdown = FormatCurrency(GetProperty(all, "MaxDrawdown"));
            record.BacktestStart = FormatSummaryDate(GetProperty(result, "From"));
            record.BacktestEnd = FormatSummaryDate(GetProperty(result, "To"));
        }

        private void WriteBatchRunSummaryCsv(string fileName, List<BatchRunRecord> records)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Template,Status,Strategy,Instrument,Backtest start,Backtest end,Total net profit,Trades,Profit factor,Max drawdown,Run start time,Run end time,Output folder,Error");

            foreach (BatchRunRecord record in records)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    record.TemplateName,
                    record.Status,
                    record.Strategy,
                    record.Instrument,
                    record.BacktestStart,
                    record.BacktestEnd,
                    record.TotalNetProfit,
                    record.Trades,
                    record.ProfitFactor,
                    record.MaxDrawdown,
                    FormatSummaryDateTime(record.RunStartTime),
                    record.RunEndTime.HasValue ? FormatSummaryDateTime(record.RunEndTime.Value) : string.Empty,
                    record.OutputFolder,
                    record.Error
                }.Select(EscapeCsv)));
            }

            File.WriteAllText(fileName, sb.ToString(), Encoding.UTF8);
        }

        private void PopulateBatchRunRecordBacktestDates(BatchRunRecord record, XElement templateElement)
        {
            XElement strategyElement = templateElement?.Element("Strategy")?.Elements().FirstOrDefault();
            if (record == null || strategyElement == null)
                return;

            record.BacktestStart = FormatSummaryDate(ParseTemplateDate(strategyElement.Elements().FirstOrDefault(element => element.Name.LocalName == "From")?.Value));
            record.BacktestEnd = FormatSummaryDate(ParseTemplateDate(strategyElement.Elements().FirstOrDefault(element => element.Name.LocalName == "To")?.Value));
        }

        private string GetSafeOutputName(string templateName, int index, string destFolder)
        {
            string safe = Regex.Replace(templateName ?? "Template", "[^A-Za-z0-9_.-]+", "_").Trim('_');
            string hash = Math.Abs((safe ?? string.Empty).GetHashCode()).ToString("x", CultureInfo.InvariantCulture);
            int pathBudget = 240
                - (destFolder ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length
                - "_Optimization.csv".Length
                - 2; // subfolder separator plus file separator; output name appears in both path segments.
            int maxNameLength = Math.Max(16, Math.Min(72, pathBudget / 2));

            if (safe.Length > maxNameLength)
            {
                string suffix = "_" + index.ToString("0000", CultureInfo.InvariantCulture) + "_" + hash;
                int prefixLength = Math.Max(1, maxNameLength - suffix.Length);
                safe = safe.Substring(0, Math.Min(prefixLength, safe.Length)).TrimEnd('_', '-', '.') + suffix;
                if (safe.Length > maxNameLength)
                    safe = safe.Substring(safe.Length - maxNameLength);
            }

            return string.IsNullOrWhiteSpace(safe) ? "Template_" + index.ToString("0000", CultureInfo.InvariantCulture) : safe;
        }

        private void ApplyRollingBacktestDates(XElement templateElement, DateTime from, DateTime to)
        {
            XElement strategyElement = templateElement?.Element("Strategy")?.Elements().FirstOrDefault();
            if (strategyElement == null)
                return;

            SetSimpleChildValue(strategyElement, "From", FormatTemplateDate(from));
            SetSimpleChildValue(strategyElement, "To", FormatTemplateDate(to));
        }

        private void SetSimpleChildValue(XElement parent, string localName, string value)
        {
            XElement child = parent.Elements().FirstOrDefault(element => element.Name.LocalName == localName);
            if (child == null)
            {
                child = new XElement(parent.Name.Namespace + localName);
                parent.Add(child);
            }

            child.Value = value;
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

        private void WriteOptimizationCsv(string fileName, List<object> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Instrument,Performance,Parameters,Total net profit,Gross profit,Gross loss,Profit factor,Max. drawdown,Total # of trades,Percent profitable,");

            bool wroteInstrument = false;
            foreach (object result in results)
            {
                object summary = GetProperty(result, "SummaryPerformancesCurrency") ?? GetProperty(result, "SummaryPerformances");
                object all = GetProperty(summary, "All");
                string instrument = wroteInstrument ? string.Empty : FormatInstrumentName(GetProperty(result, "Instrument"));
                wroteInstrument = true;

                sb.AppendLine(string.Join(",", new[]
                {
                    instrument,
                    FormatOptimizationNumber(GetOptimizationPerformance(result, all)),
                    FormatOptimizationParameters(GetProperty(result, "ParametersString")),
                    FormatOptimizationNumber(GetProperty(all, "TotalNetProfit")),
                    FormatOptimizationNumber(GetProperty(all, "GrossProfit")),
                    FormatOptimizationNumber(GetProperty(all, "GrossLoss")),
                    FormatOptimizationNumber(GetProperty(all, "ProfitFactor")),
                    FormatOptimizationNumber(GetProperty(all, "MaxDrawdown")),
                    FormatInteger(GetProperty(all, "TotalNumTrades")),
                    FormatOptimizationPercent(GetProperty(all, "PercentProfitable")),
                    string.Empty
                }.Select(EscapeCsv)));
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

        private string FormatSummaryDateTime(DateTime value)
        {
            return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private string FormatSummaryDate(object value)
        {
            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private DateTime? ParseTemplateDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                return date;

            return null;
        }

        private string FormatTemplateDate(DateTime value)
        {
            return value.Date.ToString("yyyy-MM-dd'T'00:00:00", CultureInfo.InvariantCulture);
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

        private string FormatOptimizationNumber(object value)
        {
            return GetDouble(value).ToString("0.###############", CultureInfo.InvariantCulture);
        }

        private string FormatOptimizationPercent(object value)
        {
            double number = GetDouble(value);
            if (number > 0 && number <= 1)
                number *= 100;
            return number.ToString("0.###############", CultureInfo.InvariantCulture) + "%";
        }

        private string FormatOptimizationParameters(object value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            int close = text.LastIndexOf(')');
            int open = FindMatchingOpenParen(text, close);
            if (open < 0 || close <= open)
                return text;

            string values = text.Substring(0, open).TrimEnd();
            string names = text.Substring(open + 1, close - open - 1)
                .Replace(",", " ")
                .Trim();
            names = Regex.Replace(names, "_\\s+\\(", "_(");
            names = Regex.Replace(names, "\\s+", " ");
            return values + " (" + names + " )";
        }

        private int FindMatchingOpenParen(string text, int closeIndex)
        {
            if (string.IsNullOrEmpty(text) || closeIndex < 0 || closeIndex >= text.Length)
                return -1;

            int depth = 1;
            for (int i = closeIndex - 1; i >= 0; i--)
            {
                if (text[i] == ')')
                    depth++;
                else if (text[i] == '(')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        private object GetOptimizationPerformance(object result, object all)
        {
            object value = GetProperty(result, "Performance")
                ?? GetProperty(result, "FitnessValue")
                ?? GetProperty(result, "OptimizationFitnessValue")
                ?? GetProperty(result, "Value");

            return value ?? GetProperty(all, "ProfitFactor");
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

        private static readonly object logFileLock = new object();

        // Where the readable batch log is written so it can be inspected outside
        // NinjaTrader (the NinjaScript Output window is not persisted to disk).
        // Prefer the current run's dest folder; fall back to C:\temp.
        private string LogFilePath()
        {
            try
            {
                string dir = (!string.IsNullOrWhiteSpace(currentDestFolder) && Directory.Exists(currentDestFolder))
                    ? currentDestFolder
                    : @"C:\temp";
                return Path.Combine(dir, "nt8_addon_batch.log");
            }
            catch
            {
                return @"C:\temp\nt8_addon_batch.log";
            }
        }

        private void Log(string msg)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "] " + msg;

            // File sink first so diagnostics survive even if the UI thread is busy
            // or NinjaTrader crashes. Best-effort; never throw from logging.
            try
            {
                string path = LogFilePath();
                lock (logFileLock)
                {
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch { }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (outputBox == null)
                    return;

                outputBox.AppendText(line + Environment.NewLine);
                outputBox.ScrollToEnd();
                NinjaTrader.Code.Output.Process("BatchStrategyOptimizer: " + msg, NinjaTrader.NinjaScript.PrintTo.OutputTab1);
            }));
        }
    }
}
