# Phase 0 Verification — 2026-09-01

## Snapshot and recovery

Before normalization, an external Git bundle and a ZIP containing every dirty
or untracked working file were created under:

`D:\ninjatraderOptimizer-snapshots`

The bundle was verified as a complete history ending at `6e91188`. The ZIP
preserves the pre-normalization working files, including both editor-temporary
copies that were subsequently removed from the repository.

## Source normalization

- `CompileObserverService.cs` is tracked and compiled.
- The failed strategy-lifecycle spike is retained under `experiments/` and is
  not compiled or dispatched.
- The legacy AddOnPage RunBatch watcher is not initialized.
- The two captured `BatchControl.cs.tmp.*` files were removed and `*.tmp.*` is
  ignored.
- Automatic post-build copies into NinjaTrader were removed from both project
  files.
- The optimizer deployment script blocks an unapproved running-NinjaTrader
  deployment, requests graceful shutdown after authorization, and never uses a
  forced process stop.

## Build verification

Command family: Visual Studio Build Tools MSBuild 18.5, full Debug `Rebuild`.

- `NinjaTraderAddOnProject.sln`: pass.
- `NinjaTraderOptimizerProject.sln`: pass.
- Rebuilt AddOn references NinjaTrader Gui/Core 8.1.8.1.
- Rebuilt optimizer references NinjaTrader Core 8.1.8.1.
- The installed AddOn DLL remained byte-identical:
  `52B9376AB4535BBA783B0DE1D70F38AAD6DDDDBD3B81B8247EACB39197D2926A`.
- The installed optimizer DLL remained byte-identical:
  `248CEB5BAB69AA9998C238CCFAC3668B9B27EFB076DD8FD29955A012A384600B`.

No DLL was deployed and NinjaTrader was not restarted.

## Contract verification

The following downstream `ta_foundation` suites passed together:

- optimizer runner;
- market-data export;
- compile observer;
- optimizer bridge;
- full strategy loop.

Result: **79 passed in 7.94 seconds**.

Both PowerShell build/deploy helpers passed parser validation. `git diff
--check` reported no whitespace errors.

## Remaining gate

This is a source/build baseline, not runtime certification. Execute
`NT_8_1_8_1_REGRESSION_PLAN.md` after the explicit owner gate for the safe
restart/deployment step.
