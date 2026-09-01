using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NinjaTraderAddOnProject
{
    internal static class CompileObserverService
    {
        private const string StatusFilePath = @"C:\temp\nt8_status.json";

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

        public static bool IsObserveCompileCommand(string json)
        {
            string action = ExtractJsonString(json, "action");
            return action != null && action.Equals("ObserveCompile", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task ObserveCompile(string json, Action<string> log)
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
            SafeLog(log, "ObserveCompile started for " + (strategyName ?? sourceFile ?? "(unknown strategy)") + ".");
            WriteCompileStatus(runId, "starting", strategyName, sourceFile, outputDir, false, 0, null, null, null, null, log);

            DateTime sourceWriteTime = File.Exists(sourceFile) ? File.GetLastWriteTime(sourceFile) : DateTime.Now;
            DateTime observeFrom = sourceWriteTime.AddMinutes(-2);
            DateTime deadline = DateTime.Now.AddSeconds(timeoutSeconds);
            List<CompileErrorRecord> errors = new List<CompileErrorRecord>();
            string customAssemblyPath = ResolveCustomAssemblyPath(sourceFile);

            while (DateTime.Now < deadline)
            {
                WriteCompileStatus(runId, "waiting_for_auto_compile", strategyName, sourceFile, outputDir, false, 0, null, null, null, null, log);
                await Task.Delay(TimeSpan.FromSeconds(quietSeconds));
                WriteCompileStatus(runId, "observing", strategyName, sourceFile, outputDir, false, 0, null, null, null, null, log);
                await TriggerEditorCompile();

                errors = FindRecentCompileErrors(strategyName, sourceFile, observeFrom);
                if (errors.Count > 0)
                {
                    // Owned vs. peer errors both prove the NinjaScript program
                    // is in a non-compiling state. SA refuses to run either way.
                    CompileErrorRecord ownedSample = errors.FirstOrDefault(e => string.IsNullOrEmpty(e.Source) || !e.Source.EndsWith("[peer]", StringComparison.Ordinal));
                    CompileErrorRecord peerSample = errors.FirstOrDefault(e => !string.IsNullOrEmpty(e.Source) && e.Source.EndsWith("[peer]", StringComparison.Ordinal));
                    bool anyOwned = ownedSample != null;
                    string blockReason = anyOwned ? "strategy_compile_errors" : "peer_strategy_errors";
                    string lastError = anyOwned
                        ? ownedSample.Message
                        : ("peer strategy compile error blocking SA: " + (peerSample != null ? peerSample.Message : errors[0].Message));

                    string csvPath = Path.Combine(outputDir, runId + "_errors.csv");
                    string textPath = Path.Combine(outputDir, runId + "_errors.txt");
                    string resultPath = Path.Combine(outputDir, runId + "_compile_result.json");
                    WriteCompileErrorsCsv(csvPath, errors);
                    WriteCompileErrorsText(textPath, errors);
                    WriteCompileResultJson(resultPath, runId, "failed", strategyName, sourceFile, outputDir, errors, csvPath, textPath, lastError, blockReason);
                    WriteCompileStatus(runId, "failed", strategyName, sourceFile, outputDir, false, errors.Count, csvPath, textPath, lastError, blockReason, log);
                    SafeLog(log, "ObserveCompile failed for " + strategyName + " (" + blockReason + ") with " + errors.Count.ToString(CultureInfo.InvariantCulture) + " observed compiler errors.");
                    return;
                }

                if (IsStrategyTypeVisible(strategyName))
                {
                    // Type-visible alone is a stale signal — the previous
                    // assembly stays loaded if the new auto-compile didn't
                    // produce a fresh DLL. Confirm the custom assembly was
                    // rewritten after the .cs install before declaring clean.
                    if (!IsCustomAssemblyFresh(customAssemblyPath, sourceWriteTime))
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    string csvPath = Path.Combine(outputDir, runId + "_errors.csv");
                    string textPath = Path.Combine(outputDir, runId + "_errors.txt");
                    string resultPath = Path.Combine(outputDir, runId + "_compile_result.json");
                    WriteCompileErrorsCsv(csvPath, errors);
                    WriteCompileErrorsText(textPath, errors);
                    WriteCompileResultJson(resultPath, runId, "succeeded", strategyName, sourceFile, outputDir, errors, csvPath, textPath, null, null);
                    WriteCompileStatus(runId, "succeeded", strategyName, sourceFile, outputDir, true, 0, csvPath, textPath, null, null, log);
                    SafeLog(log, "ObserveCompile succeeded for " + strategyName + ".");
                    return;
                }

                await Task.Delay(1000);
            }

            string timeoutCsv = Path.Combine(outputDir, runId + "_errors.csv");
            string timeoutText = Path.Combine(outputDir, runId + "_errors.txt");
            string timeoutResult = Path.Combine(outputDir, runId + "_compile_result.json");
            bool assemblyStale = IsStrategyTypeVisible(strategyName) && !IsCustomAssemblyFresh(customAssemblyPath, sourceWriteTime);
            string timeoutReason = assemblyStale ? "stale_assembly" : null;
            string message = assemblyStale
                ? "NinjaTrader.Custom.dll was not rewritten after the .cs install; auto-compile likely failed silently."
                : "No matching compiler errors were observed, but the strategy type is not visible in loaded NinjaTrader assemblies.";
            WriteCompileErrorsCsv(timeoutCsv, errors);
            WriteCompileErrorsText(timeoutText, errors);
            WriteCompileResultJson(timeoutResult, runId, "timed_out", strategyName, sourceFile, outputDir, errors, timeoutCsv, timeoutText, message, timeoutReason);
            WriteCompileStatus(runId, "timed_out", strategyName, sourceFile, outputDir, false, errors.Count, timeoutCsv, timeoutText, message, timeoutReason, log);
            SafeLog(log, "ObserveCompile timed out for " + strategyName + ": " + message);
        }

        private static string ResolveCustomAssemblyPath(string sourceFile)
        {
            // Strategy sources live at <NT8>\bin\Custom\Strategies\<name>.cs.
            // The compiled assembly is at <NT8>\bin\Custom\NinjaTrader.Custom.dll.
            try
            {
                if (!string.IsNullOrWhiteSpace(sourceFile))
                {
                    string strategies = Path.GetDirectoryName(sourceFile);
                    if (!string.IsNullOrEmpty(strategies))
                    {
                        string customDir = Path.GetDirectoryName(strategies);
                        if (!string.IsNullOrEmpty(customDir))
                            return Path.Combine(customDir, "NinjaTrader.Custom.dll");
                    }
                }
            }
            catch { }
            string documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8");
            return Path.Combine(documents, "bin", "Custom", "NinjaTrader.Custom.dll");
        }

        private static bool IsCustomAssemblyFresh(string assemblyPath, DateTime sourceWriteTime)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
                    return false;
                // Allow a small leeway so clock-skew between source write and
                // DLL touch doesn't bounce us forever.
                DateTime dllWrite = File.GetLastWriteTime(assemblyPath);
                return dllWrite >= sourceWriteTime.AddSeconds(-2);
            }
            catch { return false; }
        }

        private static void WriteCompileStatus(string runId, string state, string strategyName, string sourceFile, string outputDir, bool compiled, int errorCount, string errorsCsv, string errorsText, string lastError, string compileBlockReason, Action<string> log)
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
                if (!string.IsNullOrWhiteSpace(compileBlockReason))
                    sb.Append("\"compileBlockReason\":").Append(EncodeJsonString(compileBlockReason)).Append(",");
                sb.Append("\"heartbeatUtc\":").Append(EncodeJsonString(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture))).Append(",");
                sb.Append("\"outputRoot\":").Append(EncodeJsonString(outputDir));
                sb.Append("}");
                AtomicWriteText(StatusFilePath, sb.ToString());
            }
            catch (Exception ex)
            {
                SafeLog(log, "WriteCompileStatus error: " + ex.Message);
            }
        }

        private static bool IsStrategyTypeVisible(string strategyName)
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

        private static async Task TriggerEditorCompile()
        {
            try
            {
                System.Windows.Application app = System.Windows.Application.Current;
                if (app == null || app.Dispatcher == null)
                    return;

                List<Task> tasks = new List<Task>();
                app.Dispatcher.Invoke(new Action(() =>
                {
                    Type viewModelType = FindType("NinjaTrader.Gui.NinjaScript.Editor.EditorViewModel");
                    if (viewModelType == null)
                        return;
                    PropertyInfo instancesProperty = viewModelType.GetProperty("Instances", BindingFlags.Public | BindingFlags.Static);
                    object instances = instancesProperty != null ? instancesProperty.GetValue(null, null) : null;
                    System.Collections.IEnumerable enumerableInstances = instances as System.Collections.IEnumerable;
                    if (enumerableInstances == null)
                        return;

                    foreach (object instance in enumerableInstances)
                    {
                        if (instance == null)
                            continue;
                        MethodInfo onCompile = instance.GetType().GetMethod("OnCompile", BindingFlags.Public | BindingFlags.Instance);
                        if (onCompile == null)
                            continue;
                        object result = onCompile.Invoke(instance, new object[] { false });
                        Task task = result as Task;
                        if (task != null)
                            tasks.Add(task);
                    }
                }));

                foreach (Task task in tasks)
                    await task;
            }
            catch { }
        }

        private static List<CompileErrorRecord> FindRecentCompileErrors(string strategyName, string sourceFile, DateTime observeFrom)
        {
            // The editor's CompileErrors collection is NT's authoritative source —
            // it's what populates the SA "programming errors must be resolved"
            // modal. A previous version of this method also called
            // NinjaTrader.Code.LegacyCompiler.Compile via reflection, but that
            // overload defaults to an older C# language version and emits 3000+
            // false positives against NT's own bin\Custom files (e.g.
            // @RegressionChannel.cs, @LineBreakBarsType.cs) that use modern
            // syntax. Trust the editor; fall back to the trace/log scan below
            // when the editor isn't open.
            List<CompileErrorRecord> records = FindEditorCompileErrors(strategyName, sourceFile);
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

        private static List<CompileErrorRecord> FindEditorCompileErrors(string strategyName, string sourceFile)
        {
            List<CompileErrorRecord> records = new List<CompileErrorRecord>();
            try
            {
                System.Windows.Application app = System.Windows.Application.Current;
                if (app == null || app.Dispatcher == null)
                    return records;

                app.Dispatcher.Invoke(new Action(() =>
                {
                    Type viewModelType = FindType("NinjaTrader.Gui.NinjaScript.Editor.EditorViewModel");
                    if (viewModelType == null)
                        return;

                    PropertyInfo instancesProperty = viewModelType.GetProperty("Instances", BindingFlags.Public | BindingFlags.Static);
                    object instances = instancesProperty != null ? instancesProperty.GetValue(null, null) : null;
                    System.Collections.IEnumerable enumerableInstances = instances as System.Collections.IEnumerable;
                    if (enumerableInstances == null)
                        return;

                    foreach (object instance in enumerableInstances)
                    {
                        if (instance == null)
                            continue;
                        PropertyInfo compileErrorsProperty = instance.GetType().GetProperty("CompileErrors", BindingFlags.Public | BindingFlags.Instance);
                        object compileErrors = compileErrorsProperty != null ? compileErrorsProperty.GetValue(instance, null) : null;
                        System.Collections.IEnumerable enumerableErrors = compileErrors as System.Collections.IEnumerable;
                        if (enumerableErrors == null)
                            continue;

                        foreach (object error in enumerableErrors)
                        {
                            if (error == null)
                                continue;
                            string fullPath = GetStringProperty(error, "FullPath");
                            string shortName = GetStringProperty(error, "ShortName");
                            string message = GetStringProperty(error, "Error");
                            string code = GetStringProperty(error, "ErrorCode");
                            bool isWarning = GetBoolProperty(error, "IsWarning");
                            if (isWarning)
                                continue;

                            // Do NOT filter by strategy name here. Strategy
                            // Analyzer compiles the entire NinjaScript program
                            // and refuses to run if *any* peer strategy in
                            // bin\Custom\Strategies has a compile error, even
                            // when our target type is still visible from a
                            // previous successful load. Surfacing peer errors
                            // is what lets the repair loop see the real block.
                            string fileName = string.IsNullOrWhiteSpace(sourceFile) ? "" : Path.GetFileName(sourceFile);
                            string strategy = strategyName ?? "";
                            bool isOwnedByRequestedStrategy =
                                string.IsNullOrWhiteSpace(strategy)
                                || string.Equals(shortName, fileName, StringComparison.OrdinalIgnoreCase)
                                || (!string.IsNullOrWhiteSpace(fullPath) && fullPath.IndexOf(strategy, StringComparison.OrdinalIgnoreCase) >= 0)
                                || (!string.IsNullOrWhiteSpace(message) && message.IndexOf(strategy, StringComparison.OrdinalIgnoreCase) >= 0);

                            records.Add(new CompileErrorRecord
                            {
                                File = string.IsNullOrWhiteSpace(shortName) ? fileName : shortName,
                                Line = GetIntProperty(error, "Line"),
                                Column = GetIntProperty(error, "Column"),
                                Code = code ?? "",
                                Message = message ?? "",
                                Raw = (shortName ?? fullPath ?? fileName) + " " + (code ?? "") + ": " + (message ?? ""),
                                Source = isOwnedByRequestedStrategy ? "NinjaScriptEditor.CompileErrors" : "NinjaScriptEditor.CompileErrors[peer]"
                            });
                        }
                    }
                }));
            }
            catch { }
            return records;
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch { }
            }
            return null;
        }

        private static List<CompileErrorRecord> ParseCompileErrorsFromText(string sourcePath, string text, string strategyName, string sourceFile)
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

        private static string ReadTail(string path, int maxBytes)
        {
            FileInfo info = new FileInfo(path);
            using (FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                long start = Math.Max(0, info.Length - maxBytes);
                stream.Seek(start, SeekOrigin.Begin);
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd();
            }
        }

        private static void WriteCompileErrorsCsv(string path, List<CompileErrorRecord> errors)
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

        private static void WriteCompileErrorsText(string path, List<CompileErrorRecord> errors)
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

        private static void WriteCompileResultJson(string path, string runId, string state, string strategyName, string sourceFile, string outputDir, List<CompileErrorRecord> errors, string csvPath, string textPath, string lastError, string compileBlockReason)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"schemaVersion\":2,");
            sb.Append("\"runId\":").Append(EncodeJsonString(runId)).Append(",");
            sb.Append("\"state\":").Append(EncodeJsonString(state)).Append(",");
            sb.Append("\"strategyName\":").Append(EncodeJsonString(strategyName)).Append(",");
            sb.Append("\"sourceFile\":").Append(EncodeJsonString(sourceFile)).Append(",");
            sb.Append("\"compiled\":").Append(state == "succeeded" ? "true" : "false").Append(",");
            sb.Append("\"errorCount\":").Append(errors.Count.ToString(CultureInfo.InvariantCulture)).Append(",");
            sb.Append("\"errorsCsv\":").Append(EncodeJsonString(csvPath)).Append(",");
            sb.Append("\"errorsText\":").Append(EncodeJsonString(textPath)).Append(",");
            sb.Append("\"lastError\":").Append(EncodeJsonString(lastError)).Append(",");
            if (!string.IsNullOrWhiteSpace(compileBlockReason))
                sb.Append("\"compileBlockReason\":").Append(EncodeJsonString(compileBlockReason)).Append(",");
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

        private static string GetStringProperty(object instance, string propertyName)
        {
            try
            {
                PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                object value = property != null ? property.GetValue(instance, null) : null;
                return value == null ? null : value.ToString();
            }
            catch { return null; }
        }

        private static bool GetBoolProperty(object instance, string propertyName)
        {
            try
            {
                PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                object value = property != null ? property.GetValue(instance, null) : null;
                return value is bool && (bool)value;
            }
            catch { return false; }
        }

        private static int? GetIntProperty(object instance, string propertyName)
        {
            try
            {
                PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                object value = property != null ? property.GetValue(instance, null) : null;
                if (value is int)
                    return (int)value;
                int parsed;
                return value != null && int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? (int?)parsed : null;
            }
            catch { return null; }
        }

        private static string ExtractJsonString(string json, string propertyName)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups["value"].Value.Replace("\\\\", "\\").Replace("\\\"", "\"") : null;
        }

        private static int ExtractJsonInt(string json, string propertyName, int defaultValue)
        {
            Match match = Regex.Match(json, "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*(?<value>-?\\d+)", RegexOptions.IgnoreCase);
            if (!match.Success)
                return defaultValue;
            int value;
            return int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : defaultValue;
        }

        private static int? NullableInt(string value)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return null;
        }

        private static void AtomicWriteText(string path, string text)
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

        private static string EncodeJsonString(string s)
        {
            if (s == null) return "null";
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
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

        private static string EscapeCsv(string value)
        {
            if (value == null) return string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static void SafeLog(Action<string> log, string message)
        {
            try { log?.Invoke(message); } catch { }
        }
    }
}
