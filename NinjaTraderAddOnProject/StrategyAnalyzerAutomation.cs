using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        // Closes the Strategy Analyzer tab hosting `tab` and returns a
        // human-readable diagnostic describing what was attempted and whether the
        // tab count actually dropped.
        //
        // The host NinjaTrader.Gui assembly is partially obfuscated — the bodies
        // of logic methods (e.g. OnCloseTab) are stripped from the shipped DLL, so
        // they cannot be trusted to do anything when invoked by reflection. We
        // therefore close a tab by explicitly removing its wrapping TabItem from
        // whichever backing collection the TabControl uses (ItemsSource when bound,
        // otherwise Items), and verify by re-counting. OnCloseTab() is kept only as
        // a last-resort fallback for builds where it is honoured.
        public static string CloseTab(object saWindow, object tab)
        {
            if (saWindow == null || tab == null || !saType.IsInstanceOfType(saWindow))
                return "CloseTab skipped: null saWindow or tab.";

            return InvokeOnAnalyzerDispatcher(saWindow, () =>
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                try
                {
                    object viewModel = GetViewModel(saWindow);
                    object currentTab = GetSelectedTab(saWindow);
                    TabControl tabControl = ResolveTabControl(saWindow);
                    int before = CountTabs(saWindow, tabControl);

                    sb.Append("CloseTab before=").Append(before)
                      .Append(" page=").Append(tab.GetType().Name)
                      .Append(" tabControl=").Append(tabControl == null ? "null" : tabControl.GetType().Name);
                    if (tabControl != null)
                        sb.Append(" itemsSource=").Append(tabControl.ItemsSource == null ? "null" : tabControl.ItemsSource.GetType().Name)
                          .Append(" items=").Append(tabControl.Items.Count);

                    if (before <= 1)
                    {
                        sb.Append(" -> skip (<=1 tab).");
                        return sb.ToString();
                    }

                    bool removed = false;
                    if (tabControl != null)
                        removed = TryRemoveTab(tabControl, tab, sb);

                    if (!removed)
                    {
                        // Fallback: select the tab and ask NT to close it. May be a
                        // no-op on obfuscated builds; the before/after count tells us.
                        SelectTab(viewModel, tabControl, tab);
                        MethodInfo closeMethod = saType.GetMethod("OnCloseTab", BindingFlags.Public | BindingFlags.Instance);
                        sb.Append(" onCloseTab=").Append(closeMethod == null ? "missing" : "invoked");
                        if (closeMethod != null)
                            closeMethod.Invoke(saWindow, null);
                    }

                    int after = CountTabs(saWindow, tabControl);
                    sb.Append(" after=").Append(after)
                      .Append(after < before ? " -> CLOSED." : " -> NO CHANGE (leaked).");

                    // Restore selection to the tab that was active before we started,
                    // if it survived.
                    if (currentTab != null && !ReferenceEquals(currentTab, tab))
                        SelectTab(viewModel, tabControl, currentTab);

                    return sb.ToString();
                }
                catch (Exception ex)
                {
                    while (ex is TargetInvocationException && ex.InnerException != null)
                        ex = ex.InnerException;
                    sb.Append(" -> EXCEPTION ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                    return sb.ToString();
                }
            });
        }

        private static TabControl ResolveTabControl(object saWindow)
        {
            return saType.GetProperty("MainTabControl", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saWindow) as TabControl
                ?? saType.GetField("saTabControl", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(saWindow) as TabControl;
        }

        private static int CountTabs(object saWindow, TabControl tabControl)
        {
            if (tabControl != null)
            {
                if (tabControl.ItemsSource is ICollection src)
                    return src.Count;
                return tabControl.Items.Count;
            }
            return GetTabCountCore(saWindow);
        }

        // True when `element` is the page itself, or a TabItem/ContentControl whose
        // Content is the page, or any element whose DataContext is the page.
        private static bool MatchesPage(object element, object page)
        {
            if (ReferenceEquals(element, page))
                return true;
            ContentControl cc = element as ContentControl;
            if (cc != null && ReferenceEquals(cc.Content, page))
                return true;
            FrameworkElement fe = element as FrameworkElement;
            if (fe != null && ReferenceEquals(fe.DataContext, page))
                return true;
            return false;
        }

        // Removes the entry hosting `page` from whichever backing collection the
        // TabControl uses. Handles both ItemsSource-bound and direct-Items modes.
        private static bool TryRemoveTab(TabControl tabControl, object page, System.Text.StringBuilder sb)
        {
            IList source = tabControl.ItemsSource as IList;
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (MatchesPage(source[i], page))
                    {
                        source.RemoveAt(i);
                        sb.Append(" removed=itemsSource[").Append(i).Append(']');
                        return true;
                    }
                }
                sb.Append(" itemsSource:no-match");
                return false;
            }

            for (int i = 0; i < tabControl.Items.Count; i++)
            {
                if (MatchesPage(tabControl.Items[i], page))
                {
                    object victim = tabControl.Items[i];
                    tabControl.Items.Remove(victim);
                    sb.Append(" removed=items[").Append(i).Append(']');
                    return true;
                }
            }
            sb.Append(" items:no-match");
            return false;
        }

        private static void SelectTab(object viewModel, TabControl tabControl, object page)
        {
            if (viewModel != null)
                saVmType.GetProperty("SelectedTab", BindingFlags.Public | BindingFlags.Instance)?.SetValue(viewModel, page);
            if (tabControl != null)
            {
                object target = page;
                for (int i = 0; i < tabControl.Items.Count; i++)
                {
                    if (MatchesPage(tabControl.Items[i], page)) { target = tabControl.Items[i]; break; }
                }
                tabControl.SelectedItem = target;
            }
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
                {
                    yield return item;
                    foreach (object child in GetChildResults(item))
                        yield return child;
                }
            }
        }

        private static IEnumerable<object> GetChildResults(object result)
        {
            object children = result?.GetType().GetProperty("Children", BindingFlags.Public | BindingFlags.Instance)?.GetValue(result)
                ?? result?.GetType().GetField("children", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(result);

            if (children is IEnumerable enumerable)
            {
                foreach (object child in enumerable)
                    yield return child;
            }
        }

        public static void WriteSelectedTabDiagnostics(object saWindow, string fileName)
        {
            if (saWindow == null || string.IsNullOrWhiteSpace(fileName))
                return;

            InvokeOnAnalyzerDispatcher(saWindow, () =>
            {
                try
                {
                    object selectedTab = GetSelectedTab(saWindow);
                    var lines = new List<string>();
                    DumpObjectShape(lines, "SelectedTab", selectedTab, 0);

                    if (selectedTab is DependencyObject visual)
                        DumpVisualGridShape(lines, visual);

                    File.WriteAllLines(fileName, lines, System.Text.Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(fileName, "Diagnostics failed: " + ex, System.Text.Encoding.UTF8);
                }
            });
        }

        private static void DumpObjectShape(List<string> lines, string label, object instance, int depth)
        {
            if (instance == null || depth > 1)
                return;

            Type type = instance.GetType();
            lines.Add(label + " type=" + type.FullName);

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).OrderBy(p => p.Name))
                DumpMemberShape(lines, label + "." + property.Name, () => property.GetValue(instance), property.PropertyType, depth);

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).OrderBy(f => f.Name))
                DumpMemberShape(lines, label + "." + field.Name, () => field.GetValue(instance), field.FieldType, depth);
        }

        private static void DumpMemberShape(List<string> lines, string label, Func<object> getValue, Type declaredType, int depth)
        {
            object value;
            try
            {
                value = getValue();
            }
            catch
            {
                return;
            }

            if (value == null)
                return;

            Type valueType = value.GetType();
            int? count = TryGetCount(value);
            bool interesting = count.HasValue
                || label.IndexOf("Result", StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf("Optim", StringComparison.OrdinalIgnoreCase) >= 0
                || label.IndexOf("Grid", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!interesting)
                return;

            lines.Add(label + " declared=" + declaredType.FullName + " value=" + valueType.FullName + (count.HasValue ? " count=" + count.Value.ToString(CultureInfo.InvariantCulture) : string.Empty));

            if (depth == 0 && !IsScalar(valueType) && !(value is IEnumerable && !(value is string)))
                DumpObjectShape(lines, label, value, depth + 1);
        }

        private static void DumpVisualGridShape(List<string> lines, DependencyObject root)
        {
            int gridIndex = 0;
            foreach (DependencyObject child in FindVisualChildren(root))
            {
                if (!ntGridType.IsInstanceOfType(child))
                    continue;

                gridIndex++;
                lines.Add("Visual.NTGrid[" + gridIndex.ToString(CultureInfo.InvariantCulture) + "] type=" + child.GetType().FullName);
                DumpMemberShape(lines, "Visual.NTGrid[" + gridIndex.ToString(CultureInfo.InvariantCulture) + "].ItemsSource", () => child.GetType().GetProperty("ItemsSource", BindingFlags.Public | BindingFlags.Instance)?.GetValue(child), typeof(object), 1);
                DumpMemberShape(lines, "Visual.NTGrid[" + gridIndex.ToString(CultureInfo.InvariantCulture) + "].Items", () => child.GetType().GetProperty("Items", BindingFlags.Public | BindingFlags.Instance)?.GetValue(child), typeof(object), 1);
                DumpMemberShape(lines, "Visual.NTGrid[" + gridIndex.ToString(CultureInfo.InvariantCulture) + "].DataContext", () => (child as FrameworkElement)?.DataContext, typeof(object), 1);
            }
        }

        private static int? TryGetCount(object value)
        {
            if (value is ICollection collection)
                return collection.Count;

            PropertyInfo countProperty = value.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            if (countProperty != null)
            {
                try
                {
                    object count = countProperty.GetValue(value);
                    if (count != null)
                        return Convert.ToInt32(count, CultureInfo.InvariantCulture);
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool IsScalar(Type type)
        {
            Type effectiveType = Nullable.GetUnderlyingType(type) ?? type;
            return effectiveType.IsPrimitive || effectiveType.IsEnum || effectiveType == typeof(string) || effectiveType == typeof(decimal) || effectiveType == typeof(DateTime);
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
                    ApplyOptimizerTemplate(strategyTemplate, element);
                    SetStrategyTemplate(props, strategyTemplate);
                }

                ApplyAnalyzerTemplateType(selectedTab, element);
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
                object optimizer = template?.GetType().GetProperty("Optimizer", BindingFlags.Public | BindingFlags.Instance)?.GetValue(template);
                object fitness = template?.GetType().GetProperty("OptimizationFitness", BindingFlags.Public | BindingFlags.Instance)?.GetValue(template);
                object optimizationParameters = template?.GetType().GetProperty("OptimizationParameters", BindingFlags.Public | BindingFlags.Instance)?.GetValue(template);
                int optimizationParameterCount = (optimizationParameters as ICollection)?.Count ?? 0;
                return "Strategy=" + (strategy ?? "null")
                    + ", TemplateType=" + (template?.GetType().FullName ?? "null")
                    + ", Instrument=" + (instrument ?? "null")
                    + ", Bars=" + (bars ?? "null")
                    + ", Optimizer=" + (optimizer?.GetType().FullName ?? "null")
                    + ", Fitness=" + (fitness?.GetType().FullName ?? "null")
                    + ", OptimizationParameters=" + optimizationParameterCount.ToString(CultureInfo.InvariantCulture);
            });
        }

        internal static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            Type type = Type.GetType(typeName, false);
            if (type != null)
                return type;

            type = Type.GetType(typeName + ", NinjaTraderOptimizerProject", false);
            if (type != null)
                return type;

            // Keep the LAST match: NinjaTrader cannot unload custom assemblies, so every
            // NinjaScript recompile leaves the stale assembly resident and loads the new
            // one AFTER it. First-match returned the stale type, so strategy properties
            // added since the last NT restart silently failed to apply (GetProperty null).
            Type latest = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false);
                if (type != null)
                    latest = type;
            }

            return latest;
        }

        internal static void ApplySimpleXmlProperties(object target, XElement source)
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

        internal static void ApplyBarsPeriod(object strategyTemplate, XElement strategyElement)
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
            {
                Diagnostic("Property not writable: " + targetType.FullName + "." + propertyName + ".");
                return;
            }

            if (value == null || property.PropertyType.IsInstanceOfType(value))
            {
                property.SetValue(target, value);
            }
            else
            {
                Diagnostic("Property type mismatch: " + targetType.FullName + "." + propertyName + " expects " + property.PropertyType.FullName + " but got " + value.GetType().FullName + ".");
            }
        }

        private static void Diagnostic(string message)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "nt8_batch_optimizer_loader.log");
                File.AppendAllText(path, DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
            }
            catch
            {
            }
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

        private static void ApplyAnalyzerTemplateType(object selectedTab, XElement element)
        {
            object props = selectedTab?.GetType().GetProperty("TabStrategyProperties", BindingFlags.Public | BindingFlags.Instance)?.GetValue(selectedTab);
            if (props == null)
                return;

            string backtestType = element.Element("BacktestType")?.Value;
            if (string.IsNullOrWhiteSpace(backtestType))
                backtestType = element.Element("Strategy")?.Elements().FirstOrDefault()?.Elements().FirstOrDefault(child => child.Name.LocalName == "Category")?.Value;
            if (string.IsNullOrWhiteSpace(backtestType) && element.Element("OptimizationParameters") != null)
                backtestType = "Optimize";
            if (string.IsNullOrWhiteSpace(backtestType))
                return;

            PropertyInfo property = props.GetType().GetProperty("BacktestType", BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite)
                return;

            try
            {
                object enumValue = Enum.Parse(property.PropertyType, backtestType);
                SetMember(props, "backtestType", "BacktestType", enumValue);
            }
            catch
            {
            }
        }

        private static void ApplyOptimizerTemplate(object strategyTemplate, XElement element)
        {
            if (strategyTemplate == null || element == null)
                return;

            string optimizerTypeName = element.Element("OptimizerType")?.Value;
            string fitnessTypeName = element.Element("OptimizationFitness")?.Value;
            Diagnostic("ApplyOptimizerTemplate requested optimizer=" + (optimizerTypeName ?? "null") + ", fitness=" + (fitnessTypeName ?? "null") + ".");

            object optimizer = CreateTemplateObject(optimizerTypeName);
            if (optimizer != null)
            {
                ApplyParameterWrappers(optimizer, element.Element("OptimizerParameters"));
                SetPropertyIfWritable(strategyTemplate, strategyTemplate.GetType(), "Optimizer", optimizer);
                Diagnostic("Applied optimizer instance type=" + optimizer.GetType().AssemblyQualifiedName + ".");
            }
            else
            {
                Diagnostic("Could not create optimizer for type=" + (optimizerTypeName ?? "null") + ".");
            }

            object fitness = CreateTemplateObject(fitnessTypeName);
            if (fitness != null)
            {
                SetPropertyIfWritable(strategyTemplate, strategyTemplate.GetType(), "OptimizationFitness", fitness);
                Diagnostic("Applied fitness instance type=" + fitness.GetType().AssemblyQualifiedName + ".");
            }
            else
            {
                Diagnostic("Could not create fitness for type=" + (fitnessTypeName ?? "null") + ".");
            }

            ApplyOptimizationParameters(strategyTemplate, element.Element("OptimizationParameters"));
        }

        private static object CreateTemplateObject(string typeName)
        {
            Type type = ResolveType(typeName);
            if (type == null)
            {
                Diagnostic("ResolveType failed for " + (typeName ?? "null") + ".");
                return null;
            }

            try
            {
                object instance = Activator.CreateInstance(type);
                InitializeTemplateObject(instance);
                return instance;
            }
            catch (Exception ex)
            {
                Diagnostic("CreateTemplateObject failed for " + type.FullName + ": " + ex.Message);
                return null;
            }
        }

        private static void InitializeTemplateObject(object instance)
        {
            if (instance == null)
                return;

            try
            {
                MethodInfo setState = instance.GetType().GetMethod("SetState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                setState?.Invoke(instance, new object[] { State.SetDefaults });
            }
            catch
            {
                // Keep the raw instance if NinjaTrader refuses state initialization in this context.
            }
        }

        private static void ApplyParameterWrappers(object target, XElement wrapperRoot)
        {
            XElement wrappers = wrapperRoot?.Elements().FirstOrDefault();
            if (target == null || wrappers == null)
                return;

            Type targetType = target.GetType();
            foreach (XElement wrapper in wrappers.Elements().Where(e => e.Name.LocalName == "ParameterWrapper"))
            {
                string name = wrapper.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value;
                XElement valueElement = wrapper.Elements().FirstOrDefault(e => e.Name.LocalName == "Value");
                if (string.IsNullOrWhiteSpace(name) || valueElement == null)
                    continue;

                PropertyInfo property = targetType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (property == null || !property.CanWrite)
                    continue;

                try
                {
                    property.SetValue(target, ConvertXmlValue(valueElement.Value, property.PropertyType));
                }
                catch
                {
                    // Optimizer wrapper values are optional; keep NinjaTrader defaults if conversion fails.
                }
            }
        }

        private static void ApplyOptimizationParameters(object strategyTemplate, XElement optimizationRoot)
        {
            object collection = strategyTemplate?.GetType().GetProperty("OptimizationParameters", BindingFlags.Public | BindingFlags.Instance)?.GetValue(strategyTemplate);
            if (collection == null || optimizationRoot == null)
                return;

            MethodInfo clearMethod = collection.GetType().GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo addMethod = collection.GetType().GetMethod("Add", BindingFlags.Public | BindingFlags.Instance);
            if (clearMethod == null || addMethod == null)
                return;

            clearMethod.Invoke(collection, null);
            XElement parameters = optimizationRoot.Elements().FirstOrDefault();
            if (parameters == null)
                return;

            foreach (XElement parameterElement in parameters.Elements().Where(e => e.Name.LocalName == "Parameter"))
            {
                Parameter parameter = CreateOptimizationParameter(parameterElement);
                if (parameter != null)
                    addMethod.Invoke(collection, new object[] { parameter });
            }
        }

        private static Parameter CreateOptimizationParameter(XElement parameterElement)
        {
            if (parameterElement == null)
                return null;

            try
            {
                Parameter parameter = new Parameter();
                parameter.Name = parameterElement.Elements().FirstOrDefault(e => e.Name.LocalName == "Name")?.Value;
                parameter.ParameterTypeSerializable = parameterElement.Elements().FirstOrDefault(e => e.Name.LocalName == "ParameterTypeSerializable")?.Value;
                parameter.Increment = Convert.ToDouble(parameterElement.Elements().FirstOrDefault(e => e.Name.LocalName == "Increment")?.Value ?? "1", CultureInfo.InvariantCulture);

                Type parameterType = parameter.ParameterType ?? ResolveType(parameter.ParameterTypeSerializable);
                if (parameterType != null)
                    parameter.ParameterType = parameterType;

                XElement minElement = parameterElement.Elements().FirstOrDefault(e => e.Name.LocalName == "Min");
                XElement maxElement = parameterElement.Elements().FirstOrDefault(e => e.Name.LocalName == "Max");
                XElement valueElement = parameterElement.Elements().FirstOrDefault(e => e.Name.LocalName == "ValueSerializable");

                if (minElement != null)
                    parameter.Min = ConvertParameterValue(minElement.Value, parameter.ParameterType);
                if (maxElement != null)
                    parameter.Max = ConvertParameterValue(maxElement.Value, parameter.ParameterType);
                if (valueElement != null)
                    parameter.Value = ConvertParameterValue(valueElement.Value, parameter.ParameterType);

                string enumValues = parameterElement.Elements().FirstOrDefault(e => e.Name.LocalName == "EnumValuesSerializable")?.Value;
                if (!string.IsNullOrWhiteSpace(enumValues))
                    parameter.EnumValuesSerializable = enumValues.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).ToArray();

                return string.IsNullOrWhiteSpace(parameter.Name) ? null : parameter;
            }
            catch
            {
                return null;
            }
        }

        private static object ConvertParameterValue(string value, Type parameterType)
        {
            Type targetType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
            if (targetType == null || targetType == typeof(string))
                return value;
            if (targetType == typeof(bool))
                return bool.Parse(value);
            if (targetType.IsEnum)
                return Enum.Parse(targetType, value);

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
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

        public static bool Run(object saWindow)
        {
            if (saWindow == null || !saType.IsInstanceOfType(saWindow)) return false;

            var viewModel = GetViewModel(saWindow);
            if (viewModel == null) return false;

            // Use the RunCommand field which is the ICommand for starting backtests
            var runCommandField = saVmType.GetField("RunCommand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            var runCommand = runCommandField?.GetValue(viewModel) as System.Windows.Input.ICommand;

            if (runCommand != null)
            {
                return InvokeOnAnalyzerDispatcher(saWindow, () => {
                    if (!runCommand.CanExecute(null))
                        return false;
                    runCommand.Execute(null);
                    return true;
                });
            }
            else
            {
                // Fallback to OnRun handler if command field not found
                MethodInfo onRun = saVmType.GetMethod("OnRun", BindingFlags.NonPublic | BindingFlags.Instance);
                if (onRun == null)
                    return false;
                onRun.Invoke(viewModel, new object[] { null, null });
                return true;
            }
        }
    }
}
