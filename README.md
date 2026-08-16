# Ecloud Lite

移动云电脑 Lite 是一个面向 Windows 的轻量客户端与兼容性研究项目。它使用原生 .NET Framework WinForms 实现登录、云电脑管理、本地会话保存和 CMSSZTE 连接链路；Step 1 可以调用用户自行取得的官方 runtime，Step 2 计划逐步替换为完全开源的传输和渲染组件。

本项目不是中国移动官方客户端，也不代表中国移动或移动云电脑服务方。请先阅读 [DISCLAIMER.md](DISCLAIMER.md) 和 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 当前状态

- 源码许可证：MIT，详见 [LICENSE](LICENSE)。
- 官方 CMSS runtime：不随源码仓库提供，不由 MIT 许可证覆盖。
- 主要测试后端：`CMSSZTE`。
- Step 1 详细进度：[docs/ROADMAP.md](docs/ROADMAP.md)。
- API、登录和连接链路记录：[docs/PROTOCOL_ANALYSIS.md](docs/PROTOCOL_ANALYSIS.md)。

## 兼容基线与测试环境

| 项目 | 当前记录 |
| --- | --- |
| 测试平台 | Windows x64，当前以 Windows 10/11 桌面环境为目标；尚未完成完整系统版本矩阵 |
| 测试账户 | 移动云电脑政企版 |
| 客户端基线 | `V3.8.4.v22607211406` |
| 移动云电脑版本 | `V3.8.4.v2` |
| 桌面协议版本 | `V11.250625/V1.2.251014/V2.3.2.0.0` |
| 官方版本下载 | <https://ecloud.10086.cn/api/query/clouddesktop/ccaorder/#/downloadAppPage> |

## 已实现能力

- 账号密码登录、独立短信验证码登录。
- 设备信任、二次验证、增强短信验证。
- DPAPI 加密的记住密码、Token 自动登录和密码回退。
- 多账号本地会话、快速切换、删除、过期提示和重新登录更新。
- 云电脑列表、状态、后端识别、开机、关机、重启和在线时长接口。
- CMSSZTE CAG 连接参数获取、Path B 握手探测和心跳 ACK 探测。
- 调用用户本地官方 CMSS renderer，包含启动、Lite 侧断开和精确 PID 清理。
- 程序目录优先的配置、日志、CMSS profile 镜像和脱敏详细日志。
- “关于”窗口显示兼容基线、移动云电脑版本和桌面协议版本。

原生 renderer 已在另一台 Windows 电脑上验证可以显示官方云电脑画面，但键鼠、音频、退出提示、重连和不同后端兼容性仍未完成完整回归。具体勾选状态以 [docs/ROADMAP.md](docs/ROADMAP.md) 为准。

## 快速构建

### 只构建开源部分

要求 Windows、PowerShell 和 .NET Framework 4.8 编译器：

```powershell
.\build.ps1 -SkipOfficialRuntime
.\dist\EcloudLite.SelfTest.exe
```

这会生成 `dist\EcloudLite.exe` 和离线自测程序，但不会生成官方 CMSS runtime；登录和云电脑管理代码仍可编译、测试和审阅。

### 构建本地兼容性测试包

只有在用户已经依法取得官方安装包并完成本地研究目录准备后，才运行：

```powershell
.\build.ps1
```

当前 runtime 构建脚本读取工作区外的 `analysis\cmss-full` 和依赖扫描工具。相关官方二进制、配置、Qt 插件和数据库不会被提交到 GitHub。构建出的 `dist\cmss-runtime` 仅适合本地授权测试，不应作为本项目的第三方再分发物。

## 使用说明

1. 启动 `EcloudLite.exe`，选择密码登录或验证码登录。
2. 密码登录可分别选择“记住密码”“自动登录”“保存会话”；保存会话会自动启用记住密码。
3. 验证码登录不保存验证码，不提供记住密码和自动登录，但可以保存 Token 会话。
4. 登录后刷新并选择云电脑，再执行列表、状态或连接操作。
5. 使用官方 runtime 时，选择 `CMSSZTE` 云电脑并点击“启动云电脑”；Lite 的“断开云电脑”只结束本次渲染会话，不执行云电脑关机。

会话与密码均按当前 Windows 用户使用 DPAPI 加密。配置默认位于程序目录 `data\settings.json`，日志默认位于 `data\logs\`。日志会尽量脱敏，但分享日志前仍应人工检查账号、网络地址和业务信息。

## 创建 Release 包

在项目根目录执行以下命令，会重新构建开源部分、运行离线自测，并在 `dist\` 生成 zip 和 SHA-256 文件：

```powershell
.\release.ps1 -Version 0.1.0
```

安装并登录 GitHub CLI 后，可以通过 Xray HTTP 代理推送标签并创建 GitHub Release：

```powershell
.\release.ps1 -Version 0.1.0 -Publish -Proxy http://127.0.0.1:10809
```

发布脚本只打包 `EcloudLite.exe`、SelfTest、固定兼容公钥和仓库声明文档，不包含官方 runtime、用户设置、日志或分析目录。

## 项目路线

- [Step 1：接入官方 runtime](docs/ROADMAP.md#step-1接入官方-runtime)：当前阶段，优先实现和验证官方功能，同时保留 Lite 自有的会话、日志和诊断能力。
- [Step 2：完全开源组件](docs/ROADMAP.md#step-2抛弃官方-runtime)：后续阶段，按大类替换官方 runtime，不在当前阶段承诺具体完成时间。

## 公开仓库发布前检查

- 完整清单见 [docs/PUBLISHING_CHECKLIST.md](docs/PUBLISHING_CHECKLIST.md)。
- 确认 `analysis/`、`dist/`、runtime、日志、`settings.json` 和 zip 包没有被提交。
- 协议固定常量、AccessKey/SecretKey 和 RSA 材料按参考项目 `ecloud-cmsszte-alive` 的公开范围保留；它们是官方客户端内置的兼容材料，不是用户账号凭据。
- 严禁提交真实账号、密码、Token、验证码、连接参数、业务日志和官方 runtime。
- 保留 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) 和 [DISCLAIMER.md](DISCLAIMER.md)，不要把官方 runtime 打包进源码 release。
- 对不同 Windows 版本、账号策略和后端重新进行现场回归。

## 参考资料

- [1936-zero/ecloud-cmsszte-alive](https://github.com/1936-zero/ecloud-cmsszte-alive)：MIT 开源的移动云电脑保活项目。本项目参考其 CEM API、登录分支、桌面接口、CAG/Path B、Token 失效识别和保活研究结果，并以 C#/WinForms 独立实现桌面客户端。
- [中国移动云电脑远程连接协议和保活机制分析](https://codming.com/posts/cmcc-cloud-computer-keepalive/)：Codming 对 SCG、穿云传输、SPICE 握手、Display Surface 和保活条件的分析。
- [SPICE Protocol Specification](https://www.spice-space.org/spice-protocol.html)：标准 SPICE 消息、通道、认证和流控参考。
- [移动云电脑官方下载页面](https://ecloud.10086.cn/api/query/clouddesktop/ccaorder/#/downloadAppPage)：用于取得合法的官方客户端和确认版本基线。

本地研究目录 `参考资料/` 保存了上述项目和文章的离线副本，仅用于开发核对，不应把第三方仓库副本或网页归档直接并入 EcloudLite 的 Git 历史。

## 文档

- [协议分析](docs/PROTOCOL_ANALYSIS.md)
- [Step 1 / Step 2 路线图](docs/ROADMAP.md)
- [GitHub 发布前检查](docs/PUBLISHING_CHECKLIST.md)
- [第三方声明](THIRD_PARTY_NOTICES.md)
- [免责声明](DISCLAIMER.md)
- [MIT License](LICENSE)
