# 第三方声明

## 中国移动与移动云电脑组件

EcloudLite 是独立的兼容性研究项目，与中国移动或 `ecloud.10086.cn` 的运营方不存在隶属、授权、背书或赞助关系。“中国移动”“移动云电脑”以及相关产品名称、商标和标识均归其各自权利人所有。

本仓库中的 MIT License 只适用于 EcloudLite 贡献者原创的源码和文档，不授予以下材料的任何权利：

- 移动云电脑官方应用程序。
- CMSS/ZTE renderer、DLL、服务程序和动态库。
- 官方配置、图标、图片、音频、数据库、Qt 插件和其他资源。
- 其他由厂商或第三方拥有版权、商标权或专利权的材料。

仓库中的 `assets/cmsszte-public.pem` 以及源码中为实现互操作而记录的 AccessKey、SecretKey、RSA 材料和协议常量属于固定客户端兼容数据，不是用户账号凭据。EcloudLite 按 MIT 开源参考项目 `1936-zero/ecloud-cmsszte-alive` 的公开范围保留这些材料，但不因此对第三方材料主张所有权或授予额外权利。

官方 runtime 文件不会包含在源码仓库中。用户必须自行从官方渠道取得客户端、接受适用条款，并自行判断在所在地区进行本地提取、调试或互操作研究是否合法。不要在 EcloudLite 的源码 release 中重新分发厂商二进制。

EcloudLite 的运行组件配置向导只在用户明确操作后打开官方下载页、下载 7-Zip 或启动 7-Zip 安装程序。官方客户端安装包由用户自行选择，提取和依赖裁剪完全在本机执行；生成的 `cmss-runtime` 仍属于不受本项目 MIT License 覆盖的厂商组件集合。

官方下载页面：<https://ecloud.10086.cn/api/query/clouddesktop/ccaorder/#/downloadAppPage>

## 平台库

EcloudLite 以 Microsoft .NET Framework 4.8 为目标，使用 Windows 提供的框架程序集。这些程序集继续受 Microsoft 的适用许可约束。运行时加载的任何厂商组件继续受其各自许可证约束。

运行组件配置向导可选择从 <https://www.7-zip.org/> 下载 7-Zip 官方 MSI，并调用用户安装的 `7z.exe` 解包。7-Zip 及其安装包适用 7-Zip 项目自己的许可证，不受 EcloudLite MIT License 覆盖。
