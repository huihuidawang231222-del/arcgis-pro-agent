# Security / 安全

Do not post keys, tokens, credential files, account screenshots or private map data in issues. If a key has been disclosed, revoke it at the provider and create a replacement. Removing a file from a later commit does not remove it from Git history.

请勿在 issue 中提交密钥、令牌、凭据文件、账户截图或私有地图数据。若密钥泄露，请在厂商平台撤销并更换；后续提交删除文件不会抹除 Git 历史。

The optional API UI stores credentials using Windows current-user DPAPI under `%LOCALAPPDATA%\ArcGISProAgent`. Never copy this directory into a repository, even though values are encrypted. Agent logs may contain user prompts, model reasoning, local paths and map metadata. `outputs/` is ignored for that reason. Review what is sent to external models before using sensitive data.

可选 API 界面在 `%LOCALAPPDATA%\ArcGISProAgent` 使用当前用户 DPAPI 保存密钥；即便加密也不要提交。Agent 日志可能包含提示、推理、本机路径及地图元信息，因此忽略 `outputs/`。处理敏感数据前请核对模型数据传输内容。

The bridge accepts allowlisted operations over a same-user named pipe; it is not an authentication boundary against malicious software running as that Windows user. It does not expose a public HTTP endpoint. Do not bypass host approvals or TLS validation to troubleshoot connectivity.

连接桥通过同用户命名管道接收受控操作，不能抵御已取得同一 Windows 账户权限的恶意程序。默认不开放公网 HTTP 接口。排查连接时不要绕过宿主授权或 TLS 证书校验。
