# 完整规范文档 r1 / Complete specification documentation r1

对应 Runtime **1.0.47-beta.1** / Networks **3.0.0-beta.6**。文档修订：**1.0.47-beta.1-r1**；固定标签：`docs-v1.0.47-r1`。

Runtime 是接入规范与公共库的组合，不是安装后即可自动正确使用的零配置工具。本次独立发布完整公开文档正文、规范阅读路线及配套文本，不修改已发布的 Runtime DLL、MSI、运行 ZIP 或 Networks 包。

## 阅读入口

- [中文规范总纲与必读路线](https://github.com/iwesun32/Iwesun.Runtime/blob/docs-v1.0.47-r1/docs/SPECIFICATION.md)
- [English specification framework](https://github.com/iwesun32/Iwesun.Runtime/blob/docs-v1.0.47-r1/docs/en/SPECIFICATION.md)
- [完整文档目录 / Complete catalog](https://github.com/iwesun32/Iwesun.Runtime/blob/docs-v1.0.47-r1/docs/DOCUMENTATION_CATALOG.md)
- [对应运行包 / Runtime binaries](https://github.com/iwesun32/Iwesun.Runtime/releases/tag/v1.0.47-beta.1)

## 下载内容

共 **177 份来源资料**，其中 **160 份 Markdown 正文**，其余为模板、配置及许可等文本；包内另附生成的导航页与清单。相对文件/目录引用检查无缺失；检查不覆盖标题锚点或远程网址，2 处仅在源码仓库中的引用和 7 处跨仓库引用另列于 manifest。

ZIP 保留源码目录及全部公开正文，覆盖架构、启动、注入、进程/线程/任务、状态、事件、协调退出、诊断、CLI、远程授权、Data、Networks、WebView2、迁移、验收、版本记录、AI 技能、模板、命令配置和许可。

解压后从 `INDEX.html` 导航，或用 Markdown 阅读器打开 `docs/SPECIFICATION.md`。`MANIFEST.json` 提供来源提交、逐文件 SHA-256 和引用边界；发布附件中的独立 manifest 与包内清单相同。`SHA256SUMS` 文件用于核对 ZIP 和 manifest。

部分正文链接指向源代码或跨仓库历史路径，需配合同标签源码及相应项目阅读；安装索引只适用于 MSI/运行包布局。文档包不附带运行二进制。

## 范围与限制

深入技术正文以中文原文为主；中英文规范总纲、使用指南和项目介绍不等于所有正文已完成英文翻译。历史发布、RFC 和实施计划保留其版本与状态，不能据此推断所有能力已实现或已验收。

Tables、Web 开发工程、私有交接和忽略归档不进入本次公开包。当前运行候选仍为 β；真实 WFP/下一跳矩阵和 WebView2 实际宿主、长期运行仍按运行版本清单执行独立验收。

## English summary

This documentation-only release publishes the complete public specification corpus and supporting material for Runtime **1.0.47-beta.1** / Networks **3.0.0-beta.6**. It adds required-reading routes, an English specification framework, a full catalog and a source-layout offline archive. Previously published runtime binaries and packages are unchanged.

The package contains **177 source texts**, including **160 Markdown documents**, plus generated navigation and manifest files. Relative file/directory references have no missing targets; heading anchors and remote URLs are not covered. Two repository-only references and seven cross-repository references are recorded separately in the manifest.

Extract the ZIP and open `INDEX.html`, or read `docs/en/SPECIFICATION.md` with a Markdown reader. The manifest records the source commit and per-file hashes; the checksum attachment covers both the ZIP and the standalone manifest. Source-code and cross-repository historical references require the corresponding repository; the installer index describes the separate runtime payload layout.

Most detailed references are Chinese originals, not full English translations. Plans and historical records are not implementation or acceptance guarantees. Unfinished Tables, the Web development project, private handoffs and ignored archives are excluded. Runtime remains beta with the acceptance limits stated in its release manifest.
