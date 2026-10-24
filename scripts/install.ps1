$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$source=Join-Path $projectRoot 'dist\ArcGISPro-Codex.esriAddinX'
if(!(Test-Path -LiteralPath $source)){throw 'Build the add-in first.'}
$destination=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'ArcGIS\AddIns\ArcGISPro\{3C50E896-CB94-4876-B70B-20E6463018B2}'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$target=Join-Path $destination 'ArcGISPro-Codex.esriAddinX'
Copy-Item -LiteralPath $source -Destination $target -Force
if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash){throw 'Installed package hash mismatch'}
Write-Output $target
Write-Output 'Save your work and restart ArcGIS Pro to load the independent Codex add-in.'
