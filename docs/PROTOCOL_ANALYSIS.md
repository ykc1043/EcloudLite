# 移动云电脑协议分析记录

本文件是 Ecloud Lite 的兼容性研究记录，不是官方协议规范。内容只描述当前版本、当前后端和当前测试账号观察到的行为；服务端字段、错误码和连接链路可能随版本变化。

## 范围与证据等级

- **已确认**：代码实现、自测或现场日志已经重复验证。
- **实现观察**：根据客户端行为、响应字段或 runtime 日志推断，并已形成代码。
- **实验性**：能完成握手或结构解析，但尚未证明等价于官方完整功能。

当前基线：客户端 `V3.8.4.v22607211406`，移动云电脑 `V3.8.4.v2`，桌面协议 `V11.250625/V1.2.251014/V2.3.2.0.0`，账户类型为移动云电脑政企版，主要后端为 `CMSSZTE`。

## API 网关层

### 请求封装

Lite 当前使用移动云电脑 API 网关：

- Base URL：`https://cloudpc.ecloud.10086.cn`
- Gateway path：`/api/cem/gateway/outer/cem-webapi`
- 方法：`POST`
- 查询签名：`AccessKey`、`SignatureMethod`、`SignatureNonce`、`SignatureVersion`、`Timestamp` 和 `Signature`
- 签名方法：代码中实现为 `HmacSHA1`，签名串先对规范化参数做 SHA-256，再使用 HMAC-SHA1 生成十六进制摘要。

业务 JSON 会合并客户端公共设备字段和当前 `accessToken`，再经过 RSA-1024 分块加密：

1. UTF-8 编码业务 JSON。
2. 使用 RSA PKCS#1 v1.5 加密，每个明文块最多 117 字节。
3. 将密文整体 Base64 编码，包装为 `{"params":"..."}`。
4. 服务端响应使用同样的 128 字节 RSA 块解密，再解析 `state`、`errorCode`、`errorMessage` 和 `body`。

代码和日志只记录字段名、长度、短哈希和响应结构，不记录密码、Token、验证码或 connectStr。源码中的固定协议常量属于客户端兼容材料，不应当作用户凭据；公开范围与 MIT 参考项目 `1936-zero/ecloud-cmsszte-alive` 保持一致。

### 登录链路

密码登录的主链路为：

1. `POST /login/verify`，提交账号、密码、时间戳和 `clientNeedTwoFactor=true`。
2. 服务器直接返回 `accessToken`，或返回 `accessTicket`。
3. `POST /login/verifyAccessTicket` 交换 `accessTicket` 得到 `accessToken`。

需要额外验证时，当前代码识别以下分支：

| 错误码 | 分支 | 主要接口 |
| --- | --- | --- |
| `30002009` | 设备未信任 | `/login/sendVerifySms`、`/login/trustDevice`、`/login/trustOrTemporaryDevice` |
| `30002060` | 二次验证 | `/login/special/getSecondauthSms`、`/login/verifyTwoFactorAuthSms` |
| `30002063` | 增强短信策略 | `/login/sendVerifySms`、`/login/verifyLoginEnhanceSms` |

独立短信登录使用：

- 发送：`POST /login/sendVerifySms`，`mobile` + `codeType=login`。
- 登录：`POST /login/verifySms`，`mobile` + `verificationCode` + `isNeedTemporaryDeviceSelection=true`。
- 成功后同样通过 `accessTicket` 或直接返回的 `accessToken` 建立会话。

### 资源管理接口

当前已接入的资源接口：

| 接口 | 用途 |
| --- | --- |
| `/user/getDeviceInfo` | 获取云电脑列表和 `customLoginParams` |
| `/user/getDesktopStatus` | 按 `instanceIdList` 获取状态 |
| `/resource/operate` | 开机、关机、重启 |
| `/resource/desktopUptime` | 查询在线时长 |
| `/login/logout` | 服务端退出登录 |

`originCompanyCode` 用于选择后端适配器。当前现场重点是 `CMSSZTE`，其他值只做识别或候选标记。

## CAG 连接参数

CMSSZTE 桌面的 `customLoginParams` 包含 CAG 地址列表和 `csapip`/`csapipv6` 等字段。Lite 选择优先端口 `8899` 的 CAG 地址，向：

`http://<cag-host>:<port>/cs/cs_suOperDesktop.action`

发送 JSON 请求。请求体包含 `encrypt=7`、语言、毫秒时间戳和加密的 VM 操作参数。响应中的 `connectStr` 是十六进制编码的连接参数，解码后解析出 VMID、密钥、IPv6、服务端口和代理端口。

连接参数只保存在当前进程内存；日志只记录长度、字段名和短哈希。刷新桌面、切换账号或关闭程序时清除。

## Path B 握手探测

当前 `PathBProtocol` 能从连接参数生成 ZTEC、主认证和 REDQ 模板，并解析供应商包裹的 SPICE 帧；实现了心跳帧和 ACK 形状检查。`PathBHandshakeService` 用它执行连接、TLS/认证阶段和心跳探测。

一次性“建立测试会话”保持原有行为：最多监听约 26 秒，收到并回复 2 个服务端 `0x74` 心跳后结束。单设备保活使用相同握手链路，但每轮监听 60 秒，不在第 2 个心跳处提前退出；每个 `0x74` 帧使用带相同 serial 的 `0x79` ACK 回复。轮次结束后查询一次 `/resource/desktopUptime`，等待 300 秒，重新获取短期 `connectStr` 后开始下一轮。停止信号在心跳读取循环内约 1.5 秒粒度检查。

保活实现不检查、加载或调用 `cmss-runtime`，因此可以独立于官方 renderer 运行。当前只允许绑定一台 `CMSSZTE` 云电脑，并与一次性测试会话和官方 renderer 互斥；退出登录、切换本地账号和关闭 Lite 会请求停止。日志使用独立 `PATHB_KEEPALIVE` 分类，记录阶段、轮次、TLS、REDQ 长度、心跳计数、耗时、在线时长查询结果和最终摘要，不记录完整 CAG 地址、连接参数、密钥或 Token。

这部分是实验性协议探测，不等同于官方完整 renderer。尤其是显示通道的 surface 初始化、输入通道、音频通道、流控、重连和多后端差异仍未完整实现，因此不能将握手成功表述为完整桌面兼容。

在 Step 1 的官方 runtime 模式下，现场已经验证键盘、鼠标、组合键、中文输入法、文本及文件剪贴板、扬声器、麦克风和网络中断后的重连可用。这些能力由官方 renderer/runtime 提供，不代表 Lite 已经独立实现对应 SPICE 通道。现场日志表明 renderer 在第三方客户端控制模式下，会使用外层 type `1010`、内层 `msg_type=10` 的 JSON 帧发送 toolbar action。Lite 将 `minimize` 转发为 renderer 主窗口最小化，并将 `quit`、`exit`、`disconnect` 统一映射到断开确认、精确 PID 清理和状态恢复流程。断开确认框使用 renderer 的原生窗口句柄作为 owner，以便在全屏或前台云电脑窗口之上显示。

## 官方 CMSS runtime 编排

Step 1 通过用户本地取得的官方 `uSmartView_VDI_Client.exe` 负责实际渲染。Lite 的工作边界是：

1. 生成本地 `127.0.0.1:15900+` 控制服务器。
2. 从桌面字段生成官方启动 JSON。
3. 使用 runtime 内公钥进行 RSA-1024 分块加密。
4. 以 `uSmartView_VDI_Client.exe --json <cipher>` 启动 renderer，并记录精确 PID。
5. 监听原生控制帧和心跳，关闭时先尝试优雅退出，再只清理本次启动的 renderer/service agent PID。

官方组件可能使用 Windows Known Folder 写配置，因此 Lite 将 profile 和镜像配置放在 runtime 目录下，同时把 Lite 日志和设置放在程序目录 `data/`。官方 runtime 的二进制、资源和许可证不属于本仓库 MIT 授权范围。

## 会话、错误和安全边界

- 本地会话档案按账号和登录模式保存，Token/密码使用当前 Windows 用户 DPAPI。
- 切换本地会话不会调用服务端 Logout，避免误注销另一个已保存账号。
- 明确的 `401` 或 Token/登录失效/授权过期提示才会清除本地失效 Token。
- 网关错误、桌面关机、实例参数错误和普通网络异常不会自动删除会话。
- 服务器可能限制同一账号的并发设备；连接或登录可能挤掉另一台设备。
- 任何协议重放、保活和连接测试都必须在用户有权控制的账号和云电脑上进行。

## 未解决问题

- 非 `CMSSZTE` 后端的实际连接。
- 官方 renderer 的重启/关机/锁屏 toolbar action、显示模式、多显示器和异常生命周期行为。
- 不依赖官方 runtime 的开源显示和输入实现。
- 单设备 Path B 保活在不同网络、账号策略和数小时/数天周期下的现场有效性；当前实现仍保持 `production_claim=false`。
- 不同服务端版本、区域和政企账号策略的兼容性矩阵。

路线与测试勾选见 [ROADMAP.md](ROADMAP.md)。
