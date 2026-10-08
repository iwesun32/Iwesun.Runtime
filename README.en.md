# iwesun Runtime

**A .NET foundation for AI-operated diagnostics and service lifecycle management.**

[中文](README.md) · [User guide](docs/en/USER_GUIDE.md) · [Releases](https://github.com/iwesun32/Iwesun.Runtime/releases) · [Contributing](docs/en/CONTRIBUTING.md) · [License](LICENSE)

iwesun Runtime connects application diagnostics, managed execution, small in-memory relational tables, WebView2 control and parallel network endpoints through reusable .NET components.
After host integration and authorization, an AI assistant can inspect state, trace business execution, access permitted object members and run debugging checks through a structured CLI and an integration skill.

Capabilities: [AI diagnostics](docs/en/USER_GUIDE.md#diagnostics) · [Service lifecycle](docs/en/USER_GUIDE.md#diagnostics) · [RecordStore](docs/en/USER_GUIDE.md#data) · [WebView2 automation](docs/en/USER_GUIDE.md#webview2) · [Parallel networking](docs/en/USER_GUIDE.md#networks).

Related topics: [.NET](https://github.com/topics/dotnet) · [C#](https://github.com/topics/csharp) · [Diagnostics](https://github.com/topics/diagnostics) · [Windows Service](https://github.com/topics/windows-service) · [WebView2](https://github.com/topics/webview2) · [Networking](https://github.com/topics/networking).

## Four capabilities

1. **AI and remote diagnostics; service lifecycle.** Structured output, conditional trace points, cooperative breakpoints, allowlisted object access, and coordinated shutdown for managed processes, threads and tasks. CLI and service shutdown share a common lifecycle. Debug and Release have different debugging capabilities.
2. **Small relational data structures.** `RecordStore<TValue,TPrimaryKey>` provides primary keys, stable record identity, multiple keys, adaptive indexes, constraints, merging, snapshots, views and publication. Performance depends on workload and indexing; scale tools are included.
3. **WebView2 control through the CLI.** Sessions, live DOM/XPath access, input, network observation and page evidence use controlled APIs. CDP node identity, revisions and refresh handling support dynamic pages.
4. **Parallel, multi-input/multi-output networking.** Batch submission, concurrent execution and asynchronous responses share explicit access plans, request correlation, bounded retries and backpressure. Public identity is `RequestId → AttemptId → BranchId → ResponseId`; multi-response behavior follows each protocol's contract.

Build on Windows with .NET 10: `dotnet build Iwesun.Runtime.slnx -c Release`.
WebView2 samples require the WebView2 Runtime. Consumers should reference the published Debug/Release DLLs, rather than cross-repository project references.
See the [English documentation index](docs/en/README.md) and [release guide](docs/en/RELEASE_GUIDE.md).

The initial public candidate is Runtime **1.0.47-beta.1**, with Networks **3.0.0-beta.6**. Beta validation does not certify every real-network, browser or long-running workload. The unfinished Tables project is excluded; existing Data/RecordStore remains available.

## License and maintenance

This project is **source available**, not OSI open source. Noncommercial use is free with attribution and license preservation. Commercial use needs separate permission. Independent distribution of modified Runtime versions requires prior written approval. Read [LICENSE](LICENSE) for the operative terms.

The public author and brand are **iwesun**. The GitHub username is `iwesun32` because of username availability.
GPT/Dot and other authorized AI assistants assist with routine maintenance under [GOVERNANCE.md](GOVERNANCE.md); this is not official OpenAI sponsorship or maintenance.

Contributions and working-group applications are welcome. Use [CONTRIBUTING.md](CONTRIBUTING.md), [FORK_POLICY.md](FORK_POLICY.md) and [SECURITY.md](SECURITY.md).

Copyright © 2026 iwesun. Third-party materials retain their own licenses.
