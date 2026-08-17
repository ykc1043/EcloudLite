# Ecloud Lite

移动云电脑 Lite 是一个面向 Windows 的轻量客户端与兼容性研究项目。它使用原生 .NET Framework WinForms 实现登录、云电脑管理、本地会话保存和 CMSSZTE 连接链路；Step 1 可以调用用户自行取得的官方 runtime，Step 2 计划逐步替换为完全开源的传输和渲染组件。

本项目不是中国移动官方客户端，也不代表中国移动或移动云电脑服务方。请先阅读 [DISCLAIMER.md](DISCLAIMER.md) 和 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 当前状态

- 源码许可证：MIT，详见 [LICENSE](LICENSE)。
- 官方 CMSS runtime：不随源码仓库提供，不由 MIT 许可证覆盖。
- 主要测试后端：`CMSSZTE`。
- 公开仓库：<https://github.com/ykc1043/EcloudLite>。
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
- 不依赖官方 runtime 的单设备 Path B 保活：60 秒心跳监听、300 秒轮询、在线时长佐证、实时统计和最终连接摘要。
- 调用用户本地官方 CMSS renderer，包含启动、Lite 侧断开和精确 PID 清理。
- 缺少 runtime 时自动提示，并可从用户自行下载的官方安装包本地提取约 146 MiB 的最小运行组件。
- 程序目录优先的配置、日志、CMSS profile 镜像和脱敏详细日志。
- “关于”窗口显示兼容基线、移动云电脑版本和桌面协议版本。

原生 renderer 和 runtime 配置向导已在其他 Windows 电脑完成现场测试。键盘、鼠标、组合键、中文输入法、文本/文件剪贴板、扬声器、麦克风和网络中断后的重连均已验证可用。官方窗口内部的退出菜单和关闭窗口提示当前不可用，可使用 Lite 主程序的“断开云电脑”退出；电源操作、生命周期、显示模式和更广泛兼容矩阵仍需回归。具体勾选状态以 [docs/ROADMAP.md](docs/ROADMAP.md) 为准。

## 快速构建

### 只构建开源部分

要求 Windows、PowerShell 和 .NET Framework 4.8 编译器：

```powershell
.\build.ps1 -SkipOfficialRuntime
.\dist\EcloudLite.SelfTest.exe
```

这会生成 `dist\EcloudLite.exe` 和离线自测程序，但不会生成官方 CMSS runtime；登录和云电脑管理代码仍可编译、测试和审阅。首次启动时，Lite 会在检测到 runtime 缺失后提供本地配置向导。

### 构建本地兼容性测试包

旧的开发构建流程仍可在已经准备本地研究目录时运行：

```powershell
.\build.ps1
```

当前开发构建脚本读取工作区外的 `analysis\cmss-full` 和依赖扫描工具。普通用户不需要准备这些目录，可以直接使用程序内的运行组件配置向导。相关官方二进制、配置、Qt 插件和数据库不会被提交到 GitHub。构建出的 `dist\cmss-runtime` 仅适合本地授权测试，不应作为本项目的第三方再分发物。

### 首次配置运行组件

1. 启动 Lite；检测不到 `cmss-runtime` 时选择“现在配置”，也可以稍后点击主界面的“运行组件”。
2. 点击“打开官方下载页”，自行下载移动云电脑 Windows 安装包。
3. 点击“选择...”指定官方安装包。
4. 如果未检测到 7-Zip，可点击“下载 7-Zip”。安装包只从 7-Zip 官方网站下载到 `data\tools\downloads\`，是否启动安装程序仍由用户确认。
5. 安装 7-Zip 后点击“重新检测”，再点击“开始提取并配置”。

提取时临时需要约 2 GiB 可用空间。Lite 会先提取安装包中的 `app.7z`，再只处理 `drivers\CMSS`，扫描 PE 依赖并组装最小 runtime。成功后临时目录会自动清理；失败时现场保留在 `data\runtime-setup\`，便于结合 `data\logs\` 排查。已有不完整 runtime 会备份到程序目录的 `cmss-runtime.backup-*`。整个过程不会静默安装官方客户端，也不会上传安装包或提取结果。

## 使用说明

1. 启动 `EcloudLite.exe`，选择密码登录或验证码登录。
2. 密码登录可分别选择“记住密码”“自动登录”“保存会话”；保存会话会自动启用记住密码。
3. 验证码登录不保存验证码，不提供记住密码和自动登录，但可以保存 Token 会话。
4. 登录后刷新并选择云电脑，再执行列表、状态或连接操作。
5. 使用官方 runtime 时，选择 `CMSSZTE` 云电脑并点击“启动云电脑”；若 runtime 尚未配置，Lite 会先打开配置向导。Lite 的“断开云电脑”只结束本次渲染会话，不执行云电脑关机。
6. 只需保活时，选择一台 `CMSSZTE` 云电脑并点击“开始保活”。保活与“建立测试会话”、官方 renderer 互斥；退出登录、切换账号或关闭 Lite 时会自动停止。界面显示当前轮次、心跳、成功/失败、最近成功、下次执行和最后连接结果，详细记录位于日志的 `PATHB_KEEPALIVE` 分类。

“建立测试会话”仍保留原有的一次性 26 秒/最多 2 个心跳探测行为。保活则每轮持续监听 60 秒，回复服务端 `0x74` 心跳并在轮次结束后等待 300 秒，再重新获取短期连接参数。在线时长查询只作为 HTTP 侧佐证；在完成真实环境长时间回归前，日志继续标记 `production_claim=false`。

当前已知限制：官方 renderer 窗口内部的“退出”和关闭窗口提示不可用。请先保存云电脑中的工作，再回到 Lite 主程序点击“断开云电脑”。

会话与密码均按当前 Windows 用户使用 DPAPI 加密。配置默认位于程序目录 `data\settings.json`，日志默认位于 `data\logs\`。日志会尽量脱敏，但分享日志前仍应人工检查账号、网络地址和业务信息。

## 创建 Release 包

在项目根目录执行以下命令，会重新构建开源部分、运行离线自测，并在 `dist\` 生成 zip 和 SHA-256 文件：

```powershell
.\release.ps1 -Version 0.1.2
```

安装并登录 GitHub CLI 后，可以通过 Xray HTTP 代理推送标签并创建 GitHub Release：

```powershell
.\release.ps1 -Version 0.1.2 -Publish -Proxy http://127.0.0.1:10809
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
