# Iwesun.Runtime.Networks 3.0.0-beta.5 升级迁移报告

> **状态**：CURRENT  
> **发布日期**：2026-09-22  
> **替代版本**：3.0.0-beta.4  
> **适用范围**：仅 `Iwesun.Runtime.Networks` 独立普通β交付

## 发布原因

已有安装目录和本地包仍为`3.0.0-beta.4`，其构建快照早于当前Networks源码。本轮新增的长期TCP
连接与转发数据面合同尚未进入任何用户可取得的包，因此`beta.4`不得再被描述为当前版本。

本次不重打全量Runtime安装包。当前工作区还包含未完成的Web/WebView2独立改动；将其混入仅为
Networks反馈而发起的交付会扩大测试面并破坏版本可追溯性。

## 新增能力

### 长期TCP连接

`NetworkSocketExecutor.OpenTcpAsync`返回`NetworkTcpConnectionOpenResult`。成功时，调用方通过
`NetworkTcpConnection.Stream`进行双向读写，并从连接对象取得请求、解析计划、四级GUID执行身份及
路径结果。

对于`ExactNextHop`，策略句柄由连接对象拥有：连接流未释放前策略不得撤销。调用方必须在读写结束后
调用`Dispose`或`DisposeAsync`。打开失败、取消和清理失败会返回真实失败结果，不能把未建立连接
包装为可用流。

### 转发数据面边界

`INetworkPacketDataPlane`新增`PacketCaptured`和`InjectAsync`。捕获对象包含：

- 内部`NetworkFlowSerial`，用于进程活动域内高频传输关联；
- 对外四级GUID执行身份，用于请求、尝试、分支和响应证据链；
- 入/出方向、接口索引、不可变数据包视图与UTC时间戳。

这是后续DNS代理、NAT IP路由、Socket/HTTP代理、三层交换或独立虚拟网卡后端可接入的稳定抽象。
它不改变默认Socket数据面的能力声明：没有实际Packet后端时，调用必须被明确拒绝，不能伪造抓包或注入成功。

## 消费者迁移

纯UDP、DNS、Ping、HTTP、DoH和现有`NetworkTcpConnectEndpoint<TKey>`消费者无需修改。
需要长期TCP流的消费者改用`OpenTcpAsync`，并以`await using`或`using`限制连接生命周期：

```csharp
var opened = await executor.OpenTcpAsync(plan, cancellationToken);
if (!opened.Result.Succeeded || opened.Connection is null)
{
    return;
}

await using var connection = opened.Connection;
await connection.Stream.WriteAsync(payload, cancellationToken);
```

不得以`TKey`作为内部完成关联；`NetworkFlowSerial`同样不是跨进程或跨机器的业务主键。公开协议跟踪继续
以`RequestId → AttemptId → BranchId → ResponseId`四级GUID链为唯一事实。

## 验证与交付

- Debug/Release合同测试：各208项通过；
- 交付物：Release NuGet包、符号包、Debug/Release DLL、文档和SHA-256外部清单；
- 未运行任何会改变系统网络状态的管理员WFP、DHCPv6或网卡恢复动作；这些仍按
  [发布状态](../RELEASE_STATUS.md)中的真实网络门禁单独执行。

安装或消费本包前，核对程序集产品信息版本为`3.0.0-beta.5`。程序集版本保持`3.0.0.0`是预发布
3.0系列的既定装载合同，不能只凭程序集版本判断是否已升级。
