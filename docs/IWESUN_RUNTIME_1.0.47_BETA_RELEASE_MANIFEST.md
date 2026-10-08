# Runtime 1.0.47-beta.1 发布清单

日期：2026-10-09。状态：公开 β 交付；以下为本地门禁记录，实际下载资产以对应 GitHub Release 和 SHA-256 清单为准。

| 交付物 | 内容 |
| --- | --- |
| Iwesun.Runtime.1.0.47-beta.1.msi | Windows 安装载荷，含 Debug/Release |
| Iwesun.Runtime.1.0.47-beta.1.zip | 完整便携 app/data 载荷 |
| Iwesun.Runtime.Networks.3.0.0-beta.6.nupkg | Networks Release 包及许可证 |
| Iwesun.Runtime.Networks.3.0.0-beta.6.snupkg | Networks 调试符号包 |
| SHA256SUMS.txt | 对应交付物 SHA-256 |
| GitHub 标签源码 | 公开源码、测试、样例与治理文件；不含 Tables 工程 |

本包不包含未发布的 Web/Tables DLL。第三方许可随 `app/LICENSE`、`app/NOTICE`、`app/THIRD_PARTY_NOTICES.md` 和 `app/legal/third-party` 提供。

## 门禁记录

- 根 Release 构建：通过，0 警告、0 错误。
- Debug/Release 模块测试均通过：Networks 各 212 项、Data 各 101 项、WebView2 各 53 项。
- Diagnostics 功能验证 Debug 30 场景、Release 25 场景均通过；针对 1.0.46 编译的旧宿主加载本候选并完成启动/退出检查。
- Debug/Release/Publish 根构建通过；首次 staging 自检识别出解决方案平台映射造成的 Data 副本哈希不一致，已隔离发布子构建配置并通过重新自检，7 份 Data DLL 均匹配对应配置。
- 自检包含 CLI 启动、Debug/Release 消费示例构建及 DLL 一致性。中英文公开文档加入交付门禁；最终包生成以发布流程成功及资产哈希为准。
- Networks nupkg/snupkg 与 MSI 构建通过；发布流程从确定提交重新执行验证并生成不可覆盖的候选目录。
- Gitleaks 8.30.1：公开副本及现有 Git 历史扫描通过；结果是规则扫描证据，不代表绝对无敏感数据。
- Tables：本地未提交，远端历史不存在；公开副本不包含工程或解决方案引用。
- 真实网络/浏览器全面矩阵与长期压力：本轮不宣称通过；继续作为正式稳定版门禁。

## 发布边界

不安装或覆盖本机已有消费宿主，不上传 nuget.org。MSI 未签名；使用前核对来源、哈希和必需运行环境。
源码、首次入门和发布说明提供中英文入口；深入设计与历史技术文档主要为中文，不宣称已全部翻译。
本地自动维护已配置为每 6 小时巡检，执行依赖电脑、应用、认证和网络可用。
