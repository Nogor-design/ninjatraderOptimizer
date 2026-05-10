# Project Status: NinjaTrader 8.1.6.3 Optimizer & Batch Automation Suite

Last updated: 2026-05-09

## 1. Current Working State

The batch Strategy Analyzer workflow is currently working in NinjaTrader 8.1.6.3.

Confirmed behavior:
- The AddOn injects a compact `BATCH STRATEGY ANALYZER` panel into the Strategy Analyzer Settings panel.
- The panel auto-detects the selected Strategy Analyzer type and selected strategy.
- The source folder defaults to the selected strategy's template folder when possible.
- The user can select a folder of `.xml` Strategy Analyzer templates.
- The batch runner opens a temporary Analyzer tab for each template, loads that template, runs it, exports results, then closes the temporary tab.
- The original user tab remains open.
- `CANCEL` stops the batch from starting additional templates and tries to close the active temporary tab.
- Exports are written to `C:\Users\Owner\Downloads\output\<template-name>\`.
- Each run cleans prior generated CSV files in that template folder before writing new results.
- The latest DLL was built, deployed, and tested by the user.

Latest confirmed commit:
- `b2bd892 Add batch cancel and close analyzer tabs`

Known unrelated local change:
- `inspect_gui.ps1` is modified locally and should not be staged unless the user specifically asks for it.

## 2. Project Pieces

### Custom Fitness

File:
- `NinjaTraderAddOnProject/OptimizationFitnesses/CustomMultiObjectiveFitness.cs`

Status:
- Compiles.
- Provides the current custom optimization fitness scaffold and configurable scoring fields.

### Custom Optimizer

File:
- `NinjaTraderAddOnProject/Optimizers/CustomMultiObjectiveOptimizer.cs`

Status:
- Compiles.
- Still mostly a skeleton for future optimizer work.
- Future work is likely NSGA-II, Bayesian search, or another multi-objective search strategy.

### Batch Strategy Analyzer AddOn

Files:
- `NinjaTraderAddOnProject/AddOnFramework.cs`
- `NinjaTraderAddOnProject/BatchControl.cs`
- `NinjaTraderAddOnProject/StrategyAnalyzerAutomation.cs`

Status:
- Working in NinjaTrader runtime.
- This is currently the most mature and tested part of the project.

## 3. Batch Automation Architecture

### UI Injection

`AddOnFramework.cs` decorates the Strategy Analyzer Settings panel and inserts `BatchControl`.

`BatchControl.cs` builds the UI in code:
- Batch mode checkbox.
- Template source folder picker.
- Template count display.
- Result export folder picker.
- `RUN BATCH BACKTEST` / `RUN BATCH OPTIMIZATION` button label based on selected Analyzer type.
- `CANCEL` button.
- Live log textbox.

The UI listens to:
- `StrategyAnalyzerViewModel.PropertyChanged`.
- Selected tab property changes.

This lets the batch panel react when the user changes Backtest type, strategy, or template selection.

### Template Loading

Important discovery:
- NinjaTrader 8.1.6.3 exposes several useful-looking methods/properties that are effectively no-ops or stubs for this use case.
- `StrategyAnalyzerTabControl.Restore(XElement)` did not reliably load Analyzer templates.
- Some public setters on Strategy Analyzer properties also did not fully update internal state.

Current working approach in `StrategyAnalyzerAutomation.cs`:
- Creates a temporary `StrategyAnalyzerTabControl(viewModel)`.
- Calls internal/public tab-add flow.
- Resolves template `<StrategyType>`.
- Instantiates the strategy template.
- Applies simple XML properties from the template.
- Applies `BarsPeriodSerializable`, `BarsPeriod`, and `BarsPeriods[0]`.
- Writes required Strategy Analyzer backing fields directly:
  - `strategy`
  - `strategyTemplate`
  - `instrumentOrInstrumentList`
  - optimizer-related fields where applicable.

The batch runner preserves the instrument selected in the user's original Analyzer tab and applies it to each temporary template tab.

### Running Templates

`BatchControl.RunBatch()`:
1. Reads source/destination folders.
2. Finds `.xml` templates.
3. For each template:
   - Opens a temporary Analyzer tab.
   - Loads template state.
   - Restores the original instrument.
   - Starts the run through the Strategy Analyzer run command path.
   - Waits for results to appear and stabilize.
   - Exports results.
   - Closes the temporary tab.

Run completion detection:
- Checks selected result count.
- Checks the Strategy Analyzer progress flags.
- If results are stable but NinjaTrader still reports busy for a while, it logs:
  - `Progress flag still busy, but results have been stable; continuing to export.`
- This is intentional because NinjaTrader sometimes leaves the busy/progress flag true after usable results are available.

### Cancel Behavior

The `CANCEL` button:
- Sets `cancelRequested`.
- Prevents the next template from starting.
- Disables itself.
- Attempts to close the currently active temporary batch tab.

Limitation:
- NinjaTrader does not expose a clean public Strategy Analyzer cancellation API through the inspected classes.
- If NinjaTrader refuses to close the active tab during an active calculation, the current run may need to settle before the tab disappears.
- Cancel still stops the batch loop from continuing to additional templates.

### Temporary Tab Cleanup

Normal completion:
- Each batch-created tab is closed after export.
- The user’s original Analyzer tab should remain open.

Safety:
- The close helper will not close the last tab.
- If the original tab still exists, it is re-selected after closing the temporary tab.

## 4. Export Behavior

Native grid export was intentionally disabled.

Reason:
- NinjaTrader grid export found every visible `NTGrid` in the visual tree, causing duplicate or mismatched files such as `Analysis_1.csv`, `Trades_1.csv`, etc.
- Some native grid exports contained zeroed or wrong values while the Strategy Analyzer result object contained correct metrics.

Current export files per template:
- `Settings.csv`
- `Summary.csv`
- `Analysis.csv`
- `Trades.csv`
- `Orders.csv`
- `Executions.csv`

Output folder:
- `C:\Users\Owner\Downloads\output\<template-name>\`

Before writing exports:
- The exporter deletes existing `*.csv` files in that template folder.
- This prevents stale duplicates from previous runs.

### Settings.csv

Matches NinjaTrader's `Item,Value,` style.

Includes:
- Strategy parameters parsed from `ParametersString`.
- Basic data series values.
- Basic setup values.
- Strategy label and instrument.

Known limitation:
- Some settings are inferred from the Strategy Analyzer result object and template data.
- If exact Analyzer UI settings beyond the currently exported fields are needed, future work should read deeper from `StrategyAnalyzerTabProperties` and strategy template objects.

### Summary.csv

Matches NinjaTrader's Performance summary style:
- `Performance,All trades,Long trades,Short trades,`
- Currency formatting.
- Percent formatting.
- Start/end date and time.
- Trade counts.
- Win/loss stats.
- Drawdown.
- Average trade stats.
- Consecutive winner/loser stats.

### Analysis.csv

Matches the general shape of NinjaTrader's Analysis export:
- Period grouped by trade date.
- Cumulative net profit.
- Net/gross profit and loss.
- Commission.
- Drawdown fields.
- Win percentage.
- Average trade/winner/loser.
- Largest winner/loser.
- MTR, MAE, MFE, ETD, and percent of trades.

Known limitation:
- NinjaTrader's internal drawdown and period-analysis math may be slightly more nuanced than the generated approximation.
- Trade-level values come from the actual Strategy Analyzer trade objects.

### Trades.csv

Matches NinjaTrader's trade-row style:
- Trade number.
- Instrument.
- Account set as `Backtest`.
- Strategy.
- Market position.
- Entry/exit prices and times.
- Entry/exit names.
- Profit and cumulative net profit.
- Commission/fee columns.
- MAE, MFE, ETD, bars.

### Orders.csv and Executions.csv

These remain internal structured exports:
- `Orders.csv` exports order details from the result object.
- `Executions.csv` exports execution details from the result object.

These do not yet attempt to exactly mimic NinjaTrader grid exports.

## 5. Build and Deploy

Use MSBuild, not `dotnet build`.

`dotnet build` can fail on WPF/XAML generated members in this project. The working build route is the Visual Studio Build Tools MSBuild command:

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe" D:\ninjatraderOptimizer\NinjaTraderAddOnProject.sln /p:Configuration=Debug /t:Rebuild
```

Expected result:
- `Build succeeded.`
- `0 Warning(s)`
- `0 Error(s)`

Post-build copy may show a sharing violation if NinjaTrader is running. That is normal because NinjaTrader locks the DLL.

Deploy manually with NinjaTrader closed:

```powershell
$p = Get-Process -Name NinjaTrader -ErrorAction SilentlyContinue
if ($p) {
  $p | Stop-Process -Force
  Start-Sleep -Seconds 3
}

Copy-Item -Path "D:\ninjatraderOptimizer\NinjaTraderAddOnProject\bin\Debug\NinjaTraderAddOnProject.dll" -Destination "$HOME\Documents\NinjaTrader 8\bin\Custom\NinjaTraderAddOnProject.dll" -Force
Copy-Item -Path "D:\ninjatraderOptimizer\NinjaTraderAddOnProject\bin\Debug\NinjaTraderAddOnProject.pdb" -Destination "$HOME\Documents\NinjaTrader 8\bin\Custom\NinjaTraderAddOnProject.pdb" -Force

Start-Process -FilePath "C:\Program Files\NinjaTrader 8\bin\NinjaTrader.exe"
```

## 6. Runtime Validation Checklist

After deployment:
1. Launch/log into NinjaTrader.
2. Open Strategy Analyzer.
3. Select a strategy and set the desired instrument/date settings.
4. Enable `Batch mode`.
5. Confirm source folder points to the desired template folder.
6. Confirm destination folder, usually `C:\Users\Owner\Downloads\output`.
7. Click `RUN BATCH BACKTEST`.
8. Watch the log for:
   - `Starting batch with <n> templates.`
   - `Loaded template state: Strategy=..., TemplateType=..., Instrument=..., Bars=...`
   - `Running backtest...`
   - `Exported 6 internal CSV files to ...`
   - `Batch completed.`
9. Confirm each template folder contains:
   - `Settings.csv`
   - `Summary.csv`
   - `Analysis.csv`
   - `Trades.csv`
   - `Orders.csv`
   - `Executions.csv`

Cancel test:
1. Start a batch with multiple templates.
2. Click `CANCEL`.
3. Confirm no further templates start.
4. Confirm the active temporary tab closes or closes after the run settles.

Tab cleanup test:
1. Run multiple templates.
2. Confirm temporary tabs do not pile up after each export.

## 7. IPC Trigger

`BatchControl` watches:

```text
C:\temp\nt8_command.json
```

Any command containing `RunBatch` can trigger the batch. Optional fields:
- `sourceFolder`
- `destFolder`

Example:

```json
{
  "action": "RunBatch",
  "sourceFolder": "C:\\Users\\Owner\\Documents\\NinjaTrader 8\\templates\\Strategy\\PantheonMasterBotV01TesterV2",
  "destFolder": "C:\\Users\\Owner\\Downloads\\output"
}
```

## 8. Debugging Notes

NinjaTrader trace logs:

```text
C:\Users\Owner\Documents\NinjaTrader 8\trace\
```

Useful search strings:
- `BatchStrategyOptimizer`
- `TargetInvocationException`
- `InvalidOperationException`
- `cross-thread`
- `Setup error`
- `Run error`
- `Export logic error`
- `Close-tab error`

The AddOn also logs to:
- The injected batch panel textbox.
- NinjaTrader Output tab via `NinjaTrader.Code.Output.Process`.

Common issues:
- DLL sharing violation during build: NinjaTrader is running. Close/kill NinjaTrader and copy manually.
- Backtest never reports not busy: current code will export after result count stabilizes.
- Strategy does not exist: template strategy type was not loaded/compiled in NinjaTrader, or the template points at a different strategy class name.
- Missing instrument error: make sure the original Analyzer tab has a valid instrument selected before starting the batch.

## 9. Recommended Next Work

High value:
1. Add a small `BatchRunSummary.csv` at the destination root with one row per template and headline metrics.
2. Add a UI checkbox for `Close temporary tabs after each run`.
3. Add a UI checkbox for `Overwrite existing output`.
4. Improve `Settings.csv` by reading more exact values from `StrategyAnalyzerTabProperties`.
5. Add exact NinjaTrader-style `Orders.csv` and `Executions.csv` formats if needed.
6. Implement the actual custom multi-objective optimizer algorithm.
7. Add better cancellation if a deeper Strategy Analyzer run cancellation hook is discovered.

Keep in mind:
- Reflection bindings are version-sensitive.
- NinjaTrader should stay locked to 8.1.6.3 unless the automation layer is revalidated.
