Batch Strategy Analyzer AddOn for NinjaTrader 8
================================================

Current compatibility boundary
------------------------------
The normalized source rebuilds against NinjaTrader 8.1.8.1. The currently
installed AddOn and optimizer binaries were built against older NinjaTrader
assemblies and remain the installed baseline until the 8.1.8.1 regression plan
passes.

Do not treat a successful MSBuild result as runtime certification. This AddOn
uses Strategy Analyzer internals through reflection.


Projects
--------
- NinjaTraderAddOnProject.dll
  RunBatch, ObserveCompile, Strategy Analyzer automation, and CSV exports.

- NinjaTraderOptimizerProject.dll
  Optional standalone coverage optimizer and custom fitness.

The strategy-lifecycle experiment under experiments\ is not compiled or
installed.


Verification build without deployment
-------------------------------------
Use full MSBuild Rebuild. Do not use dotnet build or the Compile-only target.

  MSBuild.exe NinjaTraderAddOnProject.sln /t:Rebuild /p:Configuration=Debug /p:PostBuildEvent=

  MSBuild.exe NinjaTraderOptimizerProject\NinjaTraderOptimizerProject.sln /t:Rebuild /p:Configuration=Debug /p:PostBuildEvent=

The empty PostBuildEvent prevents a verification build from copying DLLs into
the live NinjaTrader custom folder.


Deployment gate
---------------
Before copying either DLL or restarting NinjaTrader:

1. Confirm the Control Center Orders grid is empty.
2. Confirm the Positions grid is empty.
3. Confirm all strategies are disabled.
4. Confirm no order-capable automation is active.
5. Obtain explicit owner authorization for the restart/deployment.

Do not force-stop NinjaTrader as the default deployment method.

After authorization, follow docs\NT_8_1_8_1_REGRESSION_PLAN.md and use isolated,
no-order templates. Do not write canonical market data as part of the AddOn
regression.


Basic use after certification
-----------------------------
1. Start and warm NinjaTrader through the established Python readiness path.
2. Open Strategy Analyzer and select a valid strategy/instrument.
3. Enable Batch mode.
4. Select a folder of Strategy Analyzer XML templates.
5. Select an isolated output folder.
6. Run the batch from the UI or the single-writer Python IPC bridge.

The AddOn writes per-template Settings, Summary, Analysis, Trades, Orders,
Executions, and optimization CSVs plus BatchRunSummary.csv at the output root.


Uninstall
---------
After the same safe-shutdown gate, remove the specifically named DLL/PDB files
from Documents\NinjaTrader 8\bin\Custom and restart NinjaTrader. Do not perform
broad or recursive cleanup in the NinjaTrader custom folder.
