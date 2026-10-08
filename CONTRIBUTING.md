# 参与 iwesun Runtime

欢迎代码、文档、样例、复现步骤和测试贡献。先阅读 [LICENSE](LICENSE)、[治理规则](GOVERNANCE.md) 和 [行为规范](CODE_OF_CONDUCT.md)。

## 工作组与问题

通过工作组 Issue 表单申请参加维护。提交问题应包含 Runtime/模块版本、Debug 或 Release、平台、最小复现、预期及实际行为；清除凭据、Cookie、令牌和业务隐私数据。
安全问题使用 [SECURITY.md](SECURITY.md) 的私密报告方式。

## 提交修改

1. Fork 并建立贡献分支；为上游贡献而 Fork 不需要独立发行许可。
2. 修改前阅读 `AGENTS.md` 和相应模块文档，保持默认诊断安静、对象权限白名单和模块边界。
3. 针对修改执行构建与必要测试。公共 API 变更附迁移说明；性能结论附可复现工作负载。
4. 发起 PR，说明问题、变化、验证结果及限制。不要提交生成输出、凭据、现场快照或未完成的排除项目。
5. 代码、样例或其他可许可贡献须由每位贡献者明确接受 [CLA](CLA.md)；涉及单位权利时先取得单位许可。

PR 模板中的勾选是明确声明；自动回复、维护者替勾和沉默不视为贡献者接受 CLA。维护者核对签署声明、提交作者和权限来源，未齐备不合并。
AI 辅助贡献须披露使用范围，由提交者负责来源、正确性和验证。不得把 AI 输出宣称为天然无版权风险。

## 构建和测试

```powershell
dotnet build Iwesun.Runtime.slnx -c Release
dotnet test modules/Data/tests/Iwesun.Runtime.Data.Tests/Iwesun.Runtime.Data.Tests.csproj -c Release
dotnet test modules/Networks/tests/Iwesun.Runtime.Networks.Tests/Iwesun.Runtime.Networks.Tests.csproj -c Release
dotnet test modules/WebView2/tests/Iwesun.Runtime.WebView2.Tests/Iwesun.Runtime.WebView2.Tests.csproj -c Release
```

需要断点和注入回归时也执行 Debug。Diagnostics 功能测试及真实网络/浏览器验收见模块文档；不得擅自对生产系统执行恢复、停机或路由变更。
贡献不自动授权商业使用或独立分支发行；后者见 [FORK_POLICY.md](FORK_POLICY.md)。
