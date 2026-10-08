# Iwesun Runtime 1.0.46-beta.1 发布清单

> 候选类型：普通 β 测试版  
> Runtime：`1.0.46-beta.1` / 文件版本 `1.0.46.0`  
> Networks：`3.0.0-beta.5` / 文件版本 `3.0.0.0`  
> 状态：`GENERAL_BETA_PACKAGED_VALIDATED`

## 交付物

| 文件 | 用途 |
| --- | --- |
| `Iwesun.Runtime.1.0.46-beta.1.msi` | Windows 全量安装/升级 |
| `Iwesun.Runtime.1.0.46-beta.1.zip` | 完整便携 app/data 载荷 |
| `Iwesun.Runtime.Networks.3.0.0-beta.5.nupkg` | 本地 Networks 离线包 |
| `Iwesun.Runtime.Networks.3.0.0-beta.5.snupkg` | Networks 符号包 |
| `IWESUN_RUNTIME_1.0.46_BETA_RELEASE_GUIDE.md` | 消费、迁移与测试说明 |
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

- Runtime 根解决方案 Debug、Release 和 Publish 全部构建通过，0警告、0错误。
- Data Debug/Release各101项、Networks各208项、WebView2各53项测试通过。
- Diagnostics Debug/Release完整功能场景、以1.0.43编译的旧二进制宿主兼容和协调退出场景通过。
- staging 自检、NuGet、ZIP、MSI Rebuild 和 SHA-256 清单全部通过。
- 安装载荷递归拒绝 Web、Iwesun.Data、DList、RecordStore V1 和旧网络合同。
- 未完成WFP真实网络、长期压力或干净环境安装验收前，候选不得描述为正式版本，也不得上传 nuget.org。

本候选已完成打包和staging验证；Program Files安装、提交、推送及公共NuGet上传不属于本次交付。
