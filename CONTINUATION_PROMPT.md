# Continuation Prompt — Phase 1

Continue `D:\ninjatraderOptimizer` from the Phase 0 baseline tagged
`pre-nt-8.1.8.1-regression`.

Read, in order:

1. the active `AGENTS.md` instructions supplied by the operator;
2. `PROJECT_STATUS.md`;
3. `PROJECT_PLAN.md`;
4. `docs/PHASE0_VERIFICATION_2026-09-01.md`;
5. `docs/NT_8_1_8_1_REGRESSION_PLAN.md`;
6. `experiments/README.md`.

Goal: execute Phase 1 and produce an evidence-backed NinjaTrader 8.1.8.1
regression package for the batch AddOn and standalone optimizer.

Start read-only. Verify the tag, branch, clean working tree, installed
NinjaTrader version, installed DLL hashes/references, and current process state.
Read the applicable Trading Capability Hub playbooks before using the Python
NinjaTrader control path.

Do not deploy a DLL or restart NinjaTrader until all of the following have been
established with fresh evidence: Orders are empty, Positions are empty, every
strategy is disabled, no working order or order-capable automation is active,
and the owner has explicitly authorized the restart/deployment. Never force-
stop NinjaTrader.

After authorization, use the established Python/AddOn path and isolated,
no-order Backtest/Optimization fixtures. Do not drive NinjaTrader through ad hoc
GUI automation. Execute every case in the regression plan, including cold and
warm readiness, fixed Backtest, built-in and custom optimization, selector
discovery, CSV schemas, instrument preservation/override, duplicate run IDs,
timeout, cancellation, and temporary-tab cleanup.

Preserve exact evidence: Git SHA, NT version, built and installed DLL hashes,
assembly references, commands, status JSON, logs, template/output paths, test
results, and every pass/fail boundary. Do not touch canonical market data and do
not enable paper or live strategy trading. The lifecycle source under
`experiments/` is evidence only and must remain outside the production build.

At completion, update `PROJECT_STATUS.md` and the regression plan with exactly
what passed and what remains open, commit the evidence/docs, and push the
reviewed changes. Do not claim 8.1.8.1 certification unless every required case
has current artifacts.
