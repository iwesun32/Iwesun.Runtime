# Copilot Instructions

## Context Efficiency Rules
**These rules are mandatory and override all other behaviors:**

1. **Never scan the entire repository.** Start from the smallest relevant file, symbol, or document.
2. **Respect `.copilotignore` boundaries.** Build outputs, archives, large generated files, and caches are off-limits unless the user explicitly asks.
3. **Prefer targeted reads.** Search first, then read only the smallest relevant ranges.
4. **Ignore binaries and large files by default.** Do not read binary files. For miscellaneous or experimental non-source files larger than 10 KB (10240 bytes), read at most the first 20 lines unless the user explicitly requests deeper reading. Source files and `.md` documents are exempt from this threshold.
5. **Never write files from the terminal.**
   - Do not use `>`, `>>`, `Out-File`, `Set-Content`, `Add-Content`, or shell-invoked file APIs.
   - Use editor tools for all file edits and new files.
5. **Avoid destructive commands.** Do not use destructive shell or Git commands unless the user explicitly requests them.
6. **Build artifacts are not source code.** Never read files from `bin/`, `obj/`, `Debug/`, `Release/`, `publish/`, or `net*` directories.
7. **Prefer short, direct responses.** Do not output large code blocks unless the user asks.
9. **Execute explicit requests directly.** Do not stop at analysis when a safe edit can complete the task.

## Requirements-First Workflow (Global Convention)
This workflow is mandatory for future tasks.

1. Convert relevant user dialogue into a requirements document before implementation.
2. Re-read the requirements document before execution and verify task-request alignment.
3. Re-read the requirements document after execution to check for hidden or overshadowed details.
4. Keep the active requirements in `docs/REQUIREMENTS_ACTIVE.md` and update it when scope changes.

## Project Overview
Iwesun Runtime is a .NET 10 runtime diagnostics and tooling repository. It provides a diagnostics library, a standalone CLI, and shared WebRuntime models used by the CLI.

DDNS Snap is an active consumer of this repository. Keep Runtime guidance reusable from the active docs and avoid duplicating design changes into host repositories.

## Available Instruction Files

When relevant, consult these repository-local files:

| File | Purpose |
|------|---------|
| `.github/instructions/copilot-access-rules.instructions.md` | Global access boundaries, safety rules, and context efficiency guidance |
| `AGENTS.md` | Repository entrypoint with architecture, coding rules, and Runtime-specific constraints |

## Global Build Settings
`Directory.Build.props` sets repository-wide defaults:

- `LangVersion=latest`
- `Nullable=enable`
- `ImplicitUsings=enable`
- Release versions are declared in `Directory.Build.props`; Networks maintains its own package version.

## Build And Validation

- Release build: `dotnet build Iwesun.Runtime.slnx -c Release`
- Focused work: use the domain `.slnx` under `modules/<Domain>/`; unit, functional, scale, sample, and validation projects live beside their implementation domain.

## Repository Structure

- `modules/Diagnostics/` - Core diagnostics library, functional tests, and canonical SampleHost
- `modules/Data/` - Runtime data library, tests, scale tool, and authoritative domain docs
- `modules/Networks/` - Network library, tests, samples, WFP validation, and authoritative domain docs
- `modules/WebView2/` - Shared WebRuntime library, sample host, and domain docs
- `modules/Cli/`, `modules/RemoteConsole/` - Command-line and remote-control applications
- `modules/Packaging/` - Release aggregation and MSI setup projects
- `docs/` - Active Chinese documentation; begin with `docs/README.md` and `docs/REQUIREMENTS_ACTIVE.md`.

## Host Template And Injector (standardized flow)

Host integration goes through `RuntimeHostTemplate` extension methods — prefer these over calling `AddRuntimeDiagnostics`/`UseRuntimeDiagnostics`/`BuildDiagnosticRegistries` directly:

- `services.Start(runtimeDirectory)` (registers diagnostics DI)
- `provider.Activate(hostAssembly)` (starts pipe + builds registries)
- `RuntimeShutdownCoordinator.ShutdownAsync(timeout, payload)` (coordinated shutdown)

Business code injects through the `RuntimeInjector` static facade (`Output` / `Watch` / `Break` / `Data` / `Thread` / `Task`), and may wrap primitives with `RProcess` / `RThread` / `RTask` for auto-registration with `RuntimeExecutionManager`. See `modules/Diagnostics/samples/Iwesun.Runtime.SampleHost/Program.cs`.

## Runtime-Specific Rules

1. **Never add `Console.WriteLine` or temporary log files** for diagnostics. Use `RuntimeOutput.TracePoint()` / `RuntimeOutput.Log()` and the switchboard pipeline.
2. **Default diagnostics state must remain silent.** New sections, points, and outputs default to `false`. Operational docs should restore silence with `quiet` after inspection.
3. **Reflection access must stay explicit.** Register targets through approved whitelists; do not expose broad reflective surfaces.
4. **Schema changes require versioning.** When adding or changing compiled output points in `DiagnosticSwitchboardCompiledConfig`, bump `SchemaVersion`.
5. **Do not hand-roll pipe clients or framing.** Reuse the existing 4-byte little-endian framing model and shared protocol types.
6. **Do not hardcode pipe names.** Use `DiagnosticPipePrefix.Resolve(channel)` or repository defaults.
7. **Use `#if DEBUG` for breakpoint and hook injection.** `BreakIf` and watch-style instrumentation are not default production behavior.
8. **Defensive execution is required.** Validate prerequisites and skip invalid inputs locally instead of crashing the process.
9. **Do not keep legacy debug outputs alive.** File-output and console-output debug paths should be removed from active workflows and only reintroduced through the new diagnostics flow when explicitly needed.
10. **Prefer the new startup and execution model.** Use the updated host startup pattern and the managed process/thread approach instead of ad-hoc inheritance or direct primitive calls.
11. **Use the new CLI for debugging.** Follow the current command grammar and route debugging through `Iwesun.Runtime.Cli` rather than older entry points.

## Coding And Documentation Rules

- Code identifiers, comments, and XML docs are in **English**.
- Design and usage docs under `docs/` are in **Chinese**.
- AI-facing files (`.github/instructions/`, `AGENTS.md`, `copilot-instructions.md`) are in **English**.
- Private fields use `_camelCase`.
- Prefer existing patterns and minimal changes over new abstractions.
- Use `ArgumentNullException.ThrowIfNull()` or equivalent guards for public constructor dependencies.
- Use async APIs with `CancellationToken` when the surrounding code already follows that pattern.

## Relationship To Host Repositories

This repository is consumed by host applications such as DDNS Snap through the versioned DLLs installed under `C:\Program Files\Iwesun\Runtime`; cross-repository `ProjectReference` entries are forbidden. Runtime design rules live here. Host repositories should reference Runtime as an external library and should not duplicate Runtime implementation documents.
