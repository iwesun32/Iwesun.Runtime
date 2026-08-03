# Iwesun Runtime 1.0.36-beta.1 普通 β 发布说明

> 状态：`GENERAL_BETA_READY_FORMAL_BLOCKED`  
> Runtime产品版本：`1.0.36`  
> Runtime程序集/文件版本：`1.0.36.0`  
> Runtime产品信息版本：`1.0.36-beta.1`  
> Networks产品信息版本：`3.0.0-beta.3`  
> Networks程序集/文件版本：`3.0.0.0`

本版用于 Runtime、DDNS Snap、Aether 及其他内部消费者的强制升级与普通β测试。它不是正式版，
不上传公共NuGet源，也不能覆盖或冒充`1.0.35-beta.1`。

## 1. 主要升级

### Data

- 唯一数据程序集仍为`Iwesun.Runtime.Data.dll`，唯一活动实现为
  `RecordStore<TValue,TPrimaryKey>`。
- `MergeAdd(B)`由继承表负责候选选择和业务合并；基类只验证成功ID属于当前Source活动记录。
- 增加显式委托/事件、订阅领取和Source `Clear()`合同，并加固null更新及派生表恢复。
- DList、RecordStore V1、版本后缀类型、`Iwesun.Data.dll`和旧命名空间不提供兼容层。

### Networks

- 唯一公共IP类型为17字节`IpAddressValue`，唯一公共MAC类型为7字节`MacAddressValue`。
- 删除`BinaryIpAddress`；ARP和IPv6邻居记录不再暴露字符串IP/MAC。
- IPv6 `ScopeId`不进入IP值；接口索引保存在访问计划、路径证据或邻居记录独立字段。
- 所有低层发送入口在FIFO前拒绝`Guid.Empty`；绝对身份继续使用
  `RequestId → AttemptId → BranchId → ResponseId`。
- UDP长期Socket池增加安全空闲回收，严格保留活动和未到期隔离槽；实际远端必须精确匹配。

### WebView2

- 证据会话启动按事务回滚事件、CDP和缓存策略，失败不遗留半初始化订阅。
- Program停止失败或取消时保留未清理注册，后续调用可继续补偿停止。
- CSS双源、布局状态、HTTP页面资源正文、公共证据命令和SampleHost资料统一进入本次载荷。

### Runtime与发布

- Diagnostics、Data、Networks、WebView2全部从当前源码重建Debug/Release双配置DLL。
- `await RTask`现在等待Managed注销闭环；运行中Dispose保留活动登记，并在实际完成后安全释放。
- Debug与Release保持相同程序集名、命名空间、公共类型和接口；消费者按项目配置选择目录。
- MSI升级清理Program Files旧Runtime树，保留ProgramData用户数据。

## 2. 标准DLL布局

```text
lib\Iwesun.Runtime.Diagnostics\Debug\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Diagnostics\Release\Iwesun.Runtime.Diagnostics.dll
lib\Iwesun.Runtime.Data\Debug\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Data\Release\Iwesun.Runtime.Data.dll
lib\Iwesun.Runtime.Networks\Debug\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.Networks\Release\Iwesun.Runtime.Networks.dll
lib\Iwesun.Runtime.WebView2\Debug\Iwesun.Runtime.WebView2.dll
lib\Iwesun.Runtime.WebView2\Release\Iwesun.Runtime.WebView2.dll
```

各库目录顶层同名DLL是Release兼容入口。跨仓库`ProjectReference`被禁止，消费项目必须引用安装目录
或解压后的同配置DLL。

## 3. 统一交付目录

本版全部可分发文件集中在：

```text
artifacts\packages\Iwesun.Runtime.1.0.36-beta.1\
```

目录固定包含：

- `Iwesun.Runtime.1.0.36-beta.1.msi`
- `Iwesun.Runtime.1.0.36-beta.1.zip`
- `Iwesun.Runtime.Networks.3.0.0-beta.3.nupkg`
- `Iwesun.Runtime.Networks.3.0.0-beta.3.snupkg`
- `IWESUN_RUNTIME_1.0.36_BETA_RELEASE_GUIDE.md`
- `RELEASE_MANIFEST.md`
- `SHA256SUMS.txt`

所有消费方先核对`SHA256SUMS.txt`，再选择MSI、便携包或本地Networks包。不得混入其他版本DLL。

## 4. β质量门

- Data Debug/Release：各101项全部通过。
- Networks Debug/Release：各188项全部通过。
- Runtime根解决方案Debug/Release：零警告、零错误。
- Runtime Debug/Release功能场景、Publish staging、自检、安装版SampleHost双配置和MSI Rebuild通过。
- 发布树拒绝`Iwesun.Data.dll`、DList、RecordStore V1、`BinaryIpAddress`及版本后缀网络合同。

## 5. 强制迁移

- 旧IP/MAC字符串记录改用`IpAddressValue`和`MacAddressValue`。
- 旧RecordStore/DList名称不保留别名。
- Networks请求必须由调用源生成非空`RequestId`。
- 旧V2/V3、可空Route、旧Flags和旧路由适配器继续保持编译错误。
- Debug项目只能引用Debug目录，Release项目只能引用Release目录。

## 6. 普通β测试重点

1. 消费项目在只引用本版DLL时完成Debug/Release全量编译。
2. UDP持续流量记录活动槽、隔离槽、单池峰值、容量拒绝和空闲池回收。
3. IPv6精确接口Ping区分协议结果与访问合规证据。
4. ARP/IPv6邻居消费代码验证纯IP、MAC值Null及独立接口索引。
5. RecordStore继承表验证MergeAdd、Clear、Snapshot领取和恢复。
6. WebView2真实STA宿主验证初始化失败回滚、重复启停与停止补偿。

## 7. 正式版阻塞项

- WFP双真实网关并发及M11剩余真实网络矩阵。
- DDNS Snap可能执行RS、DHCPv6 renew及网卡重启的完整Service恢复链。
- 用户全流量确定UDP 64槽保护值是否需要调整。
- Aether多日端到端统计结果。
- 干净环境restore/build/test/install、升级/卸载和最终版本冻结。
