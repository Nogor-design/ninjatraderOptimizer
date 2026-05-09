$assembly = [Reflection.Assembly]::LoadFile('C:\Program Files\NinjaTrader 8\bin\NinjaTrader.Core.dll')
$type = $assembly.GetType('NinjaTrader.NinjaScript.Parameter')
if ($type) {
    Write-Host "--- Parameter Properties ---"
    $type.GetProperties() | ForEach-Object { "$($_.Name) ($($_.PropertyType.FullName))" }
} else {
    Write-Host "Parameter type not found."
}
