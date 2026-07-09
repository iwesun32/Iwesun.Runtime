# Copilot Instructions

## Context Efficiency Rules
**These rules are mandatory and override all other behaviors:**

1. **Never scan the entire repository.** Start from the smallest relevant file, symbol, or document.
2. **Respect `.copilotignore` boundaries.** Build outputs, archives, large generated files, and caches are off-limits unless the user explicitly asks.
3. **Prefer targeted reads.** Search first, then read only the smallest relevant ranges.
4. **Never write files from the terminal.**
   - Do not use `>`, `>>`, `Out-File`, `Set-Content`, `Add-Content`, or shell-invoked file APIs.
   - Use editor tools for all file edits and new files.
5. **Avoid destructive commands.** Do not use destructive shell or Git commands unless the user explicitly requests them.
6. **Build artifacts are not source code.** Never read files from `bin/`, `obj/`, `Debug/`, `Release/`, `publish/`, or `net*` directories.
7. **Prefer short, direct responses.** Do not output large code blocks unless the user asks.
8. **Execute explicit requests directly.** Do not stop at analysis when a safe edit can complete the task.

## Project Overview
Iwesun Runtime is a .NET 10 runtime diagnostics and tooling repository. It provides a diagnostics library, a standalone CLI, and shared WebRuntime models used by the CLI.

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
- `Version=1.0.0`

## Build And Validation

- Build: `dotnet build Iwesun.Runtime.slnx -c Release`
- This repository currently does **not** have a dedicated unit test project in the solution. Validate changes with focused builds unless or until tests are added.

## Repository Structure

- `Iwesun.Runtime.Diagnostics/` - Core diagnostics library consumed by host applications
- `Iwesun.Runtime.Cli/` - Standalone command-line client for diagnostics and WebRuntime control
- `Iwesun.Runtime.WebView2/` - Shared WebRuntime models and pipe client used by the CLI
- `docs/` - Active Chinese documentation

## Runtime-Specific Rules

1. **Never add `Console.WriteLine` or temporary log files** for diagnostics. Use `RuntimeOutput.TracePoint()` / `RuntimeOutput.Log()` and the switchboard pipeline.
2. **Default diagnostics state must remain silent.** New sections, points, and outputs default to `false`. Operational docs should restore silence with `quiet` after inspection.
3. **Reflection access must stay explicit.** Register targets through approved whitelists; do not expose broad reflective surfaces.
4. **Schema changes require versioning.** When adding or changing compiled output points in `DiagnosticSwitchboardCompiledConfig`, bump `SchemaVersion`.
5. **Do not hand-roll pipe clients or framing.** Reuse the existing 4-byte little-endian framing model and shared protocol types.
6. **Do not hardcode pipe names.** Use `DiagnosticPipePrefix.Resolve(channel)` or repository defaults.
7. **Use `#if DEBUG` for breakpoint and hook injection.** `BreakIf` and watch-style instrumentation are not default production behavior.
8. **Defensive execution is required.** Validate prerequisites and skip invalid inputs locally instead of crashing the process.

## Coding And Documentation Rules

- Code identifiers, comments, and XML docs are in **English**.
- Design and usage docs under `docs/` are in **Chinese**.
- AI-facing files (`.github/instructions/`, `AGENTS.md`, `copilot-instructions.md`) are in **English**.
- Private fields use `_camelCase`.
- Prefer existing patterns and minimal changes over new abstractions.
- Use `ArgumentNullException.ThrowIfNull()` or equivalent guards for public constructor dependencies.
- Use async APIs with `CancellationToken` when the surrounding code already follows that pattern.

## Relationship To Host Repositories

This repository is consumed by host applications such as DDNS Snap via `ProjectReference`. Runtime design rules live here. Host repositories should reference Runtime as an external library and should not duplicate Runtime implementation documents.