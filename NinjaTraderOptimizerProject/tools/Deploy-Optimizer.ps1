param(
    [string]$Configuration = "Debug",
    [string]$ProjectRoot = "D:\ninjatraderOptimizer\NinjaTraderOptimizerProject",
    [string]$NinjaTraderPath = "C:\Program Files\NinjaTrader 8\bin\NinjaTrader.exe",
    [string]$CustomBin = "",
    [switch]$OwnerAuthorizedRestart,
    [switch]$SkipStart
)

$ErrorActionPreference = "Stop"

$dllPath = Join-Path $ProjectRoot "NinjaTraderOptimizerProject\bin\$Configuration\NinjaTraderOptimizerProject.dll"
$pdbPath = Join-Path $ProjectRoot "NinjaTraderOptimizerProject\bin\$Configuration\NinjaTraderOptimizerProject.pdb"

if ([string]::IsNullOrWhiteSpace($CustomBin)) {
    $documents = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    $CustomBin = Join-Path $documents "NinjaTrader 8\bin\Custom"
}

if (-not (Test-Path -LiteralPath $dllPath)) {
    throw "Optimizer DLL was not found at $dllPath. Build first with tools\Build-WithLearningLog.ps1."
}

if (-not (Test-Path -LiteralPath $pdbPath)) {
    throw "Optimizer PDB was not found at $pdbPath. Build first with tools\Build-WithLearningLog.ps1."
}

$process = Get-Process -Name NinjaTrader -ErrorAction SilentlyContinue
if ($process -and -not $OwnerAuthorizedRestart) {
    throw @"
NinjaTrader is running. Deployment is blocked.
Verify that Orders and Positions are empty, every strategy is disabled, and no
order-capable automation is active. Then obtain owner authorization and rerun
with -OwnerAuthorizedRestart.
"@
}

if ($process) {
    Write-Host "Requesting a graceful NinjaTrader shutdown..."
    foreach ($item in @($process)) {
        [void]$item.CloseMainWindow()
    }
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $process = Get-Process -Name NinjaTrader -ErrorAction SilentlyContinue
    } while ($process -and (Get-Date) -lt $deadline)

    if ($process) {
        throw "NinjaTrader did not close gracefully within 30 seconds. Deployment stopped; no files were copied."
    }
}

New-Item -ItemType Directory -Force -Path $CustomBin | Out-Null

Copy-Item -LiteralPath $dllPath -Destination (Join-Path $CustomBin "NinjaTraderOptimizerProject.dll") -Force
Copy-Item -LiteralPath $pdbPath -Destination (Join-Path $CustomBin "NinjaTraderOptimizerProject.pdb") -Force

Write-Host "Copied optimizer DLL/PDB to $CustomBin"

if (-not $SkipStart) {
    Start-Process -FilePath $NinjaTraderPath
    Write-Host "Started NinjaTrader."
}
