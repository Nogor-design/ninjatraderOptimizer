param(
    [string]$Configuration = "Debug",
    [string]$ProjectRoot = "D:\ninjatraderOptimizer\NinjaTraderOptimizerProject",
    [string]$NinjaTraderPath = "C:\Program Files\NinjaTrader 8\bin\NinjaTrader.exe",
    [string]$CustomBin = "$HOME\Documents\NinjaTrader 8\bin\Custom",
    [switch]$SkipStop,
    [switch]$SkipStart
)

$ErrorActionPreference = "Stop"

$dllPath = Join-Path $ProjectRoot "NinjaTraderOptimizerProject\bin\$Configuration\NinjaTraderOptimizerProject.dll"
$pdbPath = Join-Path $ProjectRoot "NinjaTraderOptimizerProject\bin\$Configuration\NinjaTraderOptimizerProject.pdb"

if (-not (Test-Path -LiteralPath $dllPath)) {
    throw "Optimizer DLL was not found at $dllPath. Build first with tools\Build-WithLearningLog.ps1."
}

if (-not (Test-Path -LiteralPath $pdbPath)) {
    throw "Optimizer PDB was not found at $pdbPath. Build first with tools\Build-WithLearningLog.ps1."
}

if (-not $SkipStop) {
    $process = Get-Process -Name NinjaTrader -ErrorAction SilentlyContinue
    if ($process) {
        Write-Host "Stopping NinjaTrader before deploy..."
        $process | Stop-Process -Force
        Start-Sleep -Seconds 3
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
