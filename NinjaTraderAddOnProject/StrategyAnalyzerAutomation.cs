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
                SetMember(props, "instrumentOrInstrumentList", "InstrumentOrInstrumentList", instrumentOrInstrumentList);
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

        public static void CloseTab(object saWindow, object tab)
        {
            if (saWindow == null || tab == null || !saType.IsInstanceOfType(saWindow))
                return;

            InvokeOnAnalyzerDispatcher(saWindow, () =>
            {
                object viewModel = GetViewModel(saWindow);
                object currentTab = GetSelectedTab(saWindow);
                int tabCount = GetTabCountCore(saWindow);
                if (tabCount <= 1)
                    return;

                saVmType.GetProperty("SelectedTab", BindingFlags.Public | BindingFlags.Instance)?.SetValue(viewModel, tab);

                TabControl tabControl = saType.GetProperty("MainTabControl", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saWindow) as TabControl
                    ?? saType.GetField("saTabControl", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(saWindow) as TabControl;
                if (tabControl != null)
                    tabControl.SelectedItem = tab;

                MethodInfo closeMethod = saType.GetMethod("OnCloseTab", BindingFlags.Public | BindingFlags.Instance);
                if (closeMethod != null)
                    closeMethod.Invoke(saWindow, null);

                if (ContainsTab(saWindow, tab) && tabControl != null && tabControl.Items.Contains(tab) && tabControl.Items.Count > 1)
                    tabControl.Items.Remove(tab);

                if (currentTab != null && !ReferenceEquals(currentTab, tab) && ContainsTab(saWindow, currentTab))
                {
                    saVmType.GetProperty("SelectedTab", BindingFlags.Public | BindingFlags.Instance)?.SetValue(viewModel, currentTab);
                    if (tabControl != null)
                        tabControl.SelectedItem = currentTab;
                }
            });
        }

        private static int GetTabCountCore(object saWindow)
        {
            object tabItems = saType.GetProperty("TabItems", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saWindow);
            if (tabItems is ICollection collection)
                return collection.Count;

            TabControl tabControl = saType.GetProperty("MainTabControl", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saWindow) as TabControl
                ?? saType.GetField("saTabControl", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(saWindow) as TabControl;
            return tabControl?.Items.Count ?? 0;
        }

        private static bool ContainsTab(object saWindow, object tab)
        {
            object tabItems = saType.GetProperty("TabItems", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saWindow);
            if (tabItems is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                    if (ReferenceEquals(item, tab))
                        return true;
            }

            TabControl tabControl = saType.GetProperty("MainTabControl", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saWindow) as TabControl
                ?? saType.GetField("saTabControl", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(saWindow) as TabControl;
            return tabControl != null && tabControl.Items.Contains(tab);
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

                SetMember(props, "strategy", "Strategy", strategyName);

                XElement strategyElement = element.Element("Strategy")?.Elements().FirstOrDefault();
                object strategyTemplate = strategyType != null ? Activator.CreateInstance(strategyType) : null;

                if (strategyTemplate != null)
                {
                    ApplySimpleXmlProperties(strategyTemplate, strategyElement);
                    ApplyBarsPeriod(strategyTemplate, strategyElement);
                    SetStrategyTemplate(props, strategyTemplate);
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
                object barsPeriod = template?.GetType().GetProperty("BarsPeriod", BindingFlags.Public | BindingFlags.Instance)?.GetValue(template);
                string bars = DescribeBarsPeriod(barsPeriod);
                string instrument = props?.GetType().GetProperty("InstrumentOrInstrumentList", BindingFlags.Public | BindingFlags.Instance)?.GetValue(props) as string;
                return "Strategy=" + (strategy ?? "null") + ", TemplateType=" + (template?.GetType().FullName ?? "null") + ", Instrument=" + (instrument ?? "null") + ", Bars=" + (bars ?? "null");
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

        private static void ApplyBarsPeriod(object strategyTemplate, XElement strategyElement)
        {
            XElement barsElement = strategyElement?.Element("BarsPeriodSerializable");
            object barsPeriod = CreateBarsPeriod(barsElement);
            if (strategyTemplate == null || barsPeriod == null)
                return;

            Type strategyType = strategyTemplate.GetType();
            SetPropertyIfWritable(strategyTemplate, strategyType, "BarsPeriodSerializable", barsPeriod);
            SetPropertyIfWritable(strategyTemplate, strategyType, "BarsPeriod", barsPeriod);

            PropertyInfo barsPeriodsProperty = strategyType.GetProperty("BarsPeriods", BindingFlags.Public | BindingFlags.Instance);
            if (barsPeriodsProperty != null && barsPeriodsProperty.CanWrite)
            {
                Array barsPeriods = Array.CreateInstance(barsPeriod.GetType(), 1);
                barsPeriods.SetValue(barsPeriod, 0);
                barsPeriodsProperty.SetValue(strategyTemplate, barsPeriods);
            }
        }

        private static void SetStrategyTemplate(object props, object strategyTemplate)
        {
            if (props == null || strategyTemplate == null)
                return;

            SetMember(props, "strategyTemplate", "StrategyTemplate", strategyTemplate);
        }

        private static void SetMember(object target, string fieldName, string propertyName, object value)
        {
            if (target == null)
                return;

            Type targetType = target.GetType();
            FieldInfo field = targetType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && (value == null || field.FieldType.IsInstanceOfType(value)))
            {
                field.SetValue(target, value);
                return;
            }

            PropertyInfo property = targetType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanWrite && (value == null || property.PropertyType.IsInstanceOfType(value)))
                property.SetValue(target, value);
        }

        private static object CreateBarsPeriod(XElement barsElement)
        {
            Type barsPeriodType = ResolveType("NinjaTrader.Data.BarsPeriod");
            if (barsPeriodType == null)
                return null;

            object barsPeriod = Activator.CreateInstance(barsPeriodType);
            if (barsElement == null)
                return barsPeriod;

            ApplySimpleXmlProperties(barsPeriod, barsElement);
            return barsPeriod;
        }

        private static void SetPropertyIfWritable(object target, Type targetType, string propertyName, object value)
        {
            PropertyInfo property = targetType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
                return;

            if (value == null || property.PropertyType.IsInstanceOfType(value))
                property.SetValue(target, value);
        }

        private static string DescribeBarsPeriod(object barsPeriod)
        {
            if (barsPeriod == null)
                return null;

            Type type = barsPeriod.GetType();
            object barsType = type.GetProperty("BarsPeriodType", BindingFlags.Public | BindingFlags.Instance)?.GetValue(barsPeriod);
            object value = type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)?.GetValue(barsPeriod);
            object value2 = type.GetProperty("Value2", BindingFlags.Public | BindingFlags.Instance)?.GetValue(barsPeriod);
            return Convert.ToString(barsType, CultureInfo.InvariantCulture) + " " + Convert.ToString(value, CultureInfo.InvariantCulture) + "/" + Convert.ToString(value2, CultureInfo.InvariantCulture);
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
                object enumValue = Enum.Parse(property.PropertyType, backtestType.Value);
                SetMember(props, "backtestType", "BacktestType", enumValue);
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
