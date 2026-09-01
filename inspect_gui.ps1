$assembly = [Reflection.Assembly]::LoadFrom('C:\Program Files\NinjaTrader 8\bin\NinjaTrader.Gui.dll')
$type = $assembly.GetType('NinjaTrader.Gui.NinjaScript.StrategyAnalyzer.StrategyAnalyzerTabControl')
if ($type) {
    Write-Host "--- StrategyAnalyzerTabControl Constructors ---"
    $type.GetConstructors() | ForEach-Object {
        $params = $_.GetParameters() | ForEach-Object { "$($_.ParameterType.FullName) $($_.Name)" }
        "Constructor($($params -join ', '))"
    }
}
