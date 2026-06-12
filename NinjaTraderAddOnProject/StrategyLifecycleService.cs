using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

            if (Connection.PlaybackConnection != null)
            {
                SafeLog(log, "ConnectPlayback: playback connection already up (" + Connection.PlaybackConnection.Options.Name + ").");
                WriteStatus(runId, "finished", null, "already_connected:" + Connection.PlaybackConnection.Options.Name);
                return;
            }

            ConnectOptions options = NinjaTrader.Core.Globals.ConnectOptions
                .FirstOrDefault(o => !string.IsNullOrEmpty(requestedName)
                    ? string.Equals(o.Name, requestedName, StringComparison.OrdinalIgnoreCase)
                    : (o.Name ?? "").IndexOf("Playback", StringComparison.OrdinalIgnoreCase) >= 0);
            if (options == null)
            {
                string known = string.Join(" | ", NinjaTrader.Core.Globals.ConnectOptions.Select(o => o.Name));
                WriteStatus(runId, "failed", "no playback connection configured (known: " + known + ")", null);
                SafeLog(log, "ConnectPlayback: no matching ConnectOptions. Known: " + known);
                return;
            }

            SafeLog(log, "ConnectPlayback: connecting '" + options.Name + "'...");
            WriteStatus(runId, "running", null, "connecting:" + options.Name);
            Connection.Connect(options);

            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                Connection pb = Connection.PlaybackConnection;
                if (pb != null && pb.Status == ConnectionStatus.Connected)
                {
                    SafeLog(log, "ConnectPlayback: connected (" + pb.Options.Name + ").");
                    WriteStatus(runId, "finished", null, "connected:" + pb.Options.Name);
                    return;
                }
                WriteStatus(runId, "running", null, "waiting:" + (pb != null ? pb.Status.ToString() : "no_connection"));
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

            Account account = Account.All.FirstOrDefault(a => string.Equals(a.Name, accountName, StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                string known = string.Join(" | ", Account.All.Select(a => a.Name));
                WriteStatus(runId, "failed", "account not found: " + accountName + " (known: " + known + ")", null);
                return;
            }

            var transitions = new StringBuilder();
            StrategyBase strategy = null;
            Exception walkError = null;

            // Strategy configuration + state walk on the global UI dispatcher (NT's
            // own Strategies grid drives strategies from its UI thread).
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(new Action(() =>
            {
                try
                {
                    strategy = (StrategyBase)Activator.CreateInstance(strategyType);
                    transitions.Append("created:").Append(strategy.State);

                    strategy.SetState(State.SetDefaults);
                    transitions.Append(" -> setdefaults:").Append(strategy.State);

                    StrategyAnalyzerAutomation.ApplySimpleXmlProperties(strategy, strategyElement);
                    StrategyAnalyzerAutomation.ApplyBarsPeriod(strategy, strategyElement);

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

                    strategy.SetState(State.Active);
                    transitions.Append(" -> active:").Append(strategy.State);

                    // Iteration 2 (first run stalled at Configure; SetState(Active)
                    // was silently refused): register with the account's strategy
                    // collection — the binding NT's own Strategies grid maintains —
                    // then retry the forward states, escalating one at a time. Every
                    // attempt is logged; whichever rung moves the state is the answer
                    // the experiment exists to find.
                    if (strategy.State == State.Configure)
                    {
                        try
                        {
                            if (!account.Strategies.Contains(strategy))
                                account.Strategies.Add(strategy);
                            transitions.Append(" -> acctAdd:").Append(strategy.State);
                        }
                        catch (Exception ex)
                        {
                            transitions.Append(" -> acctAdd:EX(").Append(ex.Message).Append(")");
                        }

                        strategy.SetState(State.Active);
                        transitions.Append(" -> active2:").Append(strategy.State);
                    }
                    if (strategy.State == State.Configure)
                    {
                        strategy.SetState(State.DataLoaded);
                        transitions.Append(" -> dataloaded:").Append(strategy.State);
                    }
                }
                catch (Exception ex)
                {
                    walkError = ex;
                }
            }));

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
