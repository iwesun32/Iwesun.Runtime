# Iwesun.Runtime.Networks 2.0.1发布说明

## 修复

- IPv6 Ping按接口索引解析源地址时，跳过Windows上不支持IPv6属性查询并返回10043的适配器。
- IPv4 Ping同步按适配器隔离协议属性查询失败。
- 初始路由与重试路由解析异常统一进入跟踪失败闭环，不再终止端点发送线程或宿主进程。

## 验证要求

- Networks Release测试必须全部通过。
- DDNS Snap必须使用2.0.1包重新构建并启动Server。
- 通过Runtime CLI主动运行地址守护的同一条命令，确认Server持续存活且请求按`RequestId`完成或失败闭环。
