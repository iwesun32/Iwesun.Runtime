# Iwesun Runtime 1.0.36-beta.1 发布清单

> 候选类型：普通β测试版  
> Runtime：`1.0.36-beta.1` / 文件版本`1.0.36.0`  
> Networks：`3.0.0-beta.3` / 文件版本`3.0.0.0`  
> 状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`

## 交付物

| 文件 | 用途 |
| --- | --- |
| `Iwesun.Runtime.1.0.36-beta.1.msi` | Windows全量安装/升级 |
| `Iwesun.Runtime.1.0.36-beta.1.zip` | 完整便携app/data载荷 |
| `Iwesun.Runtime.Networks.3.0.0-beta.3.nupkg` | 本地Networks离线包 |
| `Iwesun.Runtime.Networks.3.0.0-beta.3.snupkg` | Networks符号包 |
| `IWESUN_RUNTIME_1.0.36_BETA_RELEASE_GUIDE.md` | 消费、迁移与测试说明 |
| `RELEASE_MANIFEST.md` | 本清单 |
| `SHA256SUMS.txt` | 全部交付物SHA-256 |

## 公共库

| 程序集 | Debug | Release | 兼容入口 |
| --- | --- | --- | --- |
| `Iwesun.Runtime.Diagnostics.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.Data.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.Networks.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.WebView2.dll` | 有 | 有 | Release |

Debug/Release的文件名、程序集名、命名空间和公共类型一致，二者不可混用。

## 必备资料

- Runtime用户指南、快速开始、Windows Service、CLI、RemoteConsole和诊断文档。
- Data设计、公共API、生命周期、发布状态、活动需求和源码映射。
- Networks最终精确访问设计、迁移矩阵、跟踪、IP/MAC值、UDP β测试与本地表文档。
- WebView2控制、能力、证据、DOM、数据记录器、CLI和SampleHost文档。
- Runtime集成技能、安装版SampleHost、Networks示例和发布/自检脚本。

## 验证摘要

- Data Debug/Release：101/101。
- Networks Debug/Release：188/188。
- Runtime Debug/Release根解决方案：零警告、零错误。
- Runtime双配置功能场景、staging自检、安装版SampleHost双配置和MSI Rebuild：必须全部通过。
- `SHA256SUMS.txt`由唯一全量发布入口在候选生成完成后产生，不手工填写。

## 禁止项

- 不上传nuget.org。
- 不包含`Iwesun.Data.dll`、DList、RecordStore V1、`BinaryIpAddress`或V2/V3网络兼容类型。
- 不从相邻仓库复制DLL，不使用跨仓库`ProjectReference`。
- 不把本普通β描述为正式版或正式候选。
