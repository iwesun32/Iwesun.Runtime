# Iwesun Runtime 1.0.41-beta.1 发布清单

> 候选类型：普通 β 测试版  
> Runtime：`1.0.41-beta.1` / 文件版本 `1.0.41.0`  
> Networks：`3.0.0-beta.4` / 文件版本 `3.0.0.0`  
> 状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`

## 交付物

| 文件 | 用途 |
| --- | --- |
| `Iwesun.Runtime.1.0.41-beta.1.msi` | Windows 全量安装/升级 |
| `Iwesun.Runtime.1.0.41-beta.1.zip` | 完整便携 app/data 载荷 |
| `Iwesun.Runtime.Networks.3.0.0-beta.4.nupkg` | 本地 Networks 离线包 |
| `Iwesun.Runtime.Networks.3.0.0-beta.4.snupkg` | Networks 符号包 |
| `IWESUN_RUNTIME_1.0.41_BETA_RELEASE_GUIDE.md` | 消费、迁移与测试说明 |
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

## 验证要求

- WebView2 Debug/Release 各 25/25。
- Runtime 根 Debug/Release 零警告、零错误，完整功能场景通过。
- Data、Networks 双配置测试通过。
- staging、自检、NuGet、ZIP、MSI Rebuild 和 SHA-256 全部通过。
- 安装载荷递归拒绝 Web、Iwesun.Data、DList、RecordStore V1 和旧网络合同。

## 禁止项

- 未经实机复验不得描述为正式版本。
- 不上传 nuget.org。
- 不提交、不推送。
