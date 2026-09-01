# NinjaTrader 8.1.8.1 Regression Plan

Status: ready to execute after owner authorization for the safe restart and
deployment step.

## Purpose

Certify the normalized source baseline against the installed NinjaTrader
8.1.8.1 runtime. A successful build is necessary but not sufficient because
the AddOn reflects over internal Strategy Analyzer types and members.

## Safety gate before deployment or restart

Record fresh evidence that:

- the Control Center Orders grid is empty;
- the Positions grid is empty;
- every strategy is disabled;
- no working order or order-capable automation is active.

Then obtain explicit owner authorization for the restart/deployment. Do not use
the legacy force-stop deployment path.

## Build evidence

- Rebuild `NinjaTraderAddOnProject.sln` against 8.1.8.1.
- Rebuild `NinjaTraderOptimizerProject.sln` against 8.1.8.1.
- Record MSBuild version, warnings/errors, DLL/PDB hashes, assembly references,
  Git SHA, and working-tree status.

## Isolated runtime fixtures

Use no-order Backtest/Optimization templates and output directories outside
canonical market data and production artifacts.

1. Confirm the AddOn loads and injects exactly one batch panel.
2. Confirm `ObserveCompile` acknowledges its run ID and produces a terminal
   schema-compatible status.
3. Run one fixed Backtest and verify Settings, Summary, Analysis, Trades,
   Orders, Executions, and BatchRunSummary outputs.
4. Run one built-in optimization and verify the optimization CSV contract.
5. Confirm `CustomMultiObjectiveOptimizer` and
   `CustomMultiObjectiveFitness` appear once in selectors with no duplicate-
   type or load warnings.
6. Run a small exhaustive custom-optimizer fixture and confirm each unique
   parameter combination appears exactly once.
7. Run a larger coverage-sampling fixture and capture unique/duplicate counts.
8. Verify a template contract is preserved when IPC omits `instrument`.
9. Verify an explicit IPC instrument overrides the template intentionally.
10. Verify duplicate `runId` events do not launch a second batch.
11. Verify timeout reporting and incomplete-run evidence.
12. Verify cancellation stops future templates and closes the active temporary
    tab without corrupting a completed result.
13. Verify temporary tabs do not accumulate across a multi-template run.

## Readiness cases

- Cold case: after the authorized restart, dispatch through the Python path
  without a manual batch click. Record the exact readiness state and outcome.
- Warm case: repeat the same isolated fixture after the Analyzer is runnable.
- If the cold case fails but retry/backoff succeeds, preserve both attempts and
  do not report cold readiness as solved.

## Acceptance criteria

- Every required case has an immutable artifact or log reference.
- Parser-facing CSV shapes pass the downstream Python tests.
- No canonical market data, account, order, position, or strategy source is
  changed by the regression.
- The installed DLL hashes and references match the certified build.
- `PROJECT_STATUS.md` is updated from “pre-regression” to the exact verified
  scope; any failure remains explicitly open.
