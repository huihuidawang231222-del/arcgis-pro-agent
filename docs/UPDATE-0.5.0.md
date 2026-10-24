# ArcGIS Pro Agent 0.5.0

## 中文

此次更新解决中断后找不到会话、FlashX 思考参数不兼容，以及模型未检查工具便误报能力缺失的问题。Pro 桥接升级至 0.5.0，受控处理操作从 39 项增加到 46 项；Codex/Harness 插件的版本号独立管理。

| 新操作 | 用途与限制 |
| --- | --- |
| `select_values` | 根据已核实的文本类别列表筛选；不把所有 POI 默认视为商业设施。最多 50 个类别，文本按字面值转义。 |
| `raster_normalize` | 读取真实最小/最大值，独立转换为 0–1，支持正负方向；常量返回 0，保留 NoData，先转浮点避免整数除法。 |
| `raster_scale` | 乘有限常数以应用权重，不执行模型提供的表达式。 |
| `create_grid` | 按当前米制图层范围创建面网格，最多 5 万单元；需另行裁剪至实际边界。 |
| `feature_density` | 复制面要素后计算 `AG_AREA_KM2` 和 `AG_DENS`；使用实际面面积，源数据保留。 |
| `global_moran` | 原生解析报告与固定种子的 999 次全局置换检验分别返回；置换最多 5000 单元，欧氏 K 近邻、行标准化，同距按对象 ID 排序。不得混配两种检验的 I/p 值。 |
| `local_moran` | 原生 LISA，999 次置换和 FDR 校正；设置随机种子，重复运行的可重现性仍需实测。HH/LL/HL/LH 使用分类符号。 |

桌面界面新增“继续上次任务”。测试模型不再清空任务目录；检查点通过临时文件替换写入，同一 Pro 进程和地图可从磁盘恢复。自动跨 Pro 重启匹配仍不支持，本次重启后的测试迁移是人工核对工程和地图后进行的，不代表通用自动恢复能力。

API 请求压缩过期工具目录及重复字段元数据，原始回执完整留在本机。显示输入/输出/缓存 Token 用量，不能直接视为账单。默认本地决策轮次仍不限；不会自动重试资源不足或自动切换模型。Flash/FlashX 资源包可能限定模型，并非永久免费的标准 API。

### 升级与验证

按主教程重新运行 `scripts/build.ps1` 和 `scripts/install.ps1`，保存工程并重启 Pro，然后刷新连接。`list_sessions` 应返回桥接 0.5.0；`get_analysis_context` 应返回 46 项操作。运行插件安装脚本同步 Codex/Harness 中打包的 MCP 文件。

34 项 .NET 检查、23 项宿主检查及模型参数兼容检查已通过。包含已知完全图 Moran I、聚集样本及固定种子重现、常量/缺失值拒绝、单位和参数校验。新版已在本机加载并发现 46 项操作；**完整的插件界面 → 厂商 API → 新分析工具 → 地图验收尚未完成，仍不通过**。此前旧桥接上的实际界面续跑导出不能代替本版新工具的实测。

私有工程、地图、运行记录、账号配置和 API 密钥不在仓库中。

## English

This update fixes lost continuation pointers after model tests, FlashX thinking compatibility, and capability descriptions that led the model to claim raster statistics were unavailable without inspecting them. The Pro bridge is now 0.5.0 with 46 allowlisted operations, up from 39. Codex and Harness plugin versions are managed separately.

New operations provide literal category filtering (`select_values`), independent 0–1 min–max normalization (`raster_normalize`), constant weights (`raster_scale`), projected grids (`create_grid`), polygon-area densities (`feature_density`), global Moran reports and permutations (`global_moran`), and native LISA with 999 permutations and FDR (`local_moran`). Sources are preserved; generated grids require boundary clipping. Grid size is capped at 50,000 cells; global permutations at 5,000 observations.

Global analytic and permutation results are distinct. The permutation test uses Euclidean directed K-nearest neighbors, row standardization, ID-ordered distance ties and a seeded two-sided comparison with expected I. Native GP tie handling may differ: do not combine its analytic p-value with the custom permutation I. Local LISA seed reproducibility still needs repeat-run validation.

The native UI now has **Resume last task**. Model tests retain checkpoints, and atomic file replacement reduces checkpoint corruption. Recovery matches the same Pro process and map; automatic matching after restarting Pro is not supported. This test's restart migration was manually verified against the original project and map.

Requests compact expired catalogs and repeated field metadata while retaining complete local receipts. Token counts are usage, not a bill. Decision rounds remain unlimited by default. Resource errors do not trigger automatic retries or model switching. Flash/FlashX promotional resources can be model-specific; the standard API is not permanently free.

Rebuild, install, save and restart Pro, then refresh the connection. Confirm bridge 0.5.0 and 46 discovered operations. Re-run the plugin installers to synchronize packaged MCP files. See the bilingual setup guides for commands.

34 .NET checks, 23 host checks and model compatibility checks passed. The bridge was loaded locally and its 46 operations were discovered. **Full UI → provider API → new analysis → map acceptance remains pending and is not passed.** Earlier real UI exports on the old bridge do not validate these new operations. Private projects, outputs, execution logs, account configuration and API keys are excluded.
