param([string]$ProDir=(Join-Path $env:ProgramFiles 'ArcGIS\Pro'),[string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
 & $Dotnet build agent\transport\ModelTransport.csproj -c Release --nologo -v minimal
 if($LASTEXITCODE -ne 0){throw 'Model transport build failed'}
 & $Dotnet build AddIn\ProCodex.csproj -c Release --nologo -v minimal ('-p:ArcGISProDir='+$ProDir)
 if($LASTEXITCODE -ne 0){throw 'Add-in build failed'}
 & $Dotnet build Tests\Checks.csproj -c Release --nologo -v minimal
 if($LASTEXITCODE -ne 0){throw 'Checks build failed'}
 & $Dotnet Tests\bin\Release\net6.0\Checks.dll
 if($LASTEXITCODE -ne 0){throw 'Checks failed'}
  $schema=& $Dotnet Tests\bin\Release\net6.0\Checks.dll --analysis-schema
 if($LASTEXITCODE -ne 0){throw "Schema export failed"}
 [IO.File]::WriteAllText((Join-Path $projectRoot "mcp\analysis-schema.json"),($schema -join "`n"),[Text.UTF8Encoding]::new($false))
 New-Item -ItemType Directory -Path build,dist -Force | Out-Null
 $staging=Join-Path $projectRoot ('build\'+[guid]::NewGuid().ToString('N'))
 New-Item -ItemType Directory -Path (Join-Path $staging 'Install') -Force | Out-Null
 Copy-Item -LiteralPath AddIn\Config.daml,AddIn\Icon.png -Destination $staging
 Copy-Item -LiteralPath AddIn\bin\Release\net6.0-windows\ProCodex.dll,AddIn\bin\Release\net6.0-windows\CodexBridge.dll -Destination (Join-Path $staging 'Install')
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $package=Join-Path $projectRoot ('dist\ProCodex-'+[guid]::NewGuid().ToString('N')+'.esriAddinX')
 [IO.Compression.ZipFile]::CreateFromDirectory($staging,$package)
 Move-Item -LiteralPath $package -Destination dist\ArcGISPro-Codex.esriAddinX -Force
 Get-FileHash -LiteralPath dist\ArcGISPro-Codex.esriAddinX
}finally{Pop-Location}
