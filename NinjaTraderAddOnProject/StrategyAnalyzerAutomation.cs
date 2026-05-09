using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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
            Dispatcher dispatcher = Application.Current?.Dispatcher ?? Core.Globals.RandomDispatcher;
            InvokeOnDispatcher(dispatcher, () => {
                foreach (Window w in Application.Current.Windows)
                {
                    if (saType.IsInstanceOfType(w))
                        analyzers.Add(w);
                }
            });
            return analyzers;
        }

        private static Dispatcher GetDispatcher(object saWindow)
        {
            if (saWindow is DispatcherObject dispatcherObject)
                return dispatcherObject.Dispatcher;

            return Application.Current?.Dispatcher ?? Core.Globals.RandomDispatcher;
        }

        private static void InvokeOnAnalyzerDispatcher(object saWindow, Action action)
        {
            InvokeOnDispatcher(GetDispatcher(saWindow), action);
        }

        private static T InvokeOnAnalyzerDispatcher<T>(object saWindow, Func<T> action)
        {
            Dispatcher dispatcher = GetDispatcher(saWindow);
            if (dispatcher == null || dispatcher.CheckAccess())
                return action();

            return (T)dispatcher.Invoke(action);
        }

        private static void InvokeOnDispatcher(Dispatcher dispatcher, Action action)
        {
            if (dispatcher == null || dispatcher.CheckAccess())
                action();
            else
                dispatcher.Invoke(action);
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
            return selectedTab?.GetType().GetProperty("TabStrategyProperties", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab)
                ?? selectedTab?.GetType().GetProperty("Properties", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
        }

        public static string GetSelectedInstrumentOrInstrumentList(object saWindow)
        {
            return InvokeOnAnalyzerDispatcher(saWindow, () =>
            {
                object props = GetSelectedTabProperties(saWindow);
                return props?.GetType().GetProperty("InstrumentOrInstrumentList", BindingFlags.Public | BindingFlags.Instance)?.GetValue(props) as string;
            });
        }

        public static void SetSelectedInstrumentOrInstrumentList(object saWindow, string instrumentOrInstrumentList)
        {
            if (string.IsNullOrWhiteSpace(instrumentOrInstrumentList))
                return;

            InvokeOnAnalyzerDispatcher(saWindow, () =>
            {
                object props = GetSelectedTabProperties(saWindow);
                PropertyInfo property = props?.GetType().GetProperty("InstrumentOrInstrumentList", BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanWrite)
                    property.SetValue(props, instrumentOrInstrumentList);
            });
        }

        public static object AddNewTab(object saWindow)
        {
            if (saWindow == null || !saType.IsInstanceOfType(saWindow)) return null;

            object newTab = null;
            InvokeOnAnalyzerDispatcher(saWindow, () =>
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
            return InvokeOnAnalyzerDispatcher(saWindow, () =>
            {
                object selectedTab = GetSelectedTab(saWindow);
                object results = selectedTab?.GetType().GetProperty("Results", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
                return (results as ICollection)?.Count ?? 0;
            });
        }

        public static bool IsSelectedTabBusy(object saWindow)
        {
            return InvokeOnAnalyzerDispatcher(saWindow, () =>
            {
                object selectedTab = GetSelectedTab(saWindow);
                if (selectedTab == null) return false;

                var progressVisible = selectedTab.GetType().GetProperty("IsProgressVisible", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
                var progressClearWaiting = selectedTab.GetType().GetProperty("IsProgressClearWaiting", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
                return (progressVisible is bool && (bool)progressVisible) || (progressClearWaiting is bool && (bool)progressClearWaiting);
            });
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

            InvokeOnAnalyzerDispatcher(saWindow, () =>
            {
                object selectedTab = GetSelectedTab(saWindow);
                if (selectedTab == null) return;

                object props = GetSelectedTabProperties(saWindow);
                if (props == null) return;

                string strategyTypeName = element.Element("StrategyType")?.Value;
                Type strategyType = ResolveType(strategyTypeName);
                string strategyName = strategyType?.Name ?? strategyTypeName?.Split('.').Last();
                if (string.IsNullOrWhiteSpace(strategyName))
                    return;

                PropertyInfo suppressStrategyChange = props.GetType().GetProperty("SuppressStrategyChange", BindingFlags.Public | BindingFlags.Instance);
                try
                {
                    suppressStrategyChange?.SetValue(props, true);
                    props.GetType().GetProperty("Strategy", BindingFlags.Public | BindingFlags.Instance)?.SetValue(props, strategyName);

                    object strategyTemplate = null;
                    XElement strategyElement = element.Element("Strategy")?.Elements().FirstOrDefault();
                    if (strategyType != null)
                    {
                        strategyTemplate = Activator.CreateInstance(strategyType);
                        ApplySimpleXmlProperties(strategyTemplate, strategyElement);
                        props.GetType().GetProperty("StrategyTemplate", BindingFlags.Public | BindingFlags.Instance)?.SetValue(props, strategyTemplate);
                    }
                }
                finally
                {
                    suppressStrategyChange?.SetValue(props, false);
                }

                ApplyOptimizerTemplate(selectedTab, element);
            });
        }

        public static string GetSelectedTemplateDebug(object saWindow)
        {
            return InvokeOnAnalyzerDispatcher(saWindow, () =>
            {
                object props = GetSelectedTabProperties(saWindow);
                object template = props?.GetType().GetProperty("StrategyTemplate", BindingFlags.Public | BindingFlags.Instance)?.GetValue(props);
                string strategy = props?.GetType().GetProperty("Strategy", BindingFlags.Public | BindingFlags.Instance)?.GetValue(props) as string;
                return "Strategy=" + (strategy ?? "null") + ", TemplateType=" + (template?.GetType().FullName ?? "null");
            });
        }

        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            Type type = Type.GetType(typeName, false);
            if (type != null)
                return type;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        private static void ApplySimpleXmlProperties(object target, XElement source)
        {
            if (target == null || source == null)
                return;

            Type targetType = target.GetType();
            foreach (XElement child in source.Elements())
            {
                if (child.HasElements)
                    continue;

                PropertyInfo property = targetType.GetProperty(child.Name.LocalName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite)
                    continue;

                try
                {
                    object value = ConvertXmlValue(child.Value, property.PropertyType);
                    property.SetValue(target, value);
                }
                catch
                {
                    // Some NinjaTrader properties have custom converters; skip those and keep defaults.
                }
            }
        }

        private static object ConvertXmlValue(string value, Type destinationType)
        {
            Type targetType = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
            if (targetType == typeof(string))
                return value;
            if (targetType == typeof(bool))
                return bool.Parse(value);
            if (targetType == typeof(DateTime))
                return DateTime.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(TimeSpan))
                return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
            if (targetType.IsEnum)
                return Enum.Parse(targetType, value);

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        private static void ApplyOptimizerTemplate(object selectedTab, XElement element)
        {
            object props = selectedTab?.GetType().GetProperty("TabStrategyProperties", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
            if (props == null)
                return;

            XElement backtestType = element.Element("BacktestType");
            if (backtestType == null || string.IsNullOrWhiteSpace(backtestType.Value))
                return;

            PropertyInfo property = props.GetType().GetProperty("BacktestType", BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
                return;

            try
            {
                property.SetValue(props, Enum.Parse(property.PropertyType, backtestType.Value));
            }
            catch
            {
            }
        }

        public static List<string> ExportSelectedTradePerformanceGrids(object saWindow, string destinationFolder)
        {
            List<string> exported = new List<string>();
            if (saWindow == null || !saType.IsInstanceOfType(saWindow) || tradePerformanceDisplayType == null || ntGridType == null)
                return exported;

            InvokeOnAnalyzerDispatcher(saWindow, () =>
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
                InvokeOnAnalyzerDispatcher(saWindow, () => {
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
