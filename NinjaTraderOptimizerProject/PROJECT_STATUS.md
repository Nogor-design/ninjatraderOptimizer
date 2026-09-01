# NinjaTrader Optimizer Project Status

## Current Status

Phase 0 baseline update (2026-09-01): the source rebuilds against the
installed NinjaTrader 8.1.8.1 assemblies, but the installed optimizer DLL still
references NinjaTrader Core 8.1.6.3. Automatic post-build deployment has been
removed. Current-version selector/runtime behavior remains behind
`..\docs\NT_8_1_8_1_REGRESSION_PLAN.md`.

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
- explicit, owner-gated deployment separate from the build

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

Deploy only after the safe-shutdown checks and explicit owner authorization:

```powershell
.\tools\Deploy-Optimizer.ps1 -OwnerAuthorizedRestart
```

The script refuses to deploy while NinjaTrader is running unless the explicit
authorization switch is supplied. It requests a graceful shutdown and refuses
to copy if NinjaTrader does not close within 30 seconds; it never force-stops
the process.

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

A successful build writes only to the project output folder. Deployment is a
separate step that copies:

- `NinjaTraderOptimizerProject.dll`
- `NinjaTraderOptimizerProject.pdb`

to:

```text
C:\Users\Owner\Documents\NinjaTrader 8\bin\Custom
```

Before runtime deployment, prove that Orders and Positions are empty, all
strategies are disabled, and no order-capable automation is active. Obtain
owner authorization, then use the guarded deployment script.

## Known Limitations

- The optimizer implementation is coverage-oriented sampling, not a complete genetic or Pareto optimizer.
- The fitness implementation calculates net profit and profit factor diagnostics, but NinjaTrader's ranking value is currently only profit factor.
- Runtime selector behavior still needs to be verified inside Strategy Analyzer after deployment.
- The 8.1.8.1 selector and runtime regression package is not yet complete.

## Next Step

Execute the custom optimizer and fitness cases in
`..\docs\NT_8_1_8_1_REGRESSION_PLAN.md` after the owner-gated deployment.
