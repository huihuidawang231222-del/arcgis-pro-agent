param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
& $Dotnet publish (Join-Path $projectRoot 'Desktop\Desktop.csproj') -c Release --no-self-contained -o (Join-Path $projectRoot 'dist\ArcGIS-Agent') --nologo -v minimal
if($LASTEXITCODE -ne 0){throw 'Desktop build failed'}
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'ArcGIS Agent.lnk'))
$shortcut.TargetPath=Join-Path $projectRoot 'dist\ArcGIS-Agent\ArcGIS-Agent.exe'
$shortcut.WorkingDirectory=$projectRoot
$shortcut.Description='ArcGIS Pro 自然语言制图助手'
$shortcut.Save()
Write-Output $shortcut.TargetPath
