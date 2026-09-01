# NinjaTrader Optimizer Repository Status

Last updated: 2026-09-01

## Current conclusion

This repository is the NinjaTrader execution worker for the broader
`D:\ta_foundation` research and validation system. It is not the portfolio,
risk, account-management, or deployment authority.

The production AddOn surface is intentionally limited to:

- `RunBatch` — run Strategy Analyzer XML templates and export results.
- `ObserveCompile` — observe NinjaTrader auto-compile state for the strategy
  factory loop.

The June 2026 `ConnectPlayback` / `EnableStrategy` / `DisableStrategy` spike is
not part of the production build. `ConnectPlayback` worked, but external
strategy activation stalled at `State.Configure`. The source is preserved in
`experiments/StrategyLifecycleService.Experimental.cs` for evidence only.

## Source, installed runtime, and proof boundaries

| Boundary | Current state |
|---|---|
| Installed NinjaTrader | 8.1.8.1 |
| Current source build references | NinjaTrader Core/Gui 8.1.8.1 |
| Installed AddOn references | NinjaTrader Core/Gui 8.1.7.2; deployed DLL last written 2026-07-27 |
| Installed optimizer references | NinjaTrader Core 8.1.6.3; deployed DLL last written 2026-05-16 |
| Current source build | Both solutions rebuild successfully with deployment disabled |
| Current-version runtime certification | Not complete; `docs/NT_8_1_8_1_REGRESSION_PLAN.md` is the next gate |

The current source rebuild has not been deployed. Installed binaries and the
source baseline must remain described separately until the regression package
passes and a safe, owner-authorized NinjaTrader restart is performed.

## What is proven

- The batch AddOn has historically completed custom-optimizer phases of 500,
  8,000, and 640 parser-clean rows plus eight final fixed Backtests.
- On 2026-08-30, the Python-to-AddOn path completed an isolated no-order export
  on NinjaTrader 8.1.8.1 after the Strategy Analyzer was warmed: 2,759 fresh
  one-minute bars, exit code 0, `running 0/1` to `finished 1/1`, zero trades.
- The current working source for both DLLs rebuilds against the installed
  8.1.8.1 assemblies when the post-build deployment event is disabled.
- The downstream Python contracts for optimizer runner, market-data export,
  compile observer, optimizer bridge, and full loop pass 79 tests.
- `CompileObserverService.cs` is now part of the source baseline, making a fresh
  checkout buildable rather than depending on an untracked file.

## What remains unproven

- Full AddOn and custom-optimizer behavior on NinjaTrader 8.1.8.1.
- Cold Strategy Analyzer readiness without a prior runnable batch.
- Current-version custom optimizer/fitness selector discovery.
- Current-version export parity for every CSV shape.
- Cancellation, duplicate-run rejection, timeout, and tab cleanup as one
  recorded 8.1.8.1 regression package.

## Repository components

- `NinjaTraderAddOnProject/` — reflection-based Strategy Analyzer batch worker,
  compile observer, CSV exporters, and IPC status reporting.
- `NinjaTraderOptimizerProject/` — standalone coverage-oriented optimizer and
  custom fitness DLL. It is exhaustive for small spaces and Halton-sampled for
  larger spaces; it is not a Pareto or evolutionary optimizer.
- `experiments/` — preserved research code that is not compiled or deployed.
- `docs/history/` — detailed May 2026 implementation handoffs retained for
  archaeology, not current status.

## Safety boundary

- No live trading is authorized.
- This project may run research Backtests/Optimizations after the current-
  version regression gate.
- Do not force-stop NinjaTrader for deployment. First establish that orders and
  positions are empty and strategies are disabled, then obtain owner approval
  for the restart/deployment step.
- Research results are evidence, not permission to activate, size, or trade a
  strategy.

## Next gate

Execute `docs/NT_8_1_8_1_REGRESSION_PLAN.md` using isolated, no-order fixtures.
Do not deploy the rebuilt DLLs as the canonical installed baseline until that
package passes.

Historical detailed status: `docs/history/PROJECT_STATUS_2026-05-11.md`.
