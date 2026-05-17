#region Using declarations
using NinjaTrader.Core;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.OptimizationFitnesses;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
#endregion

namespace NinjaTrader.NinjaScript.Optimizers
{
    public class CustomMultiObjectiveOptimizer : Optimizer
    {
        private readonly HashSet<string> queuedParameterSets = new HashSet<string>();
        private int duplicateParameterSetCount;
        private int randomSeedUsed;

        private static readonly int[] HaltonPrimes = new int[]
        {
            2, 3, 5, 7, 11, 13, 17, 19, 23, 29,
            31, 37, 41, 43, 47, 53, 59, 61, 67, 71,
            73, 79, 83, 89, 97, 101, 103, 107, 109, 113
        };

        [NinjaScriptProperty]
        [Display(Name = "Population Size", Description = "Number of individuals in each generation", Order = 1, GroupName = "Evolution")]
        public int PopulationSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Generations", Description = "Number of generations to run", Order = 2, GroupName = "Evolution")]
        public int Generations { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Random Seed", Description = "Optional deterministic seed. Use 0 for a time-based seed.", Order = 3, GroupName = "Evolution")]
        public int RandomSeed { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Duplicate Retry Limit", Description = "Attempts to find an unqueued parameter set before accepting a duplicate.", Order = 4, GroupName = "Evolution")]
        public int DuplicateRetryLimit { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "CustomMultiObjectiveOptimizer";
                PopulationSize = 50;
                Generations = 10;
                RandomSeed = 0;
                DuplicateRetryLimit = 25;
                SupportsMultiObjectiveOptimization = true;
            }
            else if (State == State.Configure)
            {
                PopulationSize = Math.Max(1, PopulationSize);
                Generations = Math.Max(1, Generations);
                DuplicateRetryLimit = Math.Max(0, DuplicateRetryLimit);
                NumberOfIterations = PopulationSize * Generations;
                randomSeedUsed = RandomSeed == 0 ? Environment.TickCount : RandomSeed;
            }
        }

        protected override void OnOptimize()
        {
            if (Strategies == null || Strategies.Count == 0 || Strategies[0] == null)
            {
                Log("No strategy instance was provided; optimizer stopped.");
                return;
            }

            if (Strategies[0].OptimizationParameters == null || Strategies[0].OptimizationParameters.Count == 0)
            {
                Log("No optimization parameters were provided; optimizer stopped.");
                return;
            }

            List<ParameterValueSpace> valueSpaces = BuildParameterValueSpaces();
            queuedParameterSets.Clear();
            duplicateParameterSetCount = 0;

            long totalUniqueCombos = ComputeUniqueComboCount(valueSpaces);

            Log("Starting coverage-search run: PopulationSize=" + PopulationSize
                + ", Generations=" + Generations
                + ", NumberOfIterations=" + NumberOfIterations
                + ", OptimizationParameters=" + Strategies[0].OptimizationParameters.Count
                + ", TotalUniqueCombos=" + totalUniqueCombos
                + ", RandomSeedUsed=" + randomSeedUsed + ".");

            LogParameterValueSpaces(valueSpaces);

            // Short-circuit: when the parameter space is fully enumerable
            // within the configured budget, run each combination exactly
            // once. This avoids wasting iterations on duplicate parameter
            // sets and prevents NT's KeepBestResults from over-representing
            // a few best-scoring combos at the expense of full coverage.
            //
            // The 0 < totalUniqueCombos check guards against the case where
            // every parameter has zero candidates (we fall back to the
            // sampling path so RunIteration is still called once).
            if (totalUniqueCombos > 0 && totalUniqueCombos <= NumberOfIterations)
            {
                RunExhaustive(valueSpaces, totalUniqueCombos);
            }
            else
            {
                RunCoverageSampling(valueSpaces);
            }

            Log((IsAborted ? "Custom optimizer aborted." : "Custom optimizer completed.")
                + " UniqueParameterSets=" + queuedParameterSets.Count
                + ", DuplicateParameterSets=" + duplicateParameterSetCount + ".");
        }

        private void RunCoverageSampling(List<ParameterValueSpace> valueSpaces)
        {
            for (int gen = 0; gen < Generations; gen++)
            {
                if (IsAborted)
                    break;

                Log("Generation " + (gen + 1) + " of " + Generations + " started.");

                for (int individual = 0; individual < PopulationSize; individual++)
                {
                    if (IsAborted)
                        break;

                    int iterationIndex = gen * PopulationSize + individual;
                    AssignCoverageSample(valueSpaces, iterationIndex);
                    RunIteration();
                }

                if (!IsAborted)
                    WaitForIterationsCompleted();

                Log("Generation " + (gen + 1) + " of " + Generations + " completed.");
            }
        }

        private void RunExhaustive(List<ParameterValueSpace> valueSpaces, long totalUniqueCombos)
        {
            Log("Exhaustive mode: enumerating " + totalUniqueCombos + " unique combinations (NumberOfIterations="
                + NumberOfIterations + ").");

            int[] cursor = new int[valueSpaces.Count];
            int dispatched = 0;

            while (!IsAborted)
            {
                for (int parameterIndex = 0; parameterIndex < valueSpaces.Count; parameterIndex++)
                {
                    ParameterValueSpace valueSpace = valueSpaces[parameterIndex];
                    if (valueSpace.Values.Count == 0)
                        continue;
                    valueSpace.Parameter.Value = valueSpace.Values[cursor[parameterIndex]];
                }

                string signature = BuildParameterSignature(valueSpaces);
                queuedParameterSets.Add(signature);
                RunIteration();
                dispatched++;

                // Advance the cursor (least-significant first).
                int carry = 1;
                for (int i = 0; i < cursor.Length && carry > 0; i++)
                {
                    if (valueSpaces[i].Values.Count == 0)
                        continue;
                    cursor[i] += 1;
                    if (cursor[i] < valueSpaces[i].Values.Count)
                    {
                        carry = 0;
                    }
                    else
                    {
                        cursor[i] = 0;
                    }
                }
                if (carry > 0)
                    break;
            }

            if (!IsAborted)
                WaitForIterationsCompleted();

            Log("Exhaustive mode dispatched " + dispatched + " iterations.");
        }

        private static long ComputeUniqueComboCount(List<ParameterValueSpace> valueSpaces)
        {
            long total = 1;
            foreach (ParameterValueSpace valueSpace in valueSpaces)
            {
                int count = valueSpace.Values.Count;
                if (count <= 0)
                    continue;
                // Guard against overflow for huge spaces — once we cross
                // int.MaxValue there's no chance of fitting in NumberOfIterations.
                if (total > int.MaxValue / Math.Max(1, count))
                    return long.MaxValue;
                total *= count;
            }
            return total;
        }

        private List<ParameterValueSpace> BuildParameterValueSpaces()
        {
            List<ParameterValueSpace> valueSpaces = new List<ParameterValueSpace>();

            foreach (Parameter parameter in Strategies[0].OptimizationParameters)
                valueSpaces.Add(new ParameterValueSpace(parameter, GetParameterValues(parameter)));

            return valueSpaces;
        }

        private void AssignCoverageSample(List<ParameterValueSpace> valueSpaces, int iterationIndex)
        {
            if (valueSpaces == null || valueSpaces.Count == 0)
                return;

            for (int attempt = 0; attempt <= DuplicateRetryLimit; attempt++)
            {
                for (int parameterIndex = 0; parameterIndex < valueSpaces.Count; parameterIndex++)
                {
                    ParameterValueSpace valueSpace = valueSpaces[parameterIndex];
                    valueSpace.Parameter.Value = GetCoverageValue(valueSpace, iterationIndex + attempt, parameterIndex);
                }

                string signature = BuildParameterSignature(valueSpaces);
                if (queuedParameterSets.Add(signature))
                    return;
            }

            duplicateParameterSetCount++;
        }

        private object GetCoverageValue(ParameterValueSpace valueSpace, int iterationIndex, int parameterIndex)
        {
            if (valueSpace.Values.Count == 0)
                return valueSpace.Parameter.Value;

            if (valueSpace.Values.Count == 1)
                return valueSpace.Values[0];

            int prime = HaltonPrimes[parameterIndex % HaltonPrimes.Length];
            int seedOffset = Math.Abs(randomSeedUsed % 7919);
            double unit = RadicalInverse(iterationIndex + seedOffset + 1, prime);
            int selectedIndex = Math.Min(valueSpace.Values.Count - 1, (int)Math.Floor(unit * valueSpace.Values.Count));
            return valueSpace.Values[selectedIndex];
        }

        private List<object> GetParameterValues(Parameter parameter)
        {
            List<object> values = new List<object>();

            if (parameter == null)
                return values;

            Type parameterType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

            if (parameterType == typeof(int))
            {
                int min = Convert.ToInt32(parameter.Min);
                int max = Convert.ToInt32(parameter.Max);
                int increment = Math.Max(1, Convert.ToInt32(parameter.Increment));
                for (int value = min; value <= max; value += increment)
                    values.Add(value);
                return values;
            }

            if (parameterType == typeof(double))
            {
                double min = Convert.ToDouble(parameter.Min);
                double max = Convert.ToDouble(parameter.Max);
                double increment = Convert.ToDouble(parameter.Increment);

                if (increment <= 0)
                {
                    values.Add(min);
                    values.Add(min + (max - min) * 0.25);
                    values.Add(min + (max - min) * 0.5);
                    values.Add(min + (max - min) * 0.75);
                    values.Add(max);
                    return values;
                }

                int steps = Math.Max(0, (int)Math.Floor((max - min) / increment));
                for (int step = 0; step <= steps; step++)
                    values.Add(min + step * increment);
                return values;
            }

            if (parameterType == typeof(bool))
            {
                bool min = Convert.ToBoolean(parameter.Min);
                bool max = Convert.ToBoolean(parameter.Max);
                values.Add(min);
                if (min != max)
                    values.Add(max);
                return values;
            }

            if (parameterType != null && parameterType.IsEnum)
            {
                object[] enumValues = parameter.EnumValues;

                if (enumValues == null || enumValues.Length == 0)
                    enumValues = Enum.GetValues(parameterType).Cast<object>().ToArray();

                values.AddRange(enumValues);
                return values;
            }

            values.Add(parameter.Value);
            return values;
        }

        private double RadicalInverse(int index, int numberBase)
        {
            double result = 0;
            double fraction = 1.0 / numberBase;
            int value = index;

            while (value > 0)
            {
                result += fraction * (value % numberBase);
                value /= numberBase;
                fraction /= numberBase;
            }

            return result;
        }

        private string BuildParameterSignature(List<ParameterValueSpace> valueSpaces)
        {
            return string.Join("|", valueSpaces.Select(valueSpace => valueSpace.Parameter.Name + "=" + Convert.ToString(valueSpace.Parameter.Value)).ToArray());
        }

        private void LogParameterValueSpaces(List<ParameterValueSpace> valueSpaces)
        {
            foreach (ParameterValueSpace valueSpace in valueSpaces)
                Log("Parameter space: " + valueSpace.Parameter.Name + " has " + valueSpace.Values.Count + " candidate values.");
        }

        private void Log(string message)
        {
            string line = "CustomMultiObjectiveOptimizer: " + message;
            NinjaTrader.Code.Output.Process(line, PrintTo.OutputTab1);

            try
            {
                string logPath = Path.Combine(Path.GetTempPath(), "nt8_custom_optimizer.log");
                File.AppendAllText(logPath, DateTime.Now.ToString("O") + " " + line + Environment.NewLine);
            }
            catch
            {
                // Output tab logging is the primary signal; file logging is best-effort diagnostics.
            }
        }

        private class ParameterValueSpace
        {
            public ParameterValueSpace(Parameter parameter, List<object> values)
            {
                Parameter = parameter;
                Values = values ?? new List<object>();
            }

            public Parameter Parameter { get; private set; }
            public List<object> Values { get; private set; }
        }
    }
}
