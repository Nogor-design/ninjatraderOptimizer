$assembly = [Reflection.Assembly]::LoadFrom('C:\Program Files\NinjaTrader 8\bin\System.Windows.Controls.WpfPropertyGrid.dll')
$assembly.GetTypes() | Select-Object FullName
