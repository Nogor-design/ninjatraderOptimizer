# Experiments

Files in this directory are preserved research evidence and are not compiled
into the production AddOn.

## Strategy lifecycle spike

`StrategyLifecycleService.Experimental.cs` contains the June 2026 experiment
for `ConnectPlayback`, `EnableStrategy`, and `DisableStrategy` IPC commands.

- `ConnectPlayback` worked when NinjaTrader object access was marshalled to the
  WPF dispatcher.
- External strategy activation reached `State.Configure` but NinjaTrader
  silently refused `State.Active` on 8.1.7.1.
- Direct `State.DataLoaded` finalized the object.

This service must not be re-added to the production build without a new scoped
research decision and current-version evidence. Live or unattended activation
is not authorized.
