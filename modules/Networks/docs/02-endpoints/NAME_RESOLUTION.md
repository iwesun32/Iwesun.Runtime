# 名称解析端点

> **状态**: CURRENT | **最后更新**: 2026-07-22

## PTR反向解析

`NetworkDnsReverseLookupEndpoint<TKey>`把查询对象与Resolver访问路径分离：`QueryAddress`是要反查的地址，`NetworkDnsResolverSelection`选择DNS服务器，`ResolverAccessPlan`控制访问该服务器的源、接口和下一跳。

端点直接构造PTR wire查询并验证事务号。UDP响应带TC位时，在同一Branch内创建TCP阶段；UDP和TCP各自保存执行证据，ExactNextHop时分别安装请求级WFP策略。

## NetBIOS节点状态

`NetworkNetBiosNameEndpoint<TKey>`直接执行NBNS UDP Node Status，不启动`nbtstat`。单播使用首个有效响应闭环；IPv4广播必须使用`CollectUntilWindowEnds`，窗口内每个响应拥有独立ResponseId并保留发送者证据。

两个端点均强制非空`RequestedAccessPlan`和RequestId，不提供旧版本化端点、可空Route或隐式系统路径兼容入口。
