# Optimizer Project Learning Log

Use this file for verified NinjaTrader optimizer and optimization fitness facts learned while building, compiling, and runtime testing.

Do not record guesses as facts. If something is inferred but not verified, label it as a hypothesis.

## 2026-05-11 - Project Split

Question:
- Can optimizer and optimization fitness code live in a separate DLL from the batch AddOn?

Evidence:
- `NinjaTraderOptimizerProject.sln` builds successfully with MSBuild.
- `NinjaTraderOptimizerProject.dll` and `.pdb` copy to `C:\Users\Owner\Documents\NinjaTrader 8\bin\Custom`.
- The main AddOn project no longer compiles the optimizer and fitness source files, avoiding duplicate NinjaScript class definitions.

Decision:
- Continue optimizer and fitness development in this standalone project.
- Do not add optimizer or optimization fitness compile entries back into `NinjaTraderAddOnProject.csproj`.

Follow-up:
- Verify the optimizer and fitness appear in NinjaTrader Strategy Analyzer selectors.
- Record any NinjaTrader runtime load errors here.

## 2026-05-13 - Random-search optimizer scaffold

Question:
- Which NinjaTrader optimizer and parameter APIs can this standalone project compile against for the first non-placeholder optimizer pass?

Evidence:
- Local docs pages confirm `OnOptimize()`, `RunIteration()`, `NumberOfIterations`, and `SupportsMultiObjectiveOptimization` are optimizer APIs.
- Reflection against `C:\Program Files\NinjaTrader 8\bin\NinjaTrader.Core.dll` showed `NinjaTrader.NinjaScript.Parameter` exposes `Min`, `Max`, `Increment`, `Value`, `ParameterType`, and `EnumValues`.
- MSBuild wrapper result: success with 0 warnings and 0 errors in `rag\build_logs\msbuild_20260513_064634.txt`.
- After adding drawdown filtering to the fitness, MSBuild wrapper result: success with 0 warnings and 0 errors in `rag\build_logs\msbuild_20260513_064654.txt`.

Decision:
- `CustomMultiObjectiveOptimizer` can set `SupportsMultiObjectiveOptimization = true`, set `NumberOfIterations`, assign randomized `Parameter.Value` values from `Min`/`Max`/`Increment`, call `RunIteration()`, and wait with `WaitForIterationsCompleted()`.
- `CustomMultiObjectiveFitness` can enforce `MinTrades`, use `TradesPerformance.Percent.Drawdown` for the configured max drawdown filter, and set `Value` from a finite profit-factor objective.

Follow-up:
- Runtime-test Strategy Analyzer selector/load behavior after stopping NinjaTrader and allowing the post-build DLL copy to complete.
- Do not implement Pareto/result harvesting until completed-result access is documented or runtime-proven; the RAG answer did not prove that contract.

## 2026-05-15 - Coverage-search optimizer compile and deploy

Question:
- Can the standalone optimizer move beyond pure random assignment while staying within the verified `Parameter.Value` + `RunIteration()` API contract?

Evidence:
- `CustomMultiObjectiveOptimizer` was updated to build discrete value spaces from `Parameter.Min`, `Parameter.Max`, `Parameter.Increment`, `ParameterType`, and `EnumValues`.
- The optimizer now assigns parameter sets with a Halton-style low-discrepancy coverage sequence, tracks queued parameter signatures, and logs duplicate counts.
- `RandomSeed=0` uses a time-based seed offset; nonzero `RandomSeed` gives a repeatable sequence.
- MSBuild wrapper result: success with 0 warnings and 0 errors in `rag\build_logs\msbuild_20260515_114727.txt`.
- `NinjaTraderOptimizerProject.dll` and `.pdb` copied to `C:\Users\Owner\Documents\NinjaTrader 8\bin\Custom`, and NinjaTrader was restarted after the copy.

Decision:
- The first productionized optimizer algorithm is coverage-oriented parameter sampling, not genetic/Pareto selection.
- Keep result ranking/export responsibility in NinjaTrader plus the TA Foundation review pipeline until completed-result access from `Optimizer` is runtime-proven.

Follow-up:
- Runtime-test a small custom optimizer template and confirm Output tab/file logs include `Starting coverage-search run`, per-parameter value-space counts, and final unique/duplicate parameter-set counts.
