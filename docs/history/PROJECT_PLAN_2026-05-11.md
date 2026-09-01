# NinjaTrader 8.1.6.3 Batch Automation AddOn: Project Plan

Last updated: 2026-05-11

## 1. Project Goal

This project extends NinjaTrader 8.1.6.3 with a Strategy Analyzer batch automation AddOn that can run many saved templates and export structured results without manually loading each template.

The custom optimizer and optimization fitness work has been split into a separate sibling project:

- `NinjaTraderOptimizerProject/`

Keep optimizer and fitness code in that project so NinjaTrader does not load duplicate NinjaScript optimizer classes from multiple DLLs.

## 2. Current Milestones

### Milestone A: Buildable Visual Studio AddOn Project

Status: Complete

The repository contains a Visual Studio/.NET Framework 4.8 AddOn project that references NinjaTrader 8.1.6.3 assemblies and builds with MSBuild.

Primary solution:
- `NinjaTraderAddOnProject.sln`

Primary project:
- `NinjaTraderAddOnProject/NinjaTraderAddOnProject.csproj`

Build command:

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe" D:\ninjatraderOptimizer\NinjaTraderAddOnProject.sln /p:Configuration=Debug /t:Rebuild
```

### Milestone B: Separate Optimizer/Fitness Project

Status: Split out to standalone project

Location:
- `NinjaTraderOptimizerProject/`

Purpose:
- Own custom optimizer and optimization fitness components outside the batch AddOn DLL.
- Allow optimizer work to evolve independently from Strategy Analyzer batch automation.

Future work:
- Continue optimizer implementation in `NinjaTraderOptimizerProject/PROJECT_PLAN.md`.
- Do not add optimizer or optimization fitness compile entries back into `NinjaTraderAddOnProject.csproj`.

### Milestone C: Batch Strategy Analyzer Automation AddOn

Status: Working and user-tested

Files:
- `NinjaTraderAddOnProject/AddOnFramework.cs`
- `NinjaTraderAddOnProject/BatchControl.cs`
- `NinjaTraderAddOnProject/StrategyAnalyzerAutomation.cs`

Purpose:
- Eventually replace or supplement NinjaTrader's built-in optimizer with a custom search strategy.

Future algorithm options:
- NSGA-II for Pareto multi-objective optimization.
- Bayesian optimization.
- Custom genetic algorithm tuned for NinjaTrader parameter spaces.

Current capabilities:
- Injects a batch panel into Strategy Analyzer Settings.
- Finds `.xml` strategy templates in a selected folder.
- Optionally overrides each template's in-memory `<From>` and `<To>` dates for a rolling backtest window.
- Opens a temporary Analyzer tab per template.
- Loads each template through reflection and direct internal field assignment.
- Preserves the original selected instrument.
- Runs each backtest.
- Waits for results to appear and stabilize.
- Exports six CSV files per template.
- Closes temporary tabs after export.
- Supports a `CANCEL` button.
- Supports file-based IPC trigger at `C:\temp\nt8_command.json`.

Current output files:
- `BatchRunSummary.csv` at the destination root
  - Contains backtest start/end dates and separate batch run start/end timestamps.
- `Settings.csv`
- `Summary.csv`
- `Analysis.csv`
- `Trades.csv`
- `Orders.csv`
- `Executions.csv`

## 3. Architecture Notes

### Why Reflection Is Used

NinjaTrader does not expose a supported public API for all Strategy Analyzer template loading, tab creation, run control, and result extraction needed here.

The AddOn reflects into:
- `NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzer`
- `NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzerViewModel`
- `NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzerTabControl`
- `NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzerTabProperties`

The reflection code is centralized in:
- `NinjaTraderAddOnProject/StrategyAnalyzerAutomation.cs`

### Important Runtime Discoveries

These discoveries matter for future development:

- `StrategyAnalyzerTabControl.Restore(XElement)` looked promising but did not reliably load templates in NinjaTrader 8.1.6.3.
- Several public setters or methods appear to be stubs/no-ops for this use case.
- Direct backing-field assignment was required for reliable template loading.
- Native `NTGrid` CSV export caused duplicate and incorrect files because it exported unrelated visible grids.
- Correct result data is available from Strategy Analyzer result objects after the run completes or stabilizes.

### Threading Rule

All Strategy Analyzer UI and reflection operations must happen on the Strategy Analyzer dispatcher.

Do not use arbitrary background threads for UI object access.

`StrategyAnalyzerAutomation` has dispatcher helpers for this.

## 4. Batch Workflow

Current batch workflow:

```text
User opens Strategy Analyzer
User selects strategy, instrument, and desired Analyzer settings
User enables Batch mode
User selects template source folder and output folder
Optional: User enables rolling date range and enters a day count
Optional: User disables tab cleanup or output overwrite
User clicks RUN BATCH BACKTEST
AddOn validates that an instrument is selected before creating batch tabs
For each XML template:
    create temporary Analyzer tab
    optionally set template From/To to today minus N days through today
    load template strategy and parameters
    restore original instrument
    run Strategy Analyzer
    wait for results
    export CSV files
    close temporary tab
Batch completed
```

Cancel workflow:

```text
User clicks CANCEL
cancelRequested = true
active temporary tab is asked to close
no additional templates start
batch logs Batch cancelled
```

## 5. Testing Plan

### Build Test

Run MSBuild and require:
- `Build succeeded.`
- `0 Warning(s)`
- `0 Error(s)`

### Deployment Test

Stop NinjaTrader, copy:
- `NinjaTraderAddOnProject.dll`
- `NinjaTraderAddOnProject.pdb`

Destination:
- `C:\Users\Owner\Documents\NinjaTrader 8\bin\Custom\`

Restart NinjaTrader.

### Runtime Smoke Test

1. Open Strategy Analyzer.
2. Confirm the `BATCH STRATEGY ANALYZER` panel appears.
3. Enable Batch mode.
4. Select a small folder with 1-3 templates.
5. Run batch.
6. Confirm logs show template load, run, export, and completion.
7. Confirm temporary tabs do not accumulate.
8. Confirm output folder contains six CSV files per template.

### Export Comparison Test

When changing export logic:
1. Manually export NinjaTrader `Trades`, `Analysis`, `Summary`, and `Settings`.
2. Save the manual exports as `Sample*.csv` in the template output folder.
3. Compare generated files to sample files.
4. Keep generated files structurally close to NinjaTrader, but prefer correct result-object values over unreliable grid exports.

## 6. Known Risks

### NinjaTrader Version Sensitivity

The reflection layer is tied to NinjaTrader 8.1.6.3 internals.

If NinjaTrader is upgraded:
- Revalidate template loading.
- Revalidate run command invocation.
- Revalidate tab creation and close behavior.
- Revalidate result object property names.

### Cancellation Limitation

The current cancel button is cooperative.

It stops the batch loop and attempts to close the active temporary tab. It does not call a known official Strategy Analyzer cancellation API because one has not been found yet.

### Export Approximation

Most values come from real Strategy Analyzer result objects.

Some NinjaTrader display-specific calculations, especially period analysis drawdown details, may not match the UI export exactly. If exact matching becomes critical, inspect deeper result types or locate NinjaTrader's internal analysis-grid data source.

## 7. Recommended Next Development Steps

1. Add remaining batch options to the UI.
   - Continue on export error.
   - Timeout minutes.

2. Improve exact Settings export.
   - Read more fields from `StrategyAnalyzerTabProperties`.
   - Include trading hours, break at EOD, order handling, fill settings, and time-in-force where available.

3. Improve exact Orders and Executions exports.
   - Compare generated files against NinjaTrader manual grid exports.
   - Match headers and formatting.

4. Improve cancellation if a deeper Strategy Analyzer run-stop hook is discovered.

5. Move custom optimizer work into a separate project before continuing its algorithm.

6. Add lightweight status output for IPC automation.
   - Example: write `C:\temp\nt8_status.json` with current template, status, and last error.

## 8. Handoff Notes For A New Chat

Start by reading:
1. `PROJECT_STATUS.md`
2. `PROJECT_PLAN.md`
3. `NinjaTraderAddOnProject/BatchControl.cs`
4. `NinjaTraderAddOnProject/StrategyAnalyzerAutomation.cs`

Before editing:
- Check `git status --short`.
- Do not stage `inspect_gui.ps1` unless the user specifically requests it.

Build with MSBuild, not `dotnet build`.

Deploy by stopping NinjaTrader, copying the DLL/PDB, and restarting NinjaTrader.

The latest known good behavior is:
- Batch runs templates.
- CSV output is correct enough for current user testing.
- Cancel button works.
- Temporary Analyzer tabs close after export.
