# NinjaTrader Optimizer Project Status

## Current Status

The optimizer and optimization fitness components have been split into a standalone project:

```text
D:\ninjatraderOptimizer\NinjaTraderOptimizerProject
```

The project is set up to build the same way as the main AddOn project:

- Visual Studio solution file
- .NET Framework 4.8 class library
- x64 target
- NinjaTrader DLL references
- MSBuild-compatible project file
- post-build copy to NinjaTrader's `bin\Custom` folder

Build verification:

- Debug rebuild completed successfully with MSBuild.
- Build finished with 0 warnings and 0 errors.
- `NinjaTraderOptimizerProject.dll` and `.pdb` copied to NinjaTrader's `bin\Custom` folder.
- NinjaTrader was restarted after deploying the split DLLs.
- `tools\Build-WithLearningLog.ps1` was verified and writes MSBuild logs to `rag\build_logs\`.
- 2026-05-15: coverage-search optimizer rebuild succeeded with 0 warnings and 0 errors in `rag\build_logs\msbuild_20260515_114727.txt`; DLL/PDB copied to NinjaTrader `bin\Custom`, then NinjaTrader was restarted.

## Files Created

- `NinjaTraderOptimizerProject.sln`
- `NinjaTraderOptimizerProject\NinjaTraderOptimizerProject.csproj`
- `NinjaTraderOptimizerProject\App.config`
- `NinjaTraderOptimizerProject\Properties\AssemblyInfo.cs`
- `NinjaTraderOptimizerProject\Optimizers\CustomMultiObjectiveOptimizer.cs`
- `NinjaTraderOptimizerProject\OptimizationFitnesses\CustomMultiObjectiveFitness.cs`
- `PROJECT_PLAN.md`
- `PROJECT_STATUS.md`
- `RAG_WORKFLOW.md`
- `rag\learning_log.md`
- `rag\prompts\optimizer_repair_prompt.md`
- `tools\Ask-NinjaTraderRag.ps1`
- `tools\Build-WithLearningLog.ps1`
- `tools\Deploy-Optimizer.ps1`

## Files Updated In Main Project

The main AddOn project no longer compiles:

- `OptimizationFitnesses\CustomMultiObjectiveFitness.cs`
- `Optimizers\CustomMultiObjectiveOptimizer.cs`

Those files are now compiled by the standalone optimizer project instead. This avoids duplicate NinjaScript class definitions when both DLLs are deployed to NinjaTrader.

## Build Command

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe" D:\ninjatraderOptimizer\NinjaTraderOptimizerProject\NinjaTraderOptimizerProject.sln /p:Configuration=Debug /t:Rebuild
```

Preferred learning-loop build command:

```powershell
.\tools\Build-WithLearningLog.ps1
```

This writes MSBuild output to `rag\build_logs\` so compile failures can be fed back into the RAG repair workflow.

Preferred deploy command after a successful build:

```powershell
.\tools\Deploy-Optimizer.ps1
```

This stops NinjaTrader, copies the optimizer DLL/PDB to NinjaTrader's custom bin folder, and restarts NinjaTrader.

## RAG Workflow

The optimizer project now has a sidecar RAG workflow documented in:

```text
RAG_WORKFLOW.md
```

It uses the existing NinjaTrader docs RAG project at:

```text
D:\Backup\projects\PythonProject\NinjatraderDocScrapper
```

Ask the docs RAG for optimizer-specific help with:

```powershell
.\tools\Ask-NinjaTraderRag.ps1 -Task "Explain the documented API contract for custom NinjaTrader optimizers and optimization fitness classes."
```

## Deployment Behavior

On successful build, the project post-build event copies:

- `NinjaTraderOptimizerProject.dll`
- `NinjaTraderOptimizerProject.pdb`

to:

```text
C:\Users\Owner\Documents\NinjaTrader 8\bin\Custom
```

For runtime testing, stop NinjaTrader before building/deploying, then restart NinjaTrader after the DLL/PDB have copied.

## Known Limitations

- The optimizer implementation is coverage-oriented sampling, not a complete genetic or Pareto optimizer.
- The fitness implementation calculates net profit and profit factor diagnostics, but NinjaTrader's ranking value is currently only profit factor.
- Runtime selector behavior still needs to be verified inside Strategy Analyzer after deployment.
- The main AddOn still contains the original optimizer source files on disk, but they are no longer compiled by that project.

## Next Step

Verify inside Strategy Analyzer that the custom optimizer and custom fitness appear without duplicate type or load errors, then run a small custom optimizer template and confirm Output tab messages show `Starting coverage-search run`.
