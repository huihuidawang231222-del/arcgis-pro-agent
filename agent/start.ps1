param([int]$ProcessId,[string]$Goal,[string]$Profile,[string]$Resume,[ValidateSet('api','codex','deepseek')][string]$Engine='api')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$python=$env:GIS_AGENT_PYTHON
if(!$python){
 foreach($regPath in @('HKLM:\SOFTWARE\ESRI\ArcGISPro','HKCU:\SOFTWARE\ESRI\ArcGISPro')){
  if(Test-Path -LiteralPath $regPath){$pro=(Get-ItemProperty -LiteralPath $regPath).InstallDir;if($pro){$candidate=Join-Path $pro 'bin\Python\envs\arcgispro-py3\python.exe';if(Test-Path -LiteralPath $candidate){$python=$candidate;break}}}
 }
}
if(!$python){$python=Join-Path $env:ProgramFiles 'ArcGIS\Pro\bin\Python\envs\arcgispro-py3\python.exe'}
if(!(Test-Path -LiteralPath $python)){throw 'ArcGIS Pro Python runtime not found. Run agent/run.py using your Python 3.9+ interpreter.'}
if(!$ProcessId){
 $sessions=@(Get-ChildItem -LiteralPath (Join-Path $env:LOCALAPPDATA 'ArcGISProCodex\endpoints') -Filter '*.json' | ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json} | Where-Object {Get-Process -Id $_.processId -ErrorAction SilentlyContinue})
 if($sessions.Count -ne 1){throw 'Multiple or no bridge sessions: specify -ProcessId after checking the intended Pro project.'}
 $ProcessId=$sessions[0].processId
}
$entry=if($Engine -eq 'codex'){'codex.py'}elseif($Engine -eq 'deepseek'){'deepseek.py'}else{'run.py'}
if($Engine -ne 'api' -and ($Profile -or $Resume)){throw 'Profile/Resume apply to the API engine only.'}
$arguments=@('-X','utf8',(Join-Path $PSScriptRoot $entry),'--pid',"$ProcessId")
if($Profile){$arguments+=@('--profile',$Profile)}
if($Resume){$arguments+=@('--resume',$Resume)}
if($Goal){$arguments+=@('--',$Goal)}
& $python @arguments
exit $LASTEXITCODE
