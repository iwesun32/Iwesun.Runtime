# Iwesun Runtime 1.0.43-beta.1 发布清单

> 候选类型：普通 β 测试版  
> Runtime：`1.0.43-beta.1` / 文件版本 `1.0.43.0`  
> Networks：`3.0.0-beta.4` / 文件版本 `3.0.0.0`  
> 状态：`GENERAL_BETA_INSTALLED_VALIDATED`

## 交付物

| 文件 | 用途 |
| --- | --- |
| `Iwesun.Runtime.1.0.43-beta.1.msi` | Windows 全量安装/升级 |
| `Iwesun.Runtime.1.0.43-beta.1.zip` | 完整便携 app/data 载荷 |
| `Iwesun.Runtime.Networks.3.0.0-beta.4.nupkg` | 本地 Networks 离线包 |
| `Iwesun.Runtime.Networks.3.0.0-beta.4.snupkg` | Networks 符号包 |
| `IWESUN_RUNTIME_1.0.43_BETA_RELEASE_GUIDE.md` | 消费、迁移与测试说明 |
| `RELEASE_MANIFEST.md` | 本清单 |
| `SHA256SUMS.txt` | 全部交付物 SHA-256 |

## 发布组件

| 程序集 | Debug | Release | 顶层兼容入口 |
| --- | --- | --- | --- |
| `Iwesun.Runtime.Diagnostics.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.Data.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.Networks.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.WebView2.dll` | 有 | 有 | Release |

`Iwesun.Runtime.Web.dll` 及其源码、符号和目录明确不包含。

## 发布门禁

- Runtime 根与 Diagnostics Debug/Release 构建零警告、零错误。
- Debug/Release 完整功能场景和 `shutdown-contract` 通过。
- 兼容宿主的 Diagnostics 引用版本必须精确为 `1.0.42.0`，并由 1.0.43 当前程序集完成旧构造函数、DI、Activate、shutdown 和退出码 0 验证。
- Data、Networks、WebView2 双配置测试通过。
- staging、自检、NuGet、ZIP、MSI Rebuild 和 SHA-256 全部通过。
- 安装载荷递归拒绝 Web、Iwesun.Data、DList、RecordStore V1 和旧网络合同。

当前源码预检已通过根解决方案双配置构建、Diagnostics Debug 29/29 与 Release 24/24 功能回归、Data 双配置各 101/101、Networks 各 202/202、WebView2 各 34/34，以及旧 `1.0.42.0` 宿主二进制兼容门禁。唯一候选目录的 7 项交付物已生成；staging 与 Program Files 官方自检通过。Aether 从默认安装根完成双配置编译、Release 测试 242/242 和 DNS/DoH 精确匹配 36/36。

## 禁止项

- 未经实机复验不得描述为正式版本。
- 不上传 nuget.org。
- 未经用户明确指令不安装、不提交、不推送。
