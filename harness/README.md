# DeepSeek Harness

[中文安装教程](../docs/GUIDE.zh-CN.md#4-deepseek-harness-插件入口) · [English installation guide](../docs/GUIDE.en.md#4-deepseek-harness-plugin)

Run `scripts/prepare-harness-plugin.py` using your chosen Windows Python 3.9+ interpreter with PyYAML installed. Then install the resulting `harness/dsh-plugin-arcgis-pro` directory through Harness's Add plugin dialog. The generated patch contains absolute local paths and is intentionally not committed. Regenerate and reinstall after moving the directory.

使用已安装 PyYAML 的 Windows Python 3.9+ 运行准备脚本，再在 Harness 添加插件对话框安装该目录。生成的配置含本机绝对路径，不提交到 Git；移动目录后重新生成并安装。
