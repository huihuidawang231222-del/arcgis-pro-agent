$ErrorActionPreference='Stop'
$pluginRoot=Split-Path -Parent $PSScriptRoot
$candidates=@()
foreach($regPath in @('HKLM:\SOFTWARE\ESRI\ArcGISPro','HKCU:\SOFTWARE\ESRI\ArcGISPro')){
 if(Test-Path -LiteralPath $regPath){$pro=(Get-ItemProperty -LiteralPath $regPath -ErrorAction SilentlyContinue).InstallDir;if($pro){$candidates+=Join-Path $pro 'bin\Python\envs\arcgispro-py3\python.exe'}}
}
$candidates+=@("$env:ProgramFiles\ArcGIS\Pro\bin\Python\envs\arcgispro-py3\python.exe")
$python=$candidates|Where-Object {Test-Path -LiteralPath $_}|Select-Object -First 1
if(!$python){[Console]::Error.WriteLine('ArcGIS Pro Python runtime not found. Install ArcGIS Pro on this Windows computer.');exit 1}
& $python -X utf8 (Join-Path $pluginRoot 'mcp\server.py')
exit $LASTEXITCODE
