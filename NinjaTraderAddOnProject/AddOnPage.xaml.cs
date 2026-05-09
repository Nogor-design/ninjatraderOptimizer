using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using NinjaTrader.Core;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Custom.AddOns.Automation;

namespace NinjaTraderAddOnProject
{
    public partial class AddOnPage : NTTabPage
    {
        private bool isRunning = false;

        public AddOnPage()
        {
            InitializeComponent();
            SetupIPC();
        }

        private void btnBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtSourceFolder.Text = dialog.SelectedPath;
                RefreshTemplateList();
            }
        }

        private void btnBrowseDest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtDestFolder.Text = dialog.SelectedPath;
            }
        }

        private void RefreshTemplateList()
        {
            if (Directory.Exists(txtSourceFolder.Text))
            {
                lstTemplates.Items.Clear();
                var files = Directory.GetFiles(txtSourceFolder.Text, "*.xml");
                foreach (var file in files)
                {
                    lstTemplates.Items.Add(Path.GetFileName(file));
                }
            }
        }

        private async void btnStartBatch_Click(object sender, RoutedEventArgs e)
        {
            if (isRunning) return;
            isRunning = true;
            btnStartBatch.IsEnabled = false;
            btnStopBatch.IsEnabled = true;

            try
            {
                await RunBatch();
            }
            catch (Exception ex)
            {
                Log("Error: " + ex.Message);
            }
            finally
            {
                isRunning = false;
                btnStartBatch.IsEnabled = true;
                btnStopBatch.IsEnabled = false;
            }
        }

        private void btnStopBatch_Click(object sender, RoutedEventArgs e)
        {
            isRunning = false;
            Log("Stopping batch...");
        }

        private async Task RunBatch()
        {
            Log("RunBatch started.");
            List<string> templates = null;
            string sourcePath = null;
            object sa = null;

            NinjaTrader.Core.Globals.RandomDispatcher.Invoke(() => {
                Log("Initializing UI data on main thread...");
                templates = lstTemplates.Items.Cast<string>().ToList();
                sourcePath = txtSourceFolder.Text;
                pbProgress.Maximum = templates.Count;
                pbProgress.Value = 0;
                
                Log("Finding Strategy Analyzer window...");
                sa = StrategyAnalyzerAutomation.GetStrategyAnalyzers().FirstOrDefault();
            });

            if (sa == null)
            {
                Log("No Strategy Analyzer window found.");
                return;
            }

            if (templates == null || templates.Count == 0)
            {
                Log("No templates found in list.");
                return;
            }

            foreach (var templateName in templates)
            {
                if (!isRunning) break;

                Log("Processing: " + templateName);
                string path = Path.Combine(sourcePath, templateName);

                // Load XML on background thread
                XElement element = null;
                try {
                    element = XElement.Load(path);
                } catch (Exception ex) {
                    Log("File Load Error: " + ex.Message);
                    continue;
                }

                // Execute automation on main thread
                bool success = false;
                NinjaTrader.Core.Globals.RandomDispatcher.Invoke(() => {
                    try {
                        Log("Loading template into Strategy Analyzer...");
                        StrategyAnalyzerAutomation.LoadTemplate(sa, element);
                        
                        Log("Triggering Run...");
                        StrategyAnalyzerAutomation.Run(sa);
                        success = true;
                    } catch (Exception ex) {
                        Log("Automation Logic Error: " + ex.Message);
                    }
                });

                if (!success) continue;

                // Wait for completion (Simulated)
                Log("Waiting for run to complete (5s)...");
                await Task.Delay(5000); 

                NinjaTrader.Core.Globals.RandomDispatcher.Invoke(() => {
                    pbProgress.Value++;
                });
            }

            Log("Batch completed.");
        }

        private void Log(string message)
        {
            NinjaTrader.Core.Globals.RandomDispatcher.BeginInvoke(new Action(() => {
                outputBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                outputBox.ScrollToEnd();
            }));
        }

        // NTTabPage overrides
        protected override string GetHeaderPart(string name) => "Batch Optimizer";

        protected override void Save(XElement element)
        {
            element.Add(new XElement("SourceFolder", txtSourceFolder.Text));
            element.Add(new XElement("DestFolder", txtDestFolder.Text));
        }

        protected override void Restore(XElement element)
        {
            if (element == null) return;
            var source = element.Element("SourceFolder");
            if (source != null) txtSourceFolder.Text = source.Value;
            var dest = element.Element("DestFolder");
            if (dest != null) txtDestFolder.Text = dest.Value;
            RefreshTemplateList();
        }

        // IPC Mechanism for Agent-Driven Automation
        private FileSystemWatcher watcher;
        private void SetupIPC()
        {
            string tempDir = @"C:\temp";
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            
            watcher = new FileSystemWatcher(tempDir, "nt8_command.json");
            watcher.Changed += (s, e) => {
                Task.Run(async () => {
                    try {
                        string json = File.ReadAllText(e.FullPath);
                        if (json.Contains("RunBatch")) 
                        {
                            Dispatcher.Invoke(() => btnStartBatch_Click(null, null));
                        }
                    } catch { }
                });
            };
            watcher.EnableRaisingEvents = true;
        }
    }
}
