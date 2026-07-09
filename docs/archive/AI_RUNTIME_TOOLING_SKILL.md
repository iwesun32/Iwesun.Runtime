---
name: ai-runtime-tooling
description: Use when working on DDNS Snap or compatible projects that use Iwesun.Runtime.Diagnostics, Iwesun.Runtime.WebView2, Iwesun.Runtime.Cli, runtime monitor points, reflected runtime objects, WebRuntime pipe commands, or AI-controlled debugging without console/file logs.
---

# AI Runtime Tooling Skill

Use this skill for runtime diagnostics and WebRuntime client work.

## Read First

- `docs/AI_RUNTIME_TOOLING.md`
- `docs/RUNTIME_DIAGNOSTICS.md`
- `docs/RUNTIME_DIAGNOSTICS_ROADMAP.md`
- `Iwesun.Runtime.Cli/Program.cs`
- `Iwesun.Runtime.Diagnostics/DiagnosticSwitchboard.cs`
- `Iwesun.Runtime.Diagnostics/DiagnosticSwitchboardConfig.cs`
- `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsMonitor.cs`
- `Iwesun.Runtime.WebView2/WebRuntimePipeClient.cs`

## Core Rule

Do not add direct console output, ad hoc log files, jsonl traces, screenshots,
or temporary debug records for runtime diagnostics. Use the managed runtime
surface and restore quiet mode after use.

## Diagnostics Commands

Prefer these client commands:

```bash
iwrt status
iwrt points --section=agent-sync
iwrt point switchboard.control
iwrt focus-point switchboard.control
iwrt events-clean 100
iwrt quiet
```

For reflected state:

```bash
iwrt invoke service.root-snapshot GetSnapshot
```

## WebRuntime Commands

Use only when a compatible WebRuntime host is running:

```bash
iwrt --web-pipe=AIGateway.WebRuntime webview2 doubao-web capabilities
iwrt --web-pipe=AIGateway.WebRuntime webview2 doubao-web snapshot
```

DDNS Snap packages the WebView2 client but does not host WebView2 sessions.

## Reflection Pattern

Use explicit access rules:

```csharp
hub.RegisterObject("agent.worker", worker, new RuntimeDiagnosticObjectAccess
{
    AllowReadAllPublic = true,
    InvokableMembers = ["TriggerCycle", "RunCycleNowForDiagnostics"]
});
```

Do not expose broad write or invoke access.

## Workflow

1. Run `status`.
2. Query targets and points.
3. Inspect a reflected object if useful.
4. Use `focus-point`, `focus-section`, or `focus-section-points`.
5. Trigger the smallest safe action.
6. Drain `events-clean`.
7. Run `quiet`.
8. Report which point/section was opened and confirm restoration.

## Validation

```bash
dotnet build DdnsSnap.slnx -c Debug -p:UseSharedCompilation=false
```

Then run a real process smoke test through `Iwesun.Runtime.Cli` when
behavior changes. Unit tests alone are not enough for pipe/thread behavior.
