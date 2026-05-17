param(
    [Parameter(Mandatory = $true)]
    [string]$Task,

    [string]$ExistingCodeFile = "",
    [string]$CompilerErrorsFile = "",
    [string]$DocsProjectRoot = "D:\Backup\projects\PythonProject\NinjatraderDocScrapper",
    [string]$Model = "qwen3-coder:30b",
    [string]$EmbedModel = "nomic-embed-text",
    [int]$TopK = 10
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $projectRoot "rag\outputs"
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$python = Join-Path $DocsProjectRoot ".venv\Scripts\python.exe"
$generator = Join-Path $DocsProjectRoot "generate_ninjascript.py"
$db = Join-Path $DocsProjectRoot "ninjatrader_docs\rag_index.sqlite"
$guide = Join-Path $DocsProjectRoot "LLM_DOCUMENTATION_GUIDE.md"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$outputPath = Join-Path $outputRoot "rag_answer_$timestamp.md"

if (-not (Test-Path $python)) {
    throw "Python environment not found: $python"
}

if (-not (Test-Path $generator)) {
    throw "RAG generator not found: $generator"
}

if (-not (Test-Path $db)) {
    throw "RAG index not found: $db. Build it from the docs scraper project first."
}

$fullTask = @"
$Task

Project context:
- Standalone NinjaTrader optimizer/fitness project.
- Build with MSBuild, not dotnet build.
- Prioritize NinjaTrader docs for References Optimizer and References Optimization Fitness.
- Follow documentation guide: $guide
- If the retrieved docs do not prove an API exists, say what is missing instead of guessing.
"@

$argsList = @(
    $generator,
    "--db", $db,
    "--model", $Model,
    "--embed-model", $EmbedModel,
    "--top-k", "$TopK",
    "--task", $fullTask,
    "--output", $outputPath
)

if ($ExistingCodeFile) {
    $argsList += @("--existing-code-file", $ExistingCodeFile)
}

if ($CompilerErrorsFile) {
    $argsList += @("--compiler-errors-file", $CompilerErrorsFile)
}

Push-Location $DocsProjectRoot
try {
    & $python @argsList
} finally {
    Pop-Location
}

Write-Host "Saved RAG answer to $outputPath"
