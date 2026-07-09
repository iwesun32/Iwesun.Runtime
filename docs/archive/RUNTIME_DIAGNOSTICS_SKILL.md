---
name: ddnssnap-runtime-diagnostics
description: Use when working on DDNS Snap runtime diagnostics, DiagnosticSwitchboard, RuntimeDiagnosticHub, Iwesun.Runtime.Cli, monitor points, reflected runtime objects, or AI-controlled debugging without console/file logs.
---

# DDNS Snap Runtime Diagnostics Skill

This is the repository copy of the Codex skill installed at:

```text
C:\Users\LYH\.codex\skills\ddnssnap-runtime-diagnostics\SKILL.md
```

Use it when changing or operating `Iwesun.Runtime.Diagnostics` and the
Service/Agent runtime diagnostics injection.

## Rule

Do not add direct console output, ad hoc log files, jsonl traces, screenshots,
or temporary debug records for runtime diagnostics.

Default state must stay quiet:

```text
globalEnabled = false
pipeOutputEnabled = false
fileOutputEnabled = false
all sections = false
all output points = false
```

## Read First

- `docs/RUNTIME_DIAGNOSTICS.md`
- `docs/RUNTIME_DIAGNOSTICS_ROADMAP.md`
- `src/Iwesun.Runtime.Diagnostics/DiagnosticSwitchboard.cs`
- `src/Iwesun.Runtime.Diagnostics/DiagnosticSwitchboardConfig.cs`
- `src/Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsMonitor.cs`
- `src/Iwesun.Runtime.Cli/Program.cs`

## AI Bridge

The AI controls diagnostics by running:

```bash
iwrt monitor
iwrt list
iwrt status
iwrt points --section=agent-sync
iwrt point agent.worker-cycle
iwrt invoke service.root-snapshot GetSnapshot
```

The client connects to the named pipe and returns JSON. The AI should not
normally hand-write pipe scripts.

Preferred shortcuts:

```bash
iwrt focus-point agent.worker-cycle
iwrt focus-section agent-sync
iwrt focus-section-points agent-sync
iwrt events-clean 100
iwrt quiet
```

WebView2 bridge commands are also available for compatible hosts such as
AIGateway:

```bash
iwrt --web-pipe=AIGateway.WebRuntime webview2 doubao-web capabilities
iwrt --web-pipe=AIGateway.WebRuntime webview2 doubao-web snapshot
```

DDNS Snap does not currently host WebView2 sessions; use these commands only
when a compatible WebRuntime pipe is running.

## Focused Monitoring

```bash
iwrt pipe on
iwrt enable agent-sync
iwrt enable-point agent.worker-cycle
iwrt enable
iwrt events 100 --excludeKinds=command,switch
iwrt disable-point agent.worker-cycle
iwrt disable agent-sync
iwrt disable
iwrt pipe off
```

Always report which points were opened and confirm quiet mode was restored.

## Reflection Pattern

Use explicit access rules:

```csharp
hub.RegisterObject("agent.worker", worker, new RuntimeDiagnosticObjectAccess
{
    AllowReadAllPublic = true,
    InvokableMembers = ["TriggerCycle", "RunCycleNowForDiagnostics"]
});
```

Writable and invokable members must be intentional and whitelisted.

## Validation

```bash
dotnet build DdnsSnap.slnx -c Debug -p:UseSharedCompilation=false
```

```bash
rg -n "Console\.(WriteLine|Error\.WriteLine|Write)|Debug\.WriteLine|Trace\.WriteLine|Serilog|WriteTo\.|Log\.(Information|Warning|Error|Debug|Trace|Critical)" src --glob '!**/bin/**' --glob '!**/obj/**'
```
