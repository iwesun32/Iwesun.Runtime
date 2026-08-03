# Iwesun Runtime 1.0.37-beta.1 发布清单

> 候选类型：普通β测试版  
> Runtime：`1.0.37-beta.1` / 文件版本`1.0.37.0`  
> Networks：`3.0.0-beta.4` / 文件版本`3.0.0.0`  
> 状态：`BETA_CANDIDATE_PREPARATION_FORMAL_BLOCKED`

> **已撤回**：首次候选误含尚未发布的`Iwesun.Runtime.Web.dll`传递依赖，不得分发或安装；
> 修正载荷使用后续新Runtime版本。

## 交付物

| 文件 | 用途 |
| --- | --- |
| `Iwesun.Runtime.1.0.37-beta.1.msi` | Windows全量安装/升级 |
| `Iwesun.Runtime.1.0.37-beta.1.zip` | 完整便携app/data载荷 |
| `Iwesun.Runtime.Networks.3.0.0-beta.4.nupkg` | 本地Networks离线包 |
| `Iwesun.Runtime.Networks.3.0.0-beta.4.snupkg` | Networks符号包 |
| `IWESUN_RUNTIME_1.0.37_BETA_RELEASE_GUIDE.md` | 消费、迁移与测试说明 |
| `RELEASE_MANIFEST.md` | 本清单 |
| `SHA256SUMS.txt` | 全部交付物SHA-256 |

## 公共库

| 程序集 | Debug | Release | 兼容入口 |
| --- | --- | --- | --- |
| `Iwesun.Runtime.Diagnostics.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.Data.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.Networks.dll` | 有 | 有 | Release |
| `Iwesun.Runtime.WebView2.dll` | 有 | 有 | Release |

Debug/Release文件名、程序集名、命名空间和公共类型一致，二者不可混用。

## Networks验证摘要

- Debug：202/202。
- Release：202/202。
- 旧V2/V3、可空Route、`BinaryIpAddress`和模糊地址族入口反射/源码守卫通过。
- 17/7字节值布局、ICMPv6 36字节布局和UTF-8泛型往返通过。
- UDP长期池和HTTP/DoH租约池生命周期合同通过。

Runtime根构建、功能场景、staging自检、MSI Rebuild和最终文件哈希由唯一发布入口执行后登记。

## 必备资料

- Runtime用户指南、快速开始、Service、CLI、RemoteConsole和诊断文档。
- Data设计、公共API、生命周期、发布状态和源码映射。
- Networks最终设计、beta.4升级迁移报告、地址值迁移、UDP/Ping β指南和完整端点文档。
- WebView2控制、证据、DOM、CLI和SampleHost文档。
- Runtime集成技能、双配置示例和发布/自检脚本。

## 禁止项

- 不上传nuget.org。
- 不包含`Iwesun.Data.dll`、DList、RecordStore V1、`BinaryIpAddress`或V2/V3网络兼容类型。
- 不从相邻仓库复制DLL，不使用跨仓库`ProjectReference`。
- 不把本普通β描述为正式版或正式候选。
