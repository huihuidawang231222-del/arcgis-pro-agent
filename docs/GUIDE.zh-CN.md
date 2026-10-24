# ArcGIS Pro Agent 中文安装与使用教程

[English](GUIDE.en.md) · [返回首页](../README.md)

## 1. 准备环境

1. 使用 Windows x64，安装并登录已授权的 **ArcGIS Pro 3.0.x**。本项目的开发基线是 Pro 3.0.1 / .NET 6，不支持 ArcMap。
2. 安装 **.NET SDK 6.0.428**。在终端运行 `dotnet --list-sdks` 检查；只有 runtime 不能编译。根目录 `global.json` 指定 SDK。新版 Pro 可能需要不同目标框架与程序集，不能直接宣称兼容。
3. Python 3.9+ 可运行 MCP；推荐使用 Pro 自带 Python。只有生成 Harness 安装配置的脚本需要 `yaml`（PyYAML）；独立 MCP 不需要额外 pip 包。
4. 选择已登录、可正常回复的 Codex 或 DeepSeek Harness 客户端。账户订阅、支持的插件接口及额度由客户端厂商管理。本项目不提供免费额度、不共享账户。
5. 坡度、水文、密度等操作可能需要 Spatial Analyst 等扩展。请先在 Pro 核实授权。没有授权时，Agent 应报告实际工具错误。

所有 PowerShell 命令均在仓库根目录执行。以下 `C:\Program Files\ArcGIS\Pro` 是示例，请替换为实际安装目录。

```powershell
git clone https://github.com/huihuidawang231222-del/arcgis-pro-agent.git
cd arcgis-pro-agent
$proDir = 'C:\Program Files\ArcGIS\Pro'
$proPython = Join-Path $proDir 'bin\Python\envs\arcgispro-py3\python.exe'
dotnet --list-sdks
& $proPython --version
```

验收：SDK 包含 6.0.428，Python 能启动，Pro 能正常打开二维地图。

## 2. 构建并安装 Pro 连接桥

```powershell
.\scripts\build.ps1 -ProDir $proDir -Dotnet 'dotnet'
.\scripts\install.ps1
```

构建脚本会编译模型传输器、Pro 插件和检查程序，运行现有离线检查，生成分析工具定义，并打包 `dist\ArcGISPro-Codex.esriAddinX`。它引用本机 ArcGIS 程序集，不复制 ArcGIS DLL。没有 ArcGIS 或正确 SDK 时无法完整构建。

安装脚本复制到当前用户文档目录的 `ArcGIS\AddIns\ArcGISPro\{3C50E896-CB94-4876-B70B-20E6463018B2}`，并核对哈希。先保存正在编辑的工程，然后自行退出并重启 Pro，打开二维地图。此连接桥自动加载，Pro 内不提供聊天面板。

如机构策略禁止未签名加载项，请由管理员按机构规范处理。不要关闭安全策略来绕过限制。

验收：Pro 的加载项管理器可识别桥接包；客户端调用 `list_sessions` 能找到实际 Pro 进程，随后 `get_active_map` 返回当前地图。只看到文件或安装提示不代表连接成功。

## 3. Codex 插件入口

先确保 `Get-Command codex` 能找到已安装的 Codex CLI，然后运行：

```powershell
.\scripts\install-codex-plugin.ps1
```

脚本同步本仓库 MCP 文件和图标，注册 `.agents/plugins/marketplace.json` 本地市场，并安装 `arcgis-pro-map@arcgis-local`。它会更新 Codex 的本地插件配置，不上传你的数据。插件版本 0.4.1，Pro 桥接版本 0.4.0，分别管理。

在 Codex 的插件页面找到 **ArcGIS Pro 自然语言制图**。开启一个新聊天并选用插件；若工具没有加载，刷新插件列表或重启客户端。插件名是 `arcgis-pro-map`。不要同时注册多个指向同一桥接的 MCP 条目，以免混淆。

先输入：

> 连接 ArcGIS Pro，列出当前可用工程，读取目标工程的活动地图和图层。只读检查，先不执行分析。

只有一个实例也应核对工程；多个实例必须明确选择目标。收到地图名称、图层及坐标系的真实工具回执，才说明 MCP 到 Pro 已连通。Codex 自己负责模型登录；此插件不需要另填 API Key。

## 4. DeepSeek Harness 插件入口

该插件适配的包声明为 Harness `>=0.2.0-0 <0.3.0`，其他版本需复测。先生成当前电脑的绝对路径配置：

```powershell
& $proPython -c 'import yaml; print(yaml.__version__)'
# 如缺少 yaml，请在合适的 Python 环境安装 PyYAML；勿盲目改坏 Pro 环境。
& $proPython .\scripts\prepare-harness-plugin.py
(Resolve-Path .\harness\dsh-plugin-arcgis-pro).Path
```

如果改用其他 Python 3.9+ 解释器运行准备脚本，其路径会成为 MCP 启动解释器；该解释器需支持 Windows 命名管道访问，且能导入 PyYAML。生成的 `cordis.patch.yml` 是本机配置，已被 Git 忽略。

打开 Harness → **插件 → 添加插件**，粘贴上一步输出的完整目录路径，点击安装并启用。列表中应出现 **ArcGIS Pro 自然语言制图**。新建聊天，使用上一节的只读连接提示测试。此时沿用 Harness 自己的账户/模型，不需要独立 EXE，也不需要从其他会话复制 MCP 配置。

保留插件目录；移动仓库或更换电脑后重新生成配置并重新安装。不要把旧电脑生成的配置当作通用安装包分发。仅当你确实使用本项目旧版受管理的手工 MCP 块时，才考虑 `--migrate-overlay`；普通新安装无需该参数。迁移脚本会校验并备份原配置，无法唯一识别则拒绝修改。

验收分两层：插件卡片出现且组件运行；新聊天实际调用 `list_sessions` / `get_active_map` 并读到正确地图。前者不代替后者。

## 5. 可选原生 Windows 界面与 API 模式

如果偏好独立界面：

```powershell
.\scripts\build-desktop.ps1 -Dotnet 'dotnet'
.\dist\ArcGIS-Agent\ArcGIS-Agent.exe
```

脚本发布 WinForms 界面并创建当前用户桌面快捷方式。保留整个仓库和发布目录，不能只拷走 EXE。界面含目标工程、连接状态、引擎选择、运行记录、成果入口及密钥设置。

Python 优先读取 `GIS_AGENT_PYTHON`，其次检查 Pro 安装注册表和标准目录。非标准环境可设置：

```powershell
$env:GIS_AGENT_PYTHON = $proPython
$env:GIS_AGENT_DOTNET = (Get-Command dotnet).Source
```

选 **厂商 API** → **API密钥与模型设置**，填写配置名、完整 HTTPS Chat Completions 地址、厂商公布的模型 ID 和有效的完整 API 密钥，保存后点击 **测试模型**。密钥框掩码显示；已有密钥留空可保留，不要发送到聊天或写入源码。

设置仅保存在 `%LOCALAPPDATA%\ArcGISProAgent\providers.json`，使用当前 Windows 用户 DPAPI 加密；不要提交该文件。该模式目前只实现 Chat Completions 兼容协议，不能直接接入任意厂商私有协议。模型、价格、可用额度与权限以厂商当前控制台为准。

智谱标准 API 与 Coding 套餐需使用匹配的密钥及接口；API Key ID 不能代替完整密钥。代码对官方智谱接口上的 `glm-5.3-flash` 有 thinking 参数兼容处理，这不保证你账户拥有该模型权限。HTTP 401 是鉴权失败，应查密钥、账户与接口；429 应结合业务码查频率或额度。不要反复更换模型名来处理无效密钥。

代理可在本机设置切换是否使用系统代理。开发环境曾验证直连可用，强制系统代理出现过 TLS EOF；没有保证所有代理组合可用。保留证书校验，不要关闭 TLS 验证。

## 6. 自然语言成图演示

先在 Pro 添加可信数据，并确认研究区、高程单位和坐标系。用副本或合成工程首次测试。

**地形图**：添加 DEM 和研究区边界，输入：

> 基于当前 DEM 和研究区制作地形专题图。先核查高程单位、坐标系和 NoData，必要时裁剪，选择合适的地形处理和配色，添加等高线与必要标注，生成含标题、图例、比例尺、指北针和真实数据来源的 A4 地图，导出 PNG/PDF。保留源数据；缺少会影响结果的重要信息时再问我。

**路网密度**：添加道路与研究区边界，输入：

> 制作研究区路网密度图。先确认线数据与边界，选择米制投影并说明方法、像元大小和搜索半径，执行后检查统计与范围，合理分级，导出 PNG/PDF。若缺少许可或无法确认距离单位，先报告问题。

**县域气温专题图**：添加县界及有年份、月份、单位的气温栅格，输入：

> 对指定年份指定月份的平均气温按县域统计，核对字段和 NoData 后制作专题图并注明单位、年月和来源。不要把多年气候平均值当作某年观测。

县级面统计可使用 `zonal_statistics`；最终渲染能否按目标方式完成需依据现有工具回执判断，不能保证任意表连接及所有专题样式。长期趋势回归、显著性检验、任意图表尚未接入。

Agent 内部使用结构化工具参数，但你只需描述目标。检查实际任务状态、产物路径和地图；缺少数据或授权时应给出明确的未完成说明，不应编造结果。地图的语义正确性、视觉效果和对外发布仍需人工复核。

## 7. 排查和验证

| 现象 | 操作 |
| --- | --- |
| 无 Pro 会话 | 确认桥接安装成功、重启 Pro、打开二维地图；核对 Windows 用户 |
| 多个 Pro 实例 | 选择正确工程/进程，不复用旧 PID |
| mapUri 或 snapshot 过期 | 重新读取地图/分析上下文，不能反复提交旧请求 |
| 分析失败 | 查看 `get_analysis_job` 的真实错误、许可、输入类型及单位 |
| 没有输出 | 任务必须实际 completed，再检查输出和导出回执 |
| 停止后文件仍在 | 正常：取消不是事务回滚，已产生结果会保留 |
| 点线面重启后消失 | `draw_graphics` 是临时叠加，不是永久要素 |
| 插件有卡片但不能调用 | 新聊天检查 MCP 运行状态和真实地图响应 |

可在无模型请求的情况下运行协议检查：

```powershell
& $proPython .\mcp\check.py
```

这会检查协议发现等流程，并在被忽略的 `outputs/` 写本机记录；它不证明空间分析全部通过。`--pid <当前PID>` 会在目标地图创建合成临时图形；只在测试工程使用，`--clear` 清理该桥接的叠加。不要把历史 PID 当成当前实例。

构建验证：

```powershell
.\scripts\build.ps1 -ProDir $proDir
# 兼容参数反射测试需要 PowerShell 7 / .NET 运行时匹配
pwsh -NoProfile -File .\scripts\test-model-compatibility.ps1
```

验证范围见 [VALIDATION.md](VALIDATION.md)。39 项操作不能因一次成图成功就视为逐项生产验收。

## 8. 开发、更新、卸载

新增分析操作需同步 `Core` 中受控操作定义、Pro 执行实现、输入校验和实际结果检查。构建会更新 `mcp/analysis-schema.json`；重新运行插件同步/准备脚本，保持两种插件内定义一致。不要改成让模型执行任意 Python/Shell/SQL。

更新 Pro 桥接后保存并重启 Pro；更新 Codex/Harness 包后重新安装或按客户端支持的方式刷新。不要在运行中覆盖正在使用的桥接程序集。

卸载：在客户端插件页面卸载对应插件；保存并关闭 Pro 后移除本桥接 GUID 目录中的 `.esriAddinX`。本机模型配置和日志按需要自行备份后处理；卸载插件不等于删除原始 GIS 数据。

提交代码前运行 `python scripts/scan-public-source.py`、检查 `git diff --cached`，确保不包含密钥、账户配置、运行日志和业务数据。请提交可复现问题、版本与脱敏错误，勿上传私有工程。
