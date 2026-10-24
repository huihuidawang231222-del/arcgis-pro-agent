$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$pluginRoot=Join-Path $projectRoot 'plugins\arcgis-pro-map'
New-Item -ItemType Directory -Path (Join-Path $pluginRoot 'mcp'),(Join-Path $pluginRoot 'assets') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'mcp\server.py'),(Join-Path $projectRoot 'mcp\analysis-schema.json') -Destination (Join-Path $pluginRoot 'mcp') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'AddIn\Icon.png') -Destination (Join-Path $pluginRoot 'assets\icon.png') -Force
$codex=(Get-Command codex -ErrorAction Stop).Source
& $codex plugin marketplace add $projectRoot --json
if($LASTEXITCODE -ne 0){throw 'Failed to register ArcGIS plugin marketplace'}
& $codex plugin add 'arcgis-pro-map@arcgis-local' --json
if($LASTEXITCODE -ne 0){throw 'Failed to install ArcGIS plugin'}
