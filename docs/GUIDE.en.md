# ArcGIS Pro Agent installation and usage guide

[中文](GUIDE.zh-CN.md) · [Home](../README.md)

## 1. Prerequisites

1. Windows x64 and a licensed, signed-in **ArcGIS Pro 3.0.x** installation. Development baseline: Pro 3.0.1 and .NET 6. ArcMap is unsupported.
2. **.NET SDK 6.0.428**. Check `dotnet --list-sdks`; a runtime alone cannot build the project. `global.json` pins the SDK. Newer Pro versions can require different frameworks and references and need separate validation.
3. Python **3.9+** for MCP; Pro's Python is convenient. Only the Harness preparation script requires PyYAML (`yaml`). The MCP server itself uses Python's standard library.
4. A working, signed-in Codex or DeepSeek Harness client. Client subscriptions, plugin support and quotas are controlled by their vendors. This project supplies neither credits nor shared accounts.
5. Check Pro extension licenses before requesting terrain, hydrology or density tools. Some operations require Spatial Analyst or other licensed capabilities. The agent must report real licensing failures.

Run PowerShell commands from the repository root. Replace the installation path below with your own.

```powershell
git clone https://github.com/huihuidawang231222-del/arcgis-pro-agent.git
cd arcgis-pro-agent
$proDir = 'C:\Program Files\ArcGIS\Pro'
$proPython = Join-Path $proDir 'bin\Python\envs\arcgispro-py3\python.exe'
dotnet --list-sdks
& $proPython --version
```

Acceptance: SDK 6.0.428 is available, Python starts, and Pro opens a 2D map.

## 2. Build and install the Pro bridge

```powershell
.\scripts\build.ps1 -ProDir $proDir -Dotnet 'dotnet'
.\scripts\install.ps1
```

The build compiles the model transport, add-in and checks, runs existing offline checks, exports the processing schema and creates `dist\ArcGISPro-Codex.esriAddinX`. ArcGIS assemblies are referenced from your installation and are not copied into the package. A full build requires the correct ArcGIS installation and SDK.

Installation copies the add-in to your Documents directory under `ArcGIS\AddIns\ArcGISPro\{3C50E896-CB94-4876-B70B-20E6463018B2}` and checks its hash. Save your work, exit and restart Pro, then open a 2D map. The bridge loads automatically; it has no in-Pro chat panel.

If organizational policy blocks unsigned add-ins, follow your administrator's approved process. Do not disable security policy to bypass restrictions.

Acceptance: Pro recognizes the add-in; `list_sessions` discovers a live Pro process and `get_active_map` returns its actual map. A copied file or installation message alone does not establish connectivity.

## 3. Codex plugin

Ensure `Get-Command codex` finds your installed CLI, then run:

```powershell
.\scripts\install-codex-plugin.ps1
```

The installer synchronizes bundled MCP files and the icon, registers the local marketplace at `.agents/plugins/marketplace.json`, and installs `arcgis-pro-map@arcgis-local`. It updates local Codex plugin configuration; it does not upload your GIS data. The Codex plugin is version 0.4.1 and the Pro bridge is 0.4.0; these are versioned separately.

Find **ArcGIS Pro 自然语言制图** on the plugin page. Open a new chat and select the plugin. Refresh the plugin list or restart the client if tools are unavailable. The MCP name is `arcgis-pro-map`. Avoid registering duplicate MCP entries for the same bridge.

Start with:

> Connect to ArcGIS Pro, list available projects and inspect the target project's active map and layers. Read-only checks first; do not run analysis yet.

Verify the target even when only one instance exists. Select explicitly when several instances are open. Actual map, layer and coordinate-system responses establish connectivity. Codex manages its own model login; this plugin does not require a separate API key.

## 4. DeepSeek Harness plugin

The package declares Harness compatibility `>=0.2.0-0 <0.3.0`; test other versions separately. Generate configuration for the current computer:

```powershell
& $proPython -c 'import yaml; print(yaml.__version__)'
# If unavailable, install PyYAML in an appropriate Python environment.
# Do not modify Pro's managed environment blindly.
& $proPython .\scripts\prepare-harness-plugin.py
(Resolve-Path .\harness\dsh-plugin-arcgis-pro).Path
```

If you use another Python 3.9+ interpreter, that interpreter becomes the MCP launch command. It must support Windows named-pipe access and have PyYAML available. The generated `cordis.patch.yml` contains local paths and is Git-ignored.

In Harness, open **Plugins → Add plugin**, paste the full directory printed above, install and enable it. The card is named **ArcGIS Pro 自然语言制图**. Start a new chat and use the read-only connection prompt above. This uses Harness's account/model directly; no standalone EXE or configuration copied from another chat is required.

Keep the plugin directory in place. If you move it or change computers, regenerate configuration and reinstall. Do not distribute another computer's generated configuration as a universal package. `--migrate-overlay` is only for users of this project's earlier managed manual MCP block; a new installation does not need it. Migration validates and backs up the original configuration and refuses ambiguous edits.

Acceptance has two layers: the plugin card appears and its component runs; a new chat actually calls `list_sessions` and `get_active_map` and reads the intended map. The first does not prove the second.

## 5. Optional native Windows UI and API mode

```powershell
.\scripts\build-desktop.ps1 -Dotnet 'dotnet'
.\dist\ArcGIS-Agent\ArcGIS-Agent.exe
```

The script publishes a WinForms application and creates a current-user desktop shortcut. Keep the complete repository and publish directory; moving the EXE alone will not work. The UI provides target selection, connection status, engine selection, execution records, map artifacts and API settings.

Python discovery checks `GIS_AGENT_PYTHON`, the Pro registry installation path and the standard installation directory. For a custom environment:

```powershell
$env:GIS_AGENT_PYTHON = $proPython
$env:GIS_AGENT_DOTNET = (Get-Command dotnet).Source
```

Select the vendor API engine, open **API密钥与模型设置**, and enter a profile name, full HTTPS Chat Completions URL, the vendor's model ID and a valid complete API key. Save and click **测试模型**. The password field is masked; leaving an existing key blank preserves it. Never send keys in chat or place them in source files.

Credentials are stored only at `%LOCALAPPDATA%\ArcGISProAgent\providers.json`, encrypted using current-user Windows DPAPI. Never commit that file. API mode currently supports Chat Completions compatible protocols, not every vendor's native protocol. Consult the vendor console for current model availability, pricing, credits and permissions.

For Zhipu, standard API and Coding subscriptions require matching credentials/endpoints. An API Key ID is not the complete key. Compatibility logic adjusts thinking parameters for `glm-5.3-flash` on the official Zhipu endpoint; this does not guarantee your account has model access. HTTP 401 means authentication failed: check the complete key, account and endpoint. For 429, inspect the provider business code for rate/quota information. Changing model names does not repair invalid credentials.

System-proxy use can be toggled locally. Direct connections worked in the development environment; forcing the system proxy previously produced a TLS EOF. Not every proxy combination is verified. Keep certificate validation enabled.

In API mode, the host polls background jobs without spending extra model decisions. Subsequent messages continue available history for the same Pro process and map; changing the map/process starts fresh context. Close the old Agent window and reopen after updates. If the bridge build check reports a mismatch, save the project and restart Pro.

Each task defaults to 60 model decisions. This is a local agent safeguard, not a vendor account quota of 60 requests. Reaching it pauses normally with an orange continuation notice and preserves outputs/history. Send “continue” when the continuation conditions above hold, or use CLI `--resume`. Progress shows the current round and limit. Authentication, quota and network failures remain distinct errors.

## 6. Natural-language mapping demonstrations

Load trusted data into Pro and establish the study area, elevation units and coordinate system. Start with a copy or synthetic project.

**Terrain map:** load a DEM and study-area boundary, then ask:

> Create a terrain map from the current DEM and study area. First inspect elevation units, CRS and NoData. Clip if needed, choose appropriate terrain processing and colors, add contours and useful labels, and export an A4 PNG/PDF with title, legend, scale, north arrow and truthful data sources. Preserve source data. Ask only when important missing information changes the result.

**Road density:** load roads and a study-area boundary, then ask:

> Create a road density map. Verify line and boundary inputs, choose a metric projection, explain the method, cell size and search radius, inspect output statistics and extent, classify appropriately and export PNG/PDF. Report missing licenses or ambiguous distance units before proceeding.

**County temperature:** load county polygons and a temperature raster with known year/month/units, then ask:

> Summarize the selected year's selected month's mean temperature by county, check fields and NoData, then create a thematic map with units, date and source. Do not treat long-term climate normals as observations for a particular year.

`zonal_statistics` supports zonal calculations. Whether the final thematic style is achievable depends on available tool responses; arbitrary joins and every thematic renderer are not guaranteed. Long-term trend regression, significance testing and arbitrary charts are not implemented.

The agent uses structured tool parameters internally; users describe goals in ordinary language. Inspect actual job states, artifact paths and maps. Missing data or licenses must result in a clear incomplete status, not invented results. Human review is required for semantics, visual quality and publication.

## 7. Diagnostics and validation

| Symptom | Action |
| --- | --- |
| No Pro sessions | Check add-in installation, restart Pro, open a 2D map, check Windows user |
| Multiple Pro instances | Choose the intended project/process; do not reuse historical PIDs |
| Stale mapUri/snapshot | Read the map/context again instead of resubmitting stale requests |
| Analysis failure | Read actual `get_analysis_job` errors, licenses, input types and units |
| No output | Wait for actual completed state, then inspect outputs and export receipts |
| Files remain after stop | Expected: cancellation is not transactional rollback |
| Drawings disappear after restart | `draw_graphics` creates temporary overlays, not permanent features |
| Plugin card exists but tools fail | Check MCP status and an actual map response in a new chat |

Run protocol checks without requesting a model:

```powershell
& $proPython .\mcp\check.py
```

This checks discovery/protocol behavior and writes local records under ignored `outputs/`. It does not prove every spatial operation. `--pid <currentPID>` creates synthetic temporary graphics in the selected map: use a test project only. `--clear` clears that bridge's overlays. Never copy a historical PID.

Build checks:

```powershell
.\scripts\build.ps1 -ProDir $proDir
# Reflection checks require PowerShell 7 / a matching .NET runtime
pwsh -NoProfile -File .\scripts\test-model-compatibility.ps1
```

See [VALIDATION.md](VALIDATION.md). One successful map does not establish production acceptance for all 39 operations.

## 8. Development, updates and uninstall

New processing operations require updating the allowlist in `Core`, Pro execution code, input validation and actual-output checks. Building refreshes `mcp/analysis-schema.json`. Rerun plugin synchronization/preparation to update both bundled schemas. Do not replace the allowlist with arbitrary Python/Shell/SQL execution.

After updating the Pro bridge, save and restart Pro. Reinstall or refresh Codex/Harness plugins using supported client controls. Do not overwrite loaded bridge assemblies while Pro is running.

To uninstall, remove the plugin in the relevant client. Save and close Pro, then remove this bridge's `.esriAddinX` from its GUID directory. Back up and manage local model settings/logs separately. Removing a plugin does not delete GIS source data.

Before contributing, run `python scripts/scan-public-source.py` and inspect `git diff --cached`. Exclude credentials, account settings, logs and business data. Submit reproducible steps, versions and sanitized errors rather than private projects.
