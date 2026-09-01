// EXPERIMENTAL, NOT COMPILED INTO NinjaTraderAddOnProject.dll.
// ConnectPlayback worked in the June 2026 spike, but external strategy enable
// stalled at Configure on NinjaTrader 8.1.7.1. Preserve this source as research
// evidence only; production IPC accepts ObserveCompile and RunBatch commands.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Custom.AddOns.Automation;
using NinjaTrader.NinjaScript;

namespace NinjaTraderAddOnProject
{
    /// <summary>
    /// Phase-2 parity-loop IPC commands: headless Playback connect + headless strategy
    /// enable/disable (no Strategies-tab click). Design of record:
    /// ta_foundation docs/designs/parity_phase2_live_leg_design.md.
    ///
    /// Commands (C:\temp\nt8_command.json, watched by the global AddOn watcher):
    ///   { "action": "ConnectPlayback", "runId": "...", "connectionName": "Playback..." }
    ///   { "action": "EnableStrategy",  "runId": "...", "templatePath": "...xml",
    ///     "account": "Sim101", "timeoutSeconds": 120 }
    ///   { "action": "DisableStrategy", "runId": "..." }   // runId of a prior Enable
    ///
    /// Status round-trip via C:\temp\nt8_status.json (same shape the Python pollers
    /// read: runId/state/lastError/heartbeatUtc + a lifecycle "detail" field).
    /// The EnableStrategy state walk is intentionally VERBOSE-LOGGED: which SetState
    /// sequence reaches Realtime is the one empirical unknown this command exists to
    /// answer (the public surface — StrategyBase.SetState, Account/Instrument setters,
    /// Globals.ConnectOptions, Connection.Connect — was verified by reflection
    /// 2026-06-12 against NinjaTrader.Core 8.1.7.1).
    ///
    /// GUARDRAIL: this service only configures and enables the REAL strategy from a
    /// template; it must never grow trading logic.
    /// </summary>
    internal static class StrategyLifecycleService
    {
        private const string StatusFilePath = @"C:\temp\nt8_status.json";

        // runId -> live strategy instance, so DisableStrategy can terminate it.
        private static readonly Dictionary<string, StrategyBase> ActiveStrategies =
            new Dictionary<string, StrategyBase>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new object();

        public static bool IsLifecycleCommand(string json)
        {
            string action = ExtractJsonString(json, "action");
            if (action == null)
                return false;
            return action.Equals("ConnectPlayback", StringComparison.OrdinalIgnoreCase)
                || action.Equals("EnableStrategy", StringComparison.OrdinalIgnoreCase)
                || action.Equals("DisableStrategy", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task Handle(string json, Action<string> log)
        {
            string action = ExtractJsonString(json, "action") ?? "";
            string runId = ExtractJsonString(json, "runId");
            if (string.IsNullOrWhiteSpace(runId))
                runId = action.ToLowerInvariant() + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

            try
            {
                if (action.Equals("ConnectPlayback", StringComparison.OrdinalIgnoreCase))
                    await ConnectPlayback(json, runId, log);
                else if (action.Equals("EnableStrategy", StringComparison.OrdinalIgnoreCase))
                    await EnableStrategy(json, runId, log);
                else if (action.Equals("DisableStrategy", StringComparison.OrdinalIgnoreCase))
                    DisableStrategy(runId, log);
            }
            catch (Exception ex)
            {
                SafeLog(log, action + " error: " + ex.Message);
                WriteStatus(runId, "failed", ex.Message, null);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        //  ConnectPlayback
        // ─────────────────────────────────────────────────────────────────────

        private static async Task ConnectPlayback(string json, string runId, Action<string> log)
        {
            string requestedName = ExtractJsonString(json, "connectionName");
            int timeoutSeconds = Math.Max(10, ExtractJsonInt(json, "timeoutSeconds", 90));

            // ALL Connection/Globals access on the UI dispatcher — from a worker
            // thread these trip NT-internal Debug.Assert dialogs that block the
            // whole IPC pipeline (live-observed 2026-06-12).
            Func<Action, Task> onUi = action => System.Windows.Application.Current.Dispatcher.InvokeAsync(action).Task;

            bool already = false;
            string connectError = null;
            await onUi(() =>
            {
                try
                {
                    if (Connection.PlaybackConnection != null)
                    {
                        already = true;
                        return;
                    }
                    ConnectOptions options = NinjaTrader.Core.Globals.ConnectOptions
                        .FirstOrDefault(o => !string.IsNullOrEmpty(requestedName)
                            ? string.Equals(o.Name, requestedName, StringComparison.OrdinalIgnoreCase)
                            : (o.Name ?? "").IndexOf("Playback", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (options == null)
                    {
                        connectError = "no playback connection configured (known: "
                            + string.Join(" | ", NinjaTrader.Core.Globals.ConnectOptions.Select(o => o.Name)) + ")";
                        return;
                    }
                    SafeLog(log, "ConnectPlayback: connecting '" + options.Name + "'...");
                    WriteStatus(runId, "running", null, "connecting:" + options.Name);
                    Connection.Connect(options);
                }
                catch (Exception ex)
                {
                    connectError = ex.Message;
                }
            });
            if (already)
            {
                SafeLog(log, "ConnectPlayback: playback connection already up.");
                WriteStatus(runId, "finished", null, "already_connected");
                return;
            }
            if (connectError != null)
            {
                WriteStatus(runId, "failed", connectError, null);
                SafeLog(log, "ConnectPlayback: " + connectError);
                return;
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                string detail = null;
                bool connected = false;
                await onUi(() =>
                {
                    Connection pb = Connection.PlaybackConnection;
                    connected = pb != null && pb.Status == ConnectionStatus.Connected;
                    detail = pb != null ? pb.Status.ToString() : "no_connection";
                });
                if (connected)
                {
                    SafeLog(log, "ConnectPlayback: connected.");
                    WriteStatus(runId, "finished", null, "connected");
                    return;
                }
                WriteStatus(runId, "running", null, "waiting:" + detail);
                await Task.Delay(1000);
            }
            WriteStatus(runId, "failed", "playback connect timeout after " + timeoutSeconds + "s", null);
            SafeLog(log, "ConnectPlayback: timeout.");
        }

        // ─────────────────────────────────────────────────────────────────────
        //  EnableStrategy / DisableStrategy
        // ─────────────────────────────────────────────────────────────────────

        private static async Task EnableStrategy(string json, string runId, Action<string> log)
        {
            string templatePath = ExtractJsonString(json, "templatePath");
            string accountName = ExtractJsonString(json, "account") ?? "Sim101";
            int timeoutSeconds = Math.Max(15, ExtractJsonInt(json, "timeoutSeconds", 120));

            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
            {
                WriteStatus(runId, "failed", "templatePath missing or not found: " + templatePath, null);
                return;
            }

            XElement element = XElement.Load(templatePath);
            string typeName = element.Element("StrategyType") != null ? element.Element("StrategyType").Value : null;
            XElement strategyElement = element.Element("Strategy") != null
                ? element.Element("Strategy").Elements().FirstOrDefault() : null;
            if (string.IsNullOrWhiteSpace(typeName) || strategyElement == null)
            {
                WriteStatus(runId, "failed", "template lacks StrategyType or Strategy element", null);
                return;
            }

            Type strategyType = StrategyAnalyzerAutomation.ResolveType(typeName);
            if (strategyType == null)
            {
                WriteStatus(runId, "failed", "cannot resolve strategy type: " + typeName, null);
                return;
            }
            SafeLog(log, "EnableStrategy: resolved " + strategyType.AssemblyQualifiedName);

            var transitions = new StringBuilder();
            StrategyBase strategy = null;
            Account account = null;
            Exception walkError = null;

            // ALL NT-object access on the UI dispatcher: Account.All / Connection
            // calls from a worker thread trip NT's internal Debug.Assert dialogs
            // (live-observed iteration 2), which block the whole IPC pipeline.
            Func<Action, Task> onUi = action => System.Windows.Application.Current.Dispatcher.InvokeAsync(action).Task;

            await onUi(() =>
            {
                try
                {
                    account = Account.All.FirstOrDefault(a => string.Equals(a.Name, accountName, StringComparison.OrdinalIgnoreCase));
                    if (account == null)
                        return;

                    strategy = (StrategyBase)Activator.CreateInstance(strategyType);
                    transitions.Append("created:").Append(strategy.State);

                    strategy.SetState(State.SetDefaults);
                    transitions.Append(" -> setdefaults:").Append(strategy.State);

                    StrategyAnalyzerAutomation.ApplySimpleXmlProperties(strategy, strategyElement);
                    StrategyAnalyzerAutomation.ApplyBarsPeriod(strategy, strategyElement);

                    // Iteration 3: the parity templates are SA fixed-backtest XMLs and
                    // carry Category=Backtest — but a Backtest-category strategy is
                    // hosted by RunBacktest(), not the realtime engine, which is the
                    // prime suspect for SetState(Active) being silently refused in
                    // iterations 1-2. A live enable is Category.NinjaScript (what the
                    // Strategies grid runs).
                    strategy.Category = Category.NinjaScript;
                    strategy.Workspace = NinjaTrader.Core.Globals.ActiveWorkspace;
                    strategy.SetUniqueId();
                    transitions.Append(" -> category:").Append(strategy.Category)
                               .Append(" ws:").Append(strategy.Workspace ?? "null");

                    // Iteration 4: NinjaScriptBase.Dispatcher { get; internal set; } is
                    // NULL on an Activator-created instance — NT's hosts assign one
                    // (Globals.RandomDispatcher spreads scripts over worker threads)
                    // before driving states. A null script dispatcher is the leading
                    // suspect for SetState(Active) silently no-opping in iterations
                    // 1-3. Assign it via reflection (internal setter), and hook the
                    // internal AfterSetState callback so the engine's own pump becomes
                    // observable in this log.
                    try
                    {
                        PropertyInfo dispProp = typeof(NinjaScriptBase).GetProperty("Dispatcher");
                        bool wasNull = dispProp != null && dispProp.GetValue(strategy) == null;
                        if (dispProp != null)
                            dispProp.GetSetMethod(true).Invoke(strategy, new object[] { NinjaTrader.Core.Globals.RandomDispatcher });
                        transitions.Append(" -> dispatcher:").Append(wasNull ? "assigned(wasNull)" : "assigned(wasSet)");

                        PropertyInfo afterProp = typeof(NinjaScriptBase).GetProperty(
                            "AfterSetState", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (afterProp != null)
                        {
                            StrategyBase captured = strategy;
                            Action hook = () => SafeLog(log, "EnableStrategy AfterSetState fired -> " + captured.State);
                            afterProp.GetSetMethod(true).Invoke(strategy, new object[] { hook });
                            transitions.Append(" afterHook:set");
                        }
                    }
                    catch (Exception ex)
                    {
                        transitions.Append(" -> dispatcher:EX(").Append(ex.Message).Append(")");
                    }

                    string instrumentName = strategy.InstrumentOrInstrumentList;
                    if (!string.IsNullOrWhiteSpace(instrumentName))
                    {
                        Instrument instrument = Instrument.GetInstrument(instrumentName, true);
                        if (instrument != null)
                            strategy.Instrument = instrument;
                        transitions.Append(" -> instrument:").Append(instrument != null ? instrument.FullName : "UNRESOLVED(" + instrumentName + ")");
                    }
                    strategy.Account = account;
                    transitions.Append(" -> account:").Append(account.Name);

                    strategy.SetState(State.Configure);
                    transitions.Append(" -> configure:").Append(strategy.State);
                }
                catch (Exception ex)
                {
                    walkError = ex;
                }
            });

            if (account == null)
            {
                WriteStatus(runId, "failed", "account not found: " + accountName, null);
                return;
            }

            // Iteration 5 probe: did OnStateChange(Configure) actually run? The
            // private doneConfigureState flag is the engine's own record of that.
            await onUi(() =>
            {
                try
                {
                    FieldInfo f = typeof(NinjaScriptBase).GetField("doneConfigureState",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    transitions.Append(" doneConfigure:")
                        .Append(f != null ? f.GetValue(strategy).ToString() : "field?");
                }
                catch (Exception ex) { transitions.Append(" doneConfigure:EX(").Append(ex.Message).Append(")"); }
            });

            // Paced Active attempts — iteration 5 runs them on the STRATEGY'S OWN
            // dispatcher (scripts are dispatcher-affine; iterations 1-4 used the main
            // UI dispatcher and were silently refused).
            for (int attempt = 1; attempt <= 3 && walkError == null; attempt++)
            {
                bool done = false;
                System.Windows.Threading.Dispatcher stratDisp = strategy.Dispatcher
                    ?? System.Windows.Application.Current.Dispatcher;
                await stratDisp.InvokeAsync(new Action(() =>
                {
                    try
                    {
                        if (strategy.State != State.Configure) { done = true; return; }
                        strategy.SetState(State.Active);
                        transitions.Append(" -> active").Append(attempt).Append("@own:").Append(strategy.State);
                    }
                    catch (Exception ex) { walkError = ex; }
                })).Task;
                if (done)
                    break;
                await Task.Delay(1000);
            }

            if (walkError != null)
            {
                SafeLog(log, "EnableStrategy state walk error: " + walkError.Message + "  [" + transitions + "]");
                WriteStatus(runId, "failed", walkError.Message, transitions.ToString());
                return;
            }
            SafeLog(log, "EnableStrategy walk: " + transitions);
            lock (Sync)
                ActiveStrategies[runId] = strategy;

            // Observe whether NT's engine pumps the remaining states itself
            // (DataLoaded -> Historical -> Transition -> Realtime).
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            State last = strategy.State;
            WriteStatus(runId, "running", null, transitions + " | observing:" + last);
            while (DateTime.UtcNow < deadline)
            {
                State now = strategy.State;
                if (now != last)
                {
                    transitions.Append(" => ").Append(now);
                    SafeLog(log, "EnableStrategy: state => " + now);
                    last = now;
                }
                if (now == State.Realtime)
                {
                    WriteStatus(runId, "finished", null, transitions.ToString());
                    SafeLog(log, "EnableStrategy: REALTIME reached. " + transitions);
                    return;
                }
                if (now == State.Terminated || now == State.Finalized)
                {
                    WriteStatus(runId, "failed", "strategy terminated during enable", transitions.ToString());
                    SafeLog(log, "EnableStrategy: terminated. " + transitions);
                    return;
                }
                WriteStatus(runId, "running", null, transitions + " | observing:" + now);
                await Task.Delay(1000);
            }
            // Timeout is still a RESULT for the experiment: report where the walk stalled.
            WriteStatus(runId, "failed", "enable timeout; stalled at state " + last, transitions.ToString());
            SafeLog(log, "EnableStrategy: timeout at " + last + ". " + transitions);
        }

        private static void DisableStrategy(string runId, Action<string> log)
        {
            StrategyBase strategy;
            lock (Sync)
                ActiveStrategies.TryGetValue(runId, out strategy);
            if (strategy == null)
            {
                WriteStatus(runId, "failed", "no active strategy for runId " + runId, null);
                return;
            }
            System.Windows.Application.Current.Dispatcher.InvokeAsync(new Action(() =>
            {
                try
                {
                    strategy.SetState(State.Terminated);
                    lock (Sync)
                        ActiveStrategies.Remove(runId);
                    SafeLog(log, "DisableStrategy: " + runId + " -> " + strategy.State);
                    WriteStatus(runId, "finished", null, "terminated:" + strategy.State);
                }
                catch (Exception ex)
                {
                    SafeLog(log, "DisableStrategy error: " + ex.Message);
                    WriteStatus(runId, "failed", ex.Message, null);
                }
            }));
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Status + JSON plumbing (house pattern: self-contained per service)
        // ─────────────────────────────────────────────────────────────────────

        private static void WriteStatus(string runId, string state, string lastError, string detail)
        {
            try
            {
                string json = "{"
                    + "\"runId\":\"" + JsonEscape(runId) + "\","
                    + "\"state\":\"" + JsonEscape(state) + "\","
                    + "\"lastError\":" + (lastError == null ? "null" : "\"" + JsonEscape(lastError) + "\"") + ","
                    + "\"detail\":" + (detail == null ? "null" : "\"" + JsonEscape(detail) + "\"") + ","
                    + "\"heartbeatUtc\":\"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\""
                    + "}";
                File.WriteAllText(StatusFilePath, json);
            }
            catch
            {
            }
        }

        private static string JsonEscape(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        private static void SafeLog(Action<string> log, string message)
        {
            try
            {
                if (log != null)
                    log(message);
            }
            catch
            {
            }
        }

        private static string ExtractJsonString(string json, string propertyName)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            Match m = Regex.Match(json,
                "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"",
                RegexOptions.IgnoreCase);
            if (!m.Success)
                return null;
            return Regex.Unescape(m.Groups[1].Value);
        }

        private static int ExtractJsonInt(string json, string propertyName, int fallback)
        {
            if (string.IsNullOrEmpty(json))
                return fallback;
            Match m = Regex.Match(json,
                "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*(-?\\d+)",
                RegexOptions.IgnoreCase);
            int value;
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return value;
            return fallback;
        }
    }
}
