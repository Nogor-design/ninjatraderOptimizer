$assembly = [Reflection.Assembly]::LoadFrom('C:\Program Files\NinjaTrader 8\bin\NinjaTrader.Gui.dll')
$type = $assembly.GetType('NinjaTrader.Gui.TradePerformance.DisplayModeSelector')
if ($type) {
    Write-Host "--- DisplayModeSelector Members ---"
    $type.GetMethods() | Select-Object Name | Sort-Object | Get-Unique
    $type.GetProperties() | Select-Object Name
}
