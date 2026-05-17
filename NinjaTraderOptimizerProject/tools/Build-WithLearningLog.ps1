param(
    [string]$Configuration = "Debug",
    [string]$MSBuildPath = "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
    [string]$SolutionPath = "D:\ninjatraderOptimizer\NinjaTraderOptimizerProject\NinjaTraderOptimizerProject.sln"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$logRoot = Join-Path $projectRoot "rag\build_logs"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logPath = Join-Path $logRoot "msbuild_$timestamp.txt"

Write-Host "Building $SolutionPath"
Write-Host "Log: $logPath"

& $MSBuildPath $SolutionPath /p:Configuration=$Configuration /t:Rebuild /nologo 2>&1 |
    Tee-Object -FilePath $logPath

$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "Build succeeded. Review $logPath before adding any verified learning notes."
} else {
    Write-Host "Build failed. Use this log with tools\Ask-NinjaTraderRag.ps1:"
    Write-Host $logPath
}

exit $exitCode
