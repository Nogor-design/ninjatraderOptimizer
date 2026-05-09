using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace NinjaTrader.Custom.AddOns.Automation
{
    public class StrategyAnalyzerAutomation
    {
        private static Assembly guiAssembly = Assembly.Load("NinjaTrader.Gui");
        private static Type saType = guiAssembly.GetType("NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzer");
        private static Type saVmType = guiAssembly.GetType("NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzerViewModel");
        private static Type saTabType = guiAssembly.GetType("NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzerTabControl");

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

            object viewModel = GetViewModel(saWindow);
            if (viewModel == null || saTabType == null) return null;

            object newTab = Activator.CreateInstance(saTabType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { viewModel }, null);
            saType.GetMethod("AddNewTab", BindingFlags.Public | BindingFlags.Instance)?.Invoke(saWindow, new[] { newTab });
            saVmType.GetProperty("SelectedTab", BindingFlags.Public | BindingFlags.Instance)?.SetValue(viewModel, newTab);
            return newTab;
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

            var viewModel = saType.GetProperty("ViewModel", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saWindow);
            if (viewModel == null) return;

            saVmType.GetMethod("Restore", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(XElement) }, null)
                    ?.Invoke(viewModel, new object[] { element });
        }

        public static void Run(object saWindow)
        {
            if (saWindow == null || !saType.IsInstanceOfType(saWindow)) return;

            var viewModel = saType.GetProperty("ViewModel", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saWindow);
            if (viewModel == null) return;

            saVmType.GetMethod("OnRun", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(viewModel, new object[] { null, null });
        }
    }
}
