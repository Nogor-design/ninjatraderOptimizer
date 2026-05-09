using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;

namespace NinjaTrader.Custom.AddOns.Automation
{
    public class StrategyAnalyzerAutomation
    {
        private static Assembly guiAssembly = Assembly.Load("NinjaTrader.Gui");
        private static Type saType = guiAssembly.GetType("NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzer");
        private static Type saVmType = guiAssembly.GetType("NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzerViewModel");
        private static Type saTabType = guiAssembly.GetType("NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzerTabControl");
        private static Type tradePerformanceDisplayType = guiAssembly.GetType("NinjaTrader.Gui.TradePerformance.TradePerformanceDisplayType");
        private static Type ntGridType = guiAssembly.GetType("NinjaTrader.Gui.Tools.NTGrid");

        public static IEnumerable<object> GetStrategyAnalyzers()
        {
            List<object> analyzers = new List<object>();
            Core.Globals.RandomDispatcher.Invoke(() => {
                foreach (Window w in Application.Current.Windows)
                {
                    if (saType.IsInstanceOfType(w))
                        analyzers.Add(w);
                }
            });
            return analyzers;
        }

        public static string GetSelectedStrategyName(object saWindow)
        {
            var props = GetSelectedTabProperties(saWindow);
            return props?.GetType().GetProperty("Strategy", BindingFlags.Public | BindingFlags.Instance)?.GetValue(props) as string;
        }

        public static string GetSelectedBacktestType(object saWindow)
        {
            var props = GetSelectedTabProperties(saWindow);
            object value = props?.GetType().GetProperty("BacktestType", BindingFlags.Public | BindingFlags.Instance)?.GetValue(props);
            return value?.ToString();
        }

        public static object GetViewModel(object saWindow)
        {
            if (saWindow == null || !saType.IsInstanceOfType(saWindow)) return null;
            return saType.GetProperty("ViewModel", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saWindow);
        }

        public static object GetSelectedTab(object saWindow)
        {
            object viewModel = GetViewModel(saWindow);
            if (viewModel == null) return null;
            return saVmType.GetProperty("SelectedTab", BindingFlags.Public | BindingFlags.Instance)?.GetValue(viewModel);
        }

        public static object GetSelectedTabProperties(object saWindow)
        {
            object selectedTab = GetSelectedTab(saWindow);
            return selectedTab?.GetType().GetProperty("Properties", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
        }

        public static object AddNewTab(object saWindow)
        {
            if (saWindow == null || !saType.IsInstanceOfType(saWindow)) return null;

            object newTab = null;
            Core.Globals.RandomDispatcher.Invoke(() =>
            {
                object viewModel = GetViewModel(saWindow);
                if (viewModel == null || saTabType == null)
                    return;

                newTab = Activator.CreateInstance(saTabType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { viewModel }, null);
                saType.GetMethod("AddNewTab", BindingFlags.Public | BindingFlags.Instance)?.Invoke(saWindow, new[] { newTab });
                saVmType.GetProperty("SelectedTab", BindingFlags.Public | BindingFlags.Instance)?.SetValue(viewModel, newTab);
            });

            return newTab ?? GetSelectedTab(saWindow);
        }

        public static int GetSelectedResultCount(object saWindow)
        {
            object selectedTab = GetSelectedTab(saWindow);
            object results = selectedTab?.GetType().GetProperty("Results", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
            return (results as ICollection)?.Count ?? 0;
        }

        public static bool IsSelectedTabBusy(object saWindow)
        {
            object selectedTab = GetSelectedTab(saWindow);
            if (selectedTab == null) return false;

            var progressVisible = selectedTab.GetType().GetProperty("IsProgressVisible", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
            var progressClearWaiting = selectedTab.GetType().GetProperty("IsProgressClearWaiting", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
            return (progressVisible is bool && (bool)progressVisible) || (progressClearWaiting is bool && (bool)progressClearWaiting);
        }

        public static IEnumerable<object> GetSelectedResults(object saWindow)
        {
            object selectedTab = GetSelectedTab(saWindow);
            object results = selectedTab?.GetType().GetProperty("Results", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
            if (results is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                    yield return item;
            }
        }

        public static void LoadTemplate(object saWindow, XElement element)
        {
            if (saWindow == null || !saType.IsInstanceOfType(saWindow)) return;

            object selectedTab = GetSelectedTab(saWindow);
            if (selectedTab == null) return;

            // Use the tab's Restore method which is what NT uses for template loading into a tab
            var restoreMethod = selectedTab.GetType().GetMethod("Restore", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(XElement) }, null);
            restoreMethod?.Invoke(selectedTab, new object[] { element });
        }

        public static List<string> ExportSelectedTradePerformanceGrids(object saWindow, string destinationFolder)
        {
            List<string> exported = new List<string>();
            if (saWindow == null || !saType.IsInstanceOfType(saWindow) || tradePerformanceDisplayType == null || ntGridType == null)
                return exported;

            Core.Globals.RandomDispatcher.Invoke(() =>
            {
                object selectedTab = GetSelectedTab(saWindow);
                if (selectedTab == null)
                    return;

                object report = selectedTab.GetType().GetField("tradePerformance", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(selectedTab);
                object reportViewModel = selectedTab.GetType().GetProperty("TradePerfViewModel", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
                if (!(report is DependencyObject reportVisual) || reportViewModel == null)
                    return;

                foreach (string displayName in new[] { "Summary", "Analysis", "Trades", "Orders", "Executions" })
                {
                    try
                    {
                        object displayValue = Enum.Parse(tradePerformanceDisplayType, displayName);
                        reportViewModel.GetType().GetProperty("SelectedDisplayType", BindingFlags.Public | BindingFlags.Instance)?.SetValue(reportViewModel, displayValue);
                        report.GetType().GetMethod("GenerateSubReport", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(report, null);
                        (report as FrameworkElement)?.UpdateLayout();

                        List<DependencyObject> grids = FindVisualChildren(reportVisual)
                            .Where(child => ntGridType.IsInstanceOfType(child))
                            .ToList();

                        for (int i = 0; i < grids.Count; i++)
                        {
                            string suffix = grids.Count == 1 ? string.Empty : "_" + (i + 1).ToString();
                            string fileName = System.IO.Path.Combine(destinationFolder, displayName + suffix + ".csv");
                            InvokeGridCsvExport(grids[i], fileName);
                            if (System.IO.File.Exists(fileName))
                                exported.Add(fileName);
                        }
                    }
                    catch
                    {
                        // Native report export is best-effort; callers write internal CSV fallbacks.
                    }
                }
            });

            return exported;
        }

        private static IEnumerable<DependencyObject> FindVisualChildren(DependencyObject parent)
        {
            if (parent == null)
                yield break;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                yield return child;

                foreach (DependencyObject grandChild in FindVisualChildren(child))
                    yield return grandChild;
            }
        }

        private static void InvokeGridCsvExport(object grid, string fileName)
        {
            MethodInfo method = ntGridType.GetMethod("OnExportToCsv", BindingFlags.NonPublic | BindingFlags.Instance);
            method?.Invoke(grid, new object[] { fileName });
        }

        public static void Run(object saWindow)
        {
            if (saWindow == null || !saType.IsInstanceOfType(saWindow)) return;

            var viewModel = GetViewModel(saWindow);
            if (viewModel == null) return;

            // Use the RunCommand field which is the ICommand for starting backtests
            var runCommandField = saVmType.GetField("RunCommand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            var runCommand = runCommandField?.GetValue(viewModel) as System.Windows.Input.ICommand;

            if (runCommand != null)
            {
                Core.Globals.RandomDispatcher.Invoke(() => {
                    if (runCommand.CanExecute(null))
                        runCommand.Execute(null);
                });
            }
            else
            {
                // Fallback to OnRun handler if command field not found
                saVmType.GetMethod("OnRun", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.Invoke(viewModel, new object[] { null, null });
            }
        }
    }
}
