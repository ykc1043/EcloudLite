# GitHub 发布前检查清单

当前协议材料的公开范围以 MIT 项目 `1936-zero/ecloud-cmsszte-alive` 为参考。固定客户端协议常量可以保留，但用户数据和官方 runtime 必须排除。发布前至少完成以下项目：

当前公开仓库：<https://github.com/ykc1043/EcloudLite>。仓库已经公开不代表以下持续审计项目可以跳过。

- [x] 对照参考项目确认 `src/Protocol/ProtocolConstants.cs` 中的 RSA 私钥、AccessKey、SecretKey 属于其已公开的固定客户端兼容材料。
- [x] `assets/cmsszte-public.pem` 作为启动官方 runtime 的固定公钥兼容数据保留，并在第三方声明中标明边界。
- [ ] 确认 `analysis/`、官方安装包、`dist/cmss-runtime/`、日志、`data/settings.json` 和所有 zip 包均未进入 Git 历史。
- [ ] 检查历史提交，而不只是当前工作树；一旦密钥进入 Git 历史，删除当前文件并不足够。
- [ ] 用不包含官方 runtime 的干净目录执行 `./build.ps1 -SkipOfficialRuntime` 和 `./dist/EcloudLite.SelfTest.exe`。
- [ ] 在没有测试账号、密码、Token、验证码和业务日志的干净环境中审阅 README、协议分析和截图。
- [ ] 由项目维护者确认 MIT License、第三方声明、官方服务条款和所在地区法律要求。
- [ ] 发布源码 release 时只发布源码和文档；官方 runtime 仅由用户通过官方下载页面自行取得。
