# NinjaTrader Optimizer Plan

Last updated: 2026-09-01

## Product role

Keep this repository narrow: NinjaTrader is the worker; `D:\ta_foundation` is
the conductor and system of record. The AddOn owns Strategy Analyzer execution,
compile observation, status heartbeats, and structured exports. It does not own
strategy selection, portfolio state, account supervision, risk approval, or
live activation.

## Phase 0 — canonical source baseline

Status: complete (2026-09-01).

- Preserve an independent snapshot of the seven unpushed commits and dirty
  working files.
- Track every source file needed for a fresh build.
- Preserve the failed strategy-lifecycle spike outside the production build.
- Remove captured editor-temporary files and ignore the pattern.
- Distinguish Git source, locally rebuilt binaries, installed binaries, and
  runtime proof in the documentation.
- Rebuild both solutions without deployment and run downstream IPC contract
  tests.
- Push the normalized baseline and tag it before current-version regression.

## Phase 1 — NinjaTrader 8.1.8.1 regression

Status: next.

Use `docs/NT_8_1_8_1_REGRESSION_PLAN.md`. The package must cover AddOn load,
fixed Backtest, built-in and custom optimization, custom optimizer/fitness
discovery, CSV exports, instrument handling, cancellation, duplicate run IDs,
timeouts, temporary-tab cleanup, and cold/warm readiness.

The regression must use isolated no-order fixtures. A NinjaTrader restart and
DLL deployment require the owner gate described in the regression plan.

## Phase 2 — bridge hardening

Start only after Phase 1 establishes a current working baseline.

- Move RunBatch ownership from per-window watchers to one global dispatcher.
- Publish schema version, worker kind, NinjaTrader version, AddOn build ID, and
  explicit Analyzer readiness in status heartbeats.
- Replace inferred warm-up state with a deterministic readiness handshake.
- Split the large `BatchControl` into IPC, orchestration, export, and UI units.
- Remove the dormant compile-observer and legacy AddOnPage batch code after
  fixture coverage protects their contracts.
- Add golden XML/CSV fixtures and reflection compatibility tests.

## Phase 3 — optimizer decisions

Do not expand the algorithm merely because NSGA-II or Bayesian optimization was
previously listed as future work.

- Keep the current optimizer documented as coverage search unless a true
  multi-objective requirement is demonstrated.
- Fix high-dimensional sampling before claiming broad coverage for strategies
  with more than 30 swept parameters.
- Continue using `pyopt` for the exact Pantheon strategy it ports and the
  NinjaTrader route for arbitrary NinjaScript or authoritative validation.
- Keep fitness-parameter injection parked unless an experiment proves that
  NinjaTrader `KeepBestResults` is discarding useful trades-backed candidates.

## Non-goals

- Live or unattended strategy activation.
- Account dashboards, copy trading, or order submission.
- Risk-engine or portfolio-control functionality already owned by sibling
  systems.
- A second operator UI competing with the `ta_foundation` web control plane.

Historical detailed plan: `docs/history/PROJECT_PLAN_2026-05-11.md`.
