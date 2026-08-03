# Iwesun.Networks 示例程序

本项目是公共库的可运行示例，不包含 Aether 或其他业务代码。当前示例使用Networks 3.0显式访问计划执行TCP探测，并输出完整Request/Attempt/Branch身份及实际路径证据。

```powershell
dotnet run --project Iwesun.Networks.Examples -- --help
```

系统选路TCP探测：

```powershell
dotnet run --project Iwesun.Networks.Examples -- tcp 1.1.1.1 443
```

完整接口语义见[Socket执行合同](../../docs/02-endpoints/SOCKET_EXECUTION_3_0.md)和
[精确网络访问最终设计](../../docs/01-design/PRECISION_NETWORK_ACCESS_CONTROL_FINAL_DESIGN.md)。
