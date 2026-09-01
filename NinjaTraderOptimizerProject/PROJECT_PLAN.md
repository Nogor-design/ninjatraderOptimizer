# NinjaTrader Optimizer Project Plan

## Purpose

Build a standalone NinjaTrader 8 assembly for custom optimization components:

- `CustomMultiObjectiveOptimizer`
- `CustomMultiObjectiveFitness`

This project is intentionally separate from the batch backtest AddOn. The batch AddOn automates Strategy Analyzer runs and exports results. This project owns optimizer and fitness behavior that NinjaTrader loads as NinjaScript optimization components.

## Project Layout

```text
NinjaTraderOptimizerProject/
  NinjaTraderOptimizerProject.sln
  PROJECT_PLAN.md
  PROJECT_STATUS.md
  RAG_WORKFLOW.md
  rag/
    learning_log.md
    prompts/
      optimizer_repair_prompt.md
  tools/
    Ask-NinjaTraderRag.ps1
    Build-WithLearningLog.ps1
    Deploy-Optimizer.ps1
  NinjaTraderOptimizerProject/
    NinjaTraderOptimizerProject.csproj
    App.config
    Optimizers/
      CustomMultiObjectiveOptimizer.cs
    OptimizationFitnesses/
      CustomMultiObjectiveFitness.cs
    Properties/
      AssemblyInfo.cs
```

## Build Requirements

- Windows
- NinjaTrader 8 installed at `C:\Program Files\NinjaTrader 8`
- Visual Studio Build Tools with MSBuild
- .NET Framework 4.8 targeting pack

Build with MSBuild, not `dotnet build`:

```powershell
& "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe" D:\ninjatraderOptimizer\NinjaTraderOptimizerProject\NinjaTraderOptimizerProject.sln /p:Configuration=Debug /t:Rebuild
```

The project is configured as:

- Target framework: .NET Framework 4.8
- Platform target: x64
- Output type: class library
- Output assembly: `NinjaTraderOptimizerProject.dll`

## Deployment

NinjaTrader loads compiled custom DLLs from:

```text
C:\Users\Owner\Documents\NinjaTrader 8\bin\Custom
```

Build and deployment are intentionally separate. A successful build produces:

- `NinjaTraderOptimizerProject.dll`
- `NinjaTraderOptimizerProject.pdb`

in the project output folder. It does not copy into NinjaTrader automatically.

For a clean deploy:

```powershell
.\tools\Build-WithLearningLog.ps1
.\tools\Deploy-Optimizer.ps1 -OwnerAuthorizedRestart
```

Before deployment, establish that Orders and Positions are empty, all
strategies are disabled, and no order-capable automation is active. The deploy
script requests a graceful shutdown and refuses to force-stop NinjaTrader.

## Relationship To The Batch AddOn

The main AddOn project should not compile these optimizer and fitness classes. If both assemblies define the same NinjaScript classes under:

- `NinjaTrader.NinjaScript.Optimizers.CustomMultiObjectiveOptimizer`
- `NinjaTrader.NinjaScript.OptimizationFitnesses.CustomMultiObjectiveFitness`

then NinjaTrader can see duplicate types and may fail to load or show confusing behavior.

The AddOn project can still automate Strategy Analyzer backtests. This optimizer project should only contain optimizer and optimization fitness components.

## Current Implementation

`CustomMultiObjectiveFitness` currently:

- exposes `Min Trades`
- exposes `Max Drawdown %`
- calculates `Objective1` from net profit
- calculates `Objective2` from profit factor
- sets NinjaTrader's ranking `Value` to profit factor

`CustomMultiObjectiveOptimizer` currently:

- exposes `Population Size`
- exposes `Generations`
- exposes `Random Seed`
- exposes `Duplicate Retry Limit`
- builds candidate values from NinjaTrader optimization parameter ranges
- samples those ranges with a Halton-style low-discrepancy coverage sequence
- tracks duplicate parameter signatures
- calls `RunIteration()`
- waits with `WaitForIterationsCompleted()`

This is a buildable coverage-search optimizer, not a complete genetic or Pareto optimizer yet.

## Next Implementation Steps

1. Verify NinjaTrader loads the separate DLL with no duplicate optimizer/fitness class warnings.
2. Confirm the optimizer and fitness appear in Strategy Analyzer optimization selectors.
3. Use NinjaTrader documentation and local inspection scripts to confirm the optimizer API contract for parameter generation, result collection, and fitness access.
4. Replace the placeholder optimizer loop with a real search algorithm.
5. Add validation around optimizer settings such as population size, generation count, and parameter ranges.
6. Decide whether the fitness should remain a single ranking value with extra diagnostic objectives or whether the optimizer should own Pareto ranking directly.
7. Add runtime logging that is useful inside NinjaTrader without flooding output.
8. Create an install/export README for distributing this optimizer package separately from the batch AddOn.

## RAG And Compile Learning Loop

Use `RAG_WORKFLOW.md` as the operating guide for documentation retrieval and compile-feedback learning.

Key files:

- `tools\Build-WithLearningLog.ps1`
- `tools\Ask-NinjaTraderRag.ps1`
- `rag\learning_log.md`

The RAG system should be used before changing unfamiliar NinjaTrader optimizer APIs. Verified compile/runtime lessons should be recorded in `rag\learning_log.md`.

## Testing Plan

1. Build with MSBuild.
2. After the explicit safety and owner gate, deploy DLL/PDB and restart NinjaTrader.
3. Open Strategy Analyzer.
4. Start a small optimization using a known built-in strategy and instrument.
5. Select `CustomMultiObjectiveOptimizer`.
6. Select `CustomMultiObjectiveFitness`.
7. Confirm the run starts, progresses, can be aborted, and completes without UI lockups.
8. Inspect NinjaTrader output/log tabs for load, compile, or runtime errors.

## Open Questions

- Which specific multi-objective algorithm should be used first: weighted score, Pareto ranking, NSGA-II style selection, or a simpler staged implementation?
- What objectives matter most for the first usable version: net profit, profit factor, max drawdown, Sharpe, Sortino, trade count, win rate, or custom constraints?
- Should this optimizer export its own diagnostic summary, or should result export stay entirely in the batch AddOn?
