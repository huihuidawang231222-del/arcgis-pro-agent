# ArcGIS Pro Agent

[中文教程](docs/GUIDE.zh-CN.md) · [English guide](docs/GUIDE.en.md) · [Architecture / 架构](docs/ARCHITECTURE.md) · [Validation / 验证](docs/VALIDATION.md)

[0.5.0 更新与验收状态 / Update and acceptance status](docs/UPDATE-0.5.0.md)：新版完整成图复测尚未通过 / Full mapping acceptance remains pending.

用自然语言描述制图目标，让 Agent 通过 MCP 操作本机 ArcGIS Pro：读取数据、空间分析、符号化、标注、导出 PNG/PDF。支持 Codex 插件、DeepSeek Harness 插件及可选的原生 Windows 聊天界面。

Describe a mapping goal in natural language. An agent inspects data, runs supported analysis, styles layers, labels features and exports PNG/PDF through a local ArcGIS Pro MCP bridge. Use the Codex plugin, DeepSeek Harness plugin, or optional native Windows interface.

```text
Natural language / 自然语言
        ↓
Agent host + model / Agent 引擎与模型
        ↕ MCP (stdio)
Local bridge / 本机连接桥
        ↕ Windows named pipe
ArcGIS Pro SDK → analysis → cartography → PNG / PDF
```

## Quick start / 快速开始

Windows x64, licensed ArcGIS Pro **3.0.x**, .NET SDK **6.0.428**, Python **3.9+**. Other Pro versions require matching SDK references/frameworks and fresh testing. ArcGIS software and analysis extension licenses are not included. This is an experimental community project, not an Esri, OpenAI or DeepSeek product.

需要 Windows x64、已授权的 ArcGIS Pro **3.0.x**、.NET SDK **6.0.428**、Python **3.9+**。其他 Pro 版本需要匹配 SDK/框架并重新测试；不附带 ArcGIS 软件或扩展许可。项目处于实验阶段，并非厂商官方产品。

```powershell
git clone https://github.com/huihuidawang231222-del/arcgis-pro-agent.git
cd arcgis-pro-agent
# Set -ProDir to your actual installation / 改成实际安装目录
.\scripts\build.ps1 -ProDir 'C:\Program Files\ArcGIS\Pro'
.\scripts\install.ps1
# Save and restart Pro, open a 2D map / 保存并重启 Pro，打开二维地图
.\scripts\install-codex-plugin.ps1
```

In a new Codex chat, select the plugin and ask: **“Connect to ArcGIS Pro and inspect the active map.”** A real map response proves the bridge connection; a plugin card alone does not.

在新 Codex 聊天中选择插件，先说：**“连接 ArcGIS Pro，检查当前地图。”** 收到实际地图回执才代表连接成功；仅出现插件卡片不能证明已连通。

For Harness, run the preparation step in the guide, then paste the generated plugin directory into **Plugins → Add plugin**. Keep that directory in place. See the complete bilingual guides for installation, API settings, example workflows, diagnostics and development.

Harness 需按教程生成本机配置，再在 **插件 → 添加插件** 中填写插件目录并安装；安装后保留该目录。双语教程包含完整安装、API 配置、演示、故障排查和开发方法。

## Capabilities / 能力

- 14 MCP tools, 46 allowlisted processing operations: vector overlay, projection, raster terrain/hydrology, density, interpolation and raster arithmetic.
- Autonomous observation → tool calls → result inspection → styling → export, rather than asking users to author plan JSON.
- Codex/Harness manage their own accounts; optional API mode supports **Chat Completions compatible** APIs. It does not support every vendor protocol.
- Local named pipes restricted to the current Windows user; process/map/snapshot checks guard against stale targets.

- 14 个 MCP 工具、46 项受控处理：矢量叠置、投影、栅格地形/水文、密度、插值及栅格运算。
- Agent 自主观察、调用工具、检查结果、符号化和导出，用户无需编写计划 JSON。
- Codex/Harness 沿用宿主账户；可选 API 模式支持 **Chat Completions 兼容协议**，不代表所有厂商协议均已适配。
- 命名管道限当前 Windows 用户，使用进程、地图和数据快照约束执行目标。

**Limits / 限制:** ArcMap is unsupported. Some tools need ArcGIS extensions. Drawn graphics are temporary overlays; analysis outputs can be persistent datasets. Cancellation is not rollback. Maps need human review. Long-term trend statistics and universal cartographic automation are not implemented. Successful build/offline tests do not establish that every operation works on production data.

不支持 ArcMap；部分工具需要扩展许可。绘图是临时叠加，分析可产生持久数据。取消不等于撤销。地图需人工复核；长期趋势统计及通用全自动制图尚未完成。构建及离线测试通过不等于所有操作已在生产数据实测。

## Source / 源码

| Directory | Purpose / 用途 |
| --- | --- |
| `AddIn`, `Core` | Pro SDK bridge, validation / Pro 连接桥与校验 |
| `mcp` | stdlib Python MCP server / 标准库 MCP 服务 |
| `plugins/arcgis-pro-map` | Codex plugin / Codex 插件 |
| `harness/dsh-plugin-arcgis-pro` | Harness bundle / Harness 插件 |
| `agent`, `Desktop` | optional engines and UI / 可选引擎及界面 |
| `Tests`, `scripts` | build and checks / 构建与检查 |

## License and privacy / 许可证与隐私

[MIT](LICENSE) for this project's code; see [THIRD_PARTY.md](THIRD_PARTY.md) for separately licensed dependencies. Credentials, account profiles, private conversations, local test data and run logs are excluded. Never commit API keys, including encrypted credential blobs. API calls may send selected map metadata and user prompts to the chosen provider; review its data policy.

项目代码使用 [MIT](LICENSE)，外部软件许可见 [THIRD_PARTY.md](THIRD_PARTY.md)。仓库不包含个人密钥、账户配置、聊天记录、本机测试数据或运行日志。加密密钥文件同样不应提交。API 调用可能向所选厂商发送地图元信息和用户提示，请自行核对其数据政策。
