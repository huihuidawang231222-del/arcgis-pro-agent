# Validation scope / 验证范围

This public source export is based on a locally developed experimental integration. Historical development checks exercised a synthetic terrain workflow with real model/tool calls and PNG/PDF delivery, plus read-only map access through Codex and a newly created Harness chat. Raw conversation logs, private project paths and screenshots are deliberately excluded. These historical observations are not independently reproducible evidence for every capability.

公开源码来自本机实验性开发版本。此前开发曾在合成工程运行真实模型/工具完成地形图及 PNG/PDF，并通过 Codex 与 Harness 新聊天读取地图。原始对话日志、私有路径和截图未公开；这些历史观察不能作为所有功能的独立复现证明。

## Reproducible checks / 可复现检查

Public-export check on 2026-10-24: bridge/transport build succeeded with zero warnings and errors; 18 offline checks passed; the optional desktop application compiled; three MCP protocol calls passed without live-map modification; five compatibility assertions passed; 16 Python sources and 10 JSON files parsed; all 18 checked local documentation links resolved. A tracked-source scan found no common credential patterns, and a local exact-match audit found no occurrence of saved API credentials. These are source/build checks, not a new end-to-end mapping acceptance run.

2026-10-24 公开版检查：桥接与传输器零警告、零错误构建；18 项离线检查通过；桌面程序编译成功；3 次不修改地图的 MCP 协议调用通过；5 项兼容性断言通过；16 个 Python 文件和 10 个 JSON 文件解析通过；18 个本地文档链接全部有效。跟踪源码未检出常见密钥格式，与本机已保存 API 密钥的精确比对未发现匹配。这些属于源码/构建验证，不是新一轮端到端成图验收。

- `scripts/build.ps1`: compile transport, Pro add-in and checks; execute validation/transport fixtures and export the operation schema. Requires the stated Pro installation and .NET SDK.
- `mcp/check.py` without `--pid`: protocol initialization, tool discovery and session discovery, with no model request or map modification.
- `scripts/test-model-compatibility.ps1`: offline thinking-parameter compatibility checks using a built .NET assembly and matching PowerShell runtime.
- `scripts/scan-public-source.py`: conservative scan of Git-tracked public files for common credential formats and forbidden private artifacts. This is a supplementary check, not proof that all secrets are absent.

- `scripts/build.ps1`：编译组件并运行校验/传输夹具，导出操作定义；需要指定 Pro 和 SDK。
- 无 `--pid` 的 `mcp/check.py`：初始化、工具发现和会话发现，不请求模型、不修改地图。
- `scripts/test-model-compatibility.ps1`：离线检查思考参数兼容性，需构建程序集及匹配 PowerShell 运行时。
- `scripts/scan-public-source.py`：对 Git 跟踪文件检查常见凭据格式和禁止公开的本机文件，作为补充检查，不保证发现所有秘密。

## Not established / 尚不能声称

- Every one of the 39 operations individually accepted on production data.
- Compatibility with all Pro versions, client releases, model vendors or proxy setups.
- Human visual review of every automatically exported map.
- Long-term trend analysis, significance tests, arbitrary code execution or complete rollback.

- 39 项操作均已在生产数据逐项验收。
- 兼容所有 Pro/客户端版本、模型厂商或代理组合。
- 每张自动导出地图都已人工目视验收。
- 已实现长期趋势、显著性检验、任意代码执行或完整回滚。

Use isolated test projects. Verify units, CRS, licensing, job completion, actual output statistics and map appearance before production use.

请在独立测试工程中验证单位、坐标系、许可、任务完成状态、实际统计和地图视觉效果，再用于生产。
