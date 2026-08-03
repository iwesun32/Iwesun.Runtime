# Iwesun Runtime 1.0.38-beta.1 发布清单

> 候选类型：普通β测试版  
> Runtime：`1.0.38-beta.1` / 文件版本`1.0.38.0`  
> Networks：`3.0.0-beta.4` / 文件版本`3.0.0.0`  
> 状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`

## 交付物

| 文件 | 用途 |
| --- | --- |
| `Iwesun.Runtime.1.0.38-beta.1.msi` | Windows全量安装/升级 |
| `Iwesun.Runtime.1.0.38-beta.1.zip` | 完整便携app/data载荷 |
| `Iwesun.Runtime.Networks.3.0.0-beta.4.nupkg` | 本地Networks离线包 |
| `Iwesun.Runtime.Networks.3.0.0-beta.4.snupkg` | Networks符号包 |
| `IWESUN_RUNTIME_1.0.38_BETA_RELEASE_GUIDE.md` | 消费、迁移与测试说明 |
| `RELEASE_MANIFEST.md` | 本清单 |
| `SHA256SUMS.txt` | 全部交付物SHA-256 |

## 发布组件

| 程序集 | Debug | Release | 顶层兼容入口 |
| --- | --- | --- | --- |
| `Iwesun.Runtime.Diagnostics.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.Data.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.Networks.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.WebView2.dll` | 有 | 有 | Release |

`Iwesun.Runtime.Web.dll`处于架构调试期，本版明确不包含。CLI、WebView2和SampleHost也不得
传递携带该DLL或PDB。

## 验证要求

- Networks Debug/Release：各202/202。
- Runtime根Debug/Release：0警告、0错误。
- Runtime功能场景、Publish staging、自检和MSI Rebuild全部通过。
- 安装载荷递归拒绝Web、Iwesun.Data、DList、RecordStore V1、BinaryIpAddress和V2/V3网络合同。
- SHA-256由唯一发布入口生成并独立复核。

## 禁止项

- 不上传nuget.org。
- 不使用跨仓库`ProjectReference`或相邻仓库手工DLL。
- 不把本普通β描述为正式版。

