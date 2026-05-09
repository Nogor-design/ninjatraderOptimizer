# NinjaTrader 8.1.6.3 Custom Optimizer & Automation Suite: Project Plan

## 1. High-Level Overview
This project aims to enhance NinjaTrader 8.1.6.3's quantitative analysis capabilities by introducing a custom-built, multi-objective **Optimizer**, a specialized **OptimizationFitness** class for advanced scoring, and a **Batch Strategy Analyzer Automation AddOn** to run sequential backtest templates programmatically. 

The initiative moves beyond NinjaTrader's default genetic algorithm by allowing complex constraints, custom multi-objective goals (e.g., Pareto optimization of Drawdown vs. Profit Factor), and automated hands-free testing across many strategy templates.

### Core Objectives:
1. **Custom OptimizationFitness:** Implement custom scoring (e.g., penalties for high drawdown, low trade counts) fully compatible with Strategy Analyzer.
2. **Custom Optimizer:** Build a sophisticated optimizer (e.g., NSGA-II Genetic Algorithm or Bayesian) tightly integrated with NinjaTrader’s backtest engine.
3. **Hybrid Architecture:** Decouple search heuristics (Optimizer) from scoring/evaluation (Fitness) allowing for dynamic parameter adjustment.
4. **Batch Automation AddOn:** Create a custom `NTWindow` AddOn to read Strategy Analyzer templates, execute them in batch via reflection, and export structured result files.

## 2. Development Workflow & Environment

### Visual Studio AddOn Project Integration
- **Environment:** Use the official NinjaTrader Visual Studio AddOn template (targeting .NET 4.8).
- **Project Structure:**
  - Reference `NinjaTrader.Core.dll`, `NinjaTrader.Gui.dll`, `NinjaTrader.Cbi.dll`.
  - Use a single solution with projects for `Fitness`, `Optimizer`, and `AddOn`.
  - **Post-Build Event:** Automatically copy the compiled DLL to `C:\Users\<User>\Documents\NinjaTrader 8\bin\Custom\`.
  - **Debug Action:** Set the Start Action to launch `NinjaTrader.exe`.
- **IntelliSense & Debugging:** Provides full IntelliSense and allows "Attach to Process" for real-time debugging inside NinjaTrader.

### NinjaTrader Compilation & Deployment (Agent-Driven CI/CD Pipeline)
- **Compilation Automation:** Use the shell to run `MSBuild` or `dotnet build` directly on the Visual Studio `.sln`. The LLM agent will read standard output to detect and fix syntax errors or missing references.
- **Automated Deployment & Restart:** Since NinjaTrader locks DLLs, the agent will script a process restart: kill `NinjaTrader.exe`, copy the new DLL to `C:\Users\<User>\Documents\NinjaTrader 8\bin\Custom\`, and launch NT8 via `Start-Process`.
- **Remote Triggering:** The AddOn will include a lightweight IPC mechanism (e.g., a file-watcher looking for `nt8_command.json`). The agent writes to this file to trigger the batch process remotely, eliminating the need for GUI interaction.

## 3. Architecture Overview

### Hybrid Architecture Pattern (Optimizer + Fitness)
```text
[AddOn (Batch Automation)] -> [NTWindow / NTTabPage]
         | (Reflection Invocation)
         v
[Strategy Analyzer Internal Engine]
         | (Instantiates)
         v
[Custom Optimizer (e.g., MyNSGA2)]
    | -> Handles: Population generation, Crossover, Mutation, Iteration control
    | -> Invokes: Strategy iterations
         |
         v
[Custom OptimizationFitness]
    | -> Handles: Scoring logic via SystemPerformance
    | -> Implementation: Use LINQ to query `strategy.SystemPerformance.AllTrades.TradesPerformance.PerformanceMetrics` for custom metrics if needed.
    | -> Returns: `Value` to Optimizer.
```

## 4. Phase Breakdown & Development Order

### Phase 1: Custom OptimizationFitness Development
*Goal: Establish the scoring logic.*
- **Task 1.1:** Scaffold `OptimizationFitness` class in VS.
- **Task 1.2:** Implement `OnCalculatePerformanceValue(StrategyBase strategy)` using `strategy.SystemPerformance`.
- **Task 1.3:** **Validation:** Verify it appears in Strategy Analyzer "Optimization fitness" dropdown after internal NT compile.
- **Task 1.4:** Multi-Objective Support: Add properties for `Objective1`, `Objective2` to be read by the custom optimizer.

### Phase 2: Custom Optimizer Development
*Goal: Build the search engine.*
- **Task 2.1:** Scaffold `Optimizer` class inheriting from `NinjaTrader.NinjaScript.Optimizers.Optimizer`.
- **Task 2.2:** Implement search heuristics (Genetic/Bayesian).
- **Task 2.3:** **Validation:** Validate population generation and iteration queuing within the NT8 Strategy Analyzer UI.
- **Task 2.4:** Casting: Implement logic to cast the `OptimizationFitness` object to the custom type to extract multi-objective data.

### Phase 3: Batch Strategy Analyzer Automation AddOn
*Goal: Automate template execution via a custom UI.*
- **Task 3.1:** Create `AddOnBase` and `NTWindow` classes based on the `AddOnFramework` pattern.
- **Task 3.2:** Implement `OnWindowCreated` to add a menu item to the Control Center "New" menu.
- **Task 3.3:** **Reflection Engine:** Use reflection to access `NinjaTrader.Gui.Tools.StrategyAnalyzer`. Map methods for `LoadTemplate()`, `Run()`, and hook into `OptimizationCompleted`.
- **Task 3.4:** UI Development: Add `NTTabPage` with progress bars, logs, and a "Start Batch" button.
- **Task 3.5:** **UI Thread Management:** Use `Core.Globals.RandomDispatcher.BeginInvoke` for all reflection calls touching the GUI to avoid deadlocks.

### Phase 4: Integration & Maintenance
- **Task 4.1:** **End-to-End Validation:** Run a batch of templates using the custom Optimizer and Fitness.
- **Task 4.2:** **Memory Management:** Monitor RAM; trigger `GC.Collect()` between template runs.
- **Task 4.3:** **Version Locking:** Lock NinjaTrader at 8.1.6.3 and disable auto-updates to protect reflection bindings.

## 5. Testing & Error Handling

### Testing Strategy (Agent-Driven Telemetry Analysis)
- **Visual Studio cannot simulate NinjaTrader's runtime environment.**
- **Automated Validation:** The agent will achieve zero-touch validation by reading NinjaTrader's internal telemetry.
  - **Trace Logs:** The agent will tail `C:\Users\<User>\Documents\NinjaTrader 8\trace\` to catch `TargetInvocationException` (reflection failures) or `InvalidOperationException` (cross-thread UI deadlocks).
  - **Status Polling:** The IPC file-watcher (`nt8_status.json`) will report the success/failure state of the Strategy Analyzer automation loop back to the agent.
  - **Data Verification:** The agent will programmatically read the exported CSV/JSON files to verify multi-objective ranking accuracy and output completeness.

### Error Detection & Resolution
- **Try/Catch Guards:** Wrap all reflection calls. Log failures to the NinjaTrader Output window or Log tab.
- **NT Compiler Errors:** Resolve NT-specific errors inside the NinjaScript Editor first, then sync back to VS.
- **Known Errors Checklist:** Maintain a log of common NT-specific issues (e.g., cross-thread exceptions, missing assembly references).

## 6. Estimated Complexity and Time Requirements

| Phase | Description | Complexity | Est. Duration |
| :--- | :--- | :--- | :--- |
| **Phase 1** | Custom Fitness Algorithm | Low | 3 - 5 Days |
| **Phase 2** | Custom Optimizer | High | 10 - 15 Days |
| **Phase 3** | Batch Automation AddOn | High | 12 - 16 Days |
| **Phase 4** | Integration & Testing | Medium | 5 - 7 Days |
| **Total** | | | **5 - 7 Weeks** |

## 7. Maintenance & Safety
- **Reflection Compatibility Layer:** Abstract all reflection calls to facilitate updates if NT internal signatures change.
- **Build Synchronization:** Keep NT and VS projects in sync manually.
- **Data Integrity:** Validate that exported results match UI values before final delivery.
