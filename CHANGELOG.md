# Changelog

## Pre-8.1.8.1 regression baseline — 2026-09-01

- Made the AddOn source reproducible by tracking `CompileObserverService.cs`.
- Preserved the failed strategy-lifecycle spike under `experiments/` and
  removed it from the production build and IPC dispatcher.
- Disabled the legacy AddOnPage RunBatch watcher to prevent duplicate jobs.
- Retained RunBatch IPC hardening: run-ID de-duplication, explicit cancellation,
  timeouts, instrument-contract preservation, status heartbeats, deferred tab
  cleanup, durable logging, and compile-observer support.
- Added current source/installed/runtime boundary documentation.
- Added the NinjaTrader 8.1.8.1 regression plan.
- Archived the detailed May 2026 status and plan under `docs/history/`.
- Removed automatic post-build copies into the live NinjaTrader custom folder.
- Changed optimizer deployment to require explicit restart authorization and a
  graceful shutdown; the script no longer force-stops NinjaTrader.

This baseline is build-verified but not yet runtime-certified against every
NinjaTrader 8.1.8.1 reflected path.
