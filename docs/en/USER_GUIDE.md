# iwesun Runtime user guide

[中文](../GETTING_STARTED.md) · [Overview](../../README.en.md) · [Documentation](README.md) · [Release guide](RELEASE_GUIDE.md)

Applies to Runtime `1.0.47-beta.1` / Networks `3.0.0-beta.6`. Author and brand: iwesun.

This is an introduction, not a complete integration contract. Follow the [specification framework](SPECIFICATION.md), read the selected module contracts and perform host acceptance. The [full catalog](../DOCUMENTATION_CATALOG.md) links the complete public corpus, primarily Chinese originals.

## What it does

An integrated, authorized AI assistant or operator can use a structured CLI to inspect application state, trace execution, diagnose failures and check fixes. The same foundation standardizes startup, managed execution and coordinated shutdown.
Data/RecordStore, WebView2 control and parallel network endpoints are reusable components; consumers need not enable every module.

## Download, prerequisites and first run

1. Download the MSI or ZIP and `SHA256SUMS.txt` from the same [GitHub release](https://github.com/iwesun32/Iwesun.Runtime/releases). Verify SHA-256 before use.
2. Building examples requires Windows and the .NET 10 SDK. Running applications requires the appropriate .NET 10 runtime; the WebView2 sample also requires WebView2 Runtime. The MSI is not an offline installer for those prerequisites.
3. The MSI installs under `C:\Program Files\Iwesun\Runtime`. The ZIP has separate `app` and `data` directories. Do not overwrite existing business data.
4. Read the bundled guide and run `app/scripts/verify-runtime-install.ps1` to check files, versions, CLI startup and sample references. It builds samples, so the SDK is required.
5. Reference matching Debug/Release DLLs from the distribution, not project files in another repository. Rebuild the consumer's own deployment after upgrading.

Use PowerShell `Get-FileHash -Algorithm SHA256` to calculate a download's hash. This release is not Authenticode-signed; checksums do not replace trusted-source verification.

## Minimal host integration

Start from `samples/templates/RuntimeHost.Startup.Minimal.Template.cs.txt`. Preserve the previous entry point as a non-compiling backup.
Reference Diagnostics and Data DLLs for the same build configuration. Align the host's `Microsoft.Extensions.*` dependencies with 10.0.9 or compatible newer versions; do not mix old preview dependencies.

```csharp
using System.Reflection;
using Iwesun.Runtime.Diagnostics;
using Microsoft.Extensions.Hosting;

[assembly: DiagnosticPipePrefix("MyProduct")]

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddRuntimeDiagnostics();
builder.Services.Start(
    runtimeDirectory: Path.Combine(AppContext.BaseDirectory, "runtime"),
    startupRuntimeDiagnosticsPipeName: "MyProduct.RuntimeDiagnostics",
    pipeAccessOptions: new RuntimeNamedPipeAccessOptions
    {
        AllowLocalInteractiveUsers = true,
        AllowAuthenticatedUsers = false,
        AllowedWindowsPrincipals = []
    });

// Add business services here.
using var host = builder.Build();
host.Services.Activate(Assembly.GetExecutingAssembly());
await host.RunAsync();
```

The runtime directory must be writable. Services should use an approved ProgramData directory with appropriate account permissions, not write application data under Program Files.
For Windows Services, use `StartWindowsService` or `StartConfiguredWindowsService` and provide the service identity and shutdown timeout. SCM and CLI shutdown use coordinated cleanup. See the [detailed service reference, Chinese](../IWESUN_RUNTIME_WINDOWS_SERVICE.md).

<a id="diagnostics"></a>
## AI diagnostics and managed execution

- Run the CLI with `--help`, discover/select the intended host, and start with status and allowlisted reads. Use the [CLI reference, Chinese](../IWESUN_RUNTIME_CLI.md) for complete command grammar.
- Use the bundled [iwesun-runtime-integration skill](https://github.com/iwesun32/Iwesun.Runtime/tree/main/skills/iwesun-runtime-integration) for AI workflows. A skill does not grant permissions beyond host configuration.
- Adopt `RuntimeInjector`, `RProcess`, `RThread` and `RTask` where managed execution is appropriate. Do not mechanically replace every `Task<T>`, async return type or UI/STA thread.
- Allowlist reflective reads, writes and calls separately. Cooperative breakpoints pause the calling chain, not every thread. Keep breakpoint and Watch injection inside `#if DEBUG`; do not assume all debug capabilities exist in Release.
- Diagnostics are silent by default. Restore `quiet` after inspection. Use the Runtime output pipeline, not ad-hoc console or temporary-file diagnostics.
- Validate actual completion, cleanup and unregistration during shutdown. A timeout is not successful shutdown.

<a id="data"></a>
## Data / RecordStore

`RecordStore<TValue,TPrimaryKey>` supports small in-process relational tables with stable record identity, multiple keys, indexes, constraints, snapshots and persistence.
It is not presented as a universal SQL/database replacement. Evaluate performance against your own scale, indexes and read/write mix.
Start with the [Data documentation and scale tooling](https://github.com/iwesun32/Iwesun.Runtime/tree/main/modules/Data). The unfinished Tables project is excluded; Data is included.

<a id="webview2"></a>
## WebView2 automation

The CLI connects to an integrated WebView2 host for session control, DOM/XPath access, input and network evidence.
Dynamic pages require current node identity, revisions and refresh handling. Reading page content does not authorize clicks, downloads or arbitrary execution.
Use the [WebView2 sample](https://github.com/iwesun32/Iwesun.Runtime/tree/main/modules/WebView2/samples) and validate against your target host and pages.

<a id="networks"></a>
## Parallel network endpoints

Submit multiple requests, execute concurrently and collect asynchronous responses. Correlation follows `RequestId → AttemptId → BranchId → ResponseId`.
Query endpoint capabilities before selecting access plans, timeouts, cancellation and backpressure. Unsupported exact-access constraints must fail explicitly, not silently fall back.
See [Networks documentation and examples](https://github.com/iwesun32/Iwesun.Runtime/tree/main/modules/Networks). Complete real-WFP, next-hop and endurance validation is outside this beta's verified scope.

## Build and troubleshoot

```powershell
dotnet build Iwesun.Runtime.slnx -c Release
dotnet test modules/Data/tests/Iwesun.Runtime.Data.Tests/Iwesun.Runtime.Data.Tests.csproj -c Release
dotnet test modules/Networks/tests/Iwesun.Runtime.Networks.Tests/Iwesun.Runtime.Networks.Tests.csproj -c Release
dotnet test modules/WebView2/tests/Iwesun.Runtime.WebView2.Tests/Iwesun.Runtime.WebView2.Tests.csproj -c Release
```

For load failures, check build configuration, file versions, stale consumer DLLs and Extensions dependencies. For connection failures, check the running process, selected pipe, account permissions and host registration.
Report the version, a minimal reproduction and redacted evidence. Do not attach credentials, private application data or large raw snapshots.

## License and participation

This is a noncommercial source-available project, not OSI open source. Noncommercial use is free with notices preserved; commercial use and independent modified distributions must follow [LICENSE](https://github.com/iwesun32/Iwesun.Runtime/blob/main/LICENSE).
Use [Contributing](CONTRIBUTING.md) for working-group applications and PRs. Membership does not grant repository write access.
AI maintenance follows explicit governance and is not official OpenAI sponsorship. This English guide covers public onboarding; detailed design documents are currently primarily Chinese.
