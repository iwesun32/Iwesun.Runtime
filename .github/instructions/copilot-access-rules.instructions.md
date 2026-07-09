---
description: "Global context efficiency, safety rules, and file access boundaries for all Iwesun Runtime operations. Applies to every file request."
applyTo: "**"
---
# Copilot Access Rules — Global Context Efficiency

## Scope

This instruction applies to **ALL operations** across the entire Iwesun Runtime repository.

## Core Principle: Minimum Context, Maximum Precision

Read only what is necessary for the current task. Prefer the smallest local code path or documentation slice that can disconfirm the current hypothesis.

## Global Safety Rules

1. **Never write, overwrite, or create files from the terminal.**
   - Do not use redirection (`>`, `>>`), `Out-File`, `Set-Content`, `Add-Content`, or .NET file write APIs from shell commands.
   - Use editor tools for all file creation and edits.
2. **Never use destructive cleanup commands unless the user explicitly asks.**
   - Examples: `git reset --hard`, `git clean -f`, `Remove-Item -Force`, `del /s /q`, `rd /s /q`.
3. **Search first, read second.** Use targeted search before opening files.
4. **Build outputs are not source code.** Ignore generated artifacts, caches, and package outputs.
5. **Ignore binaries and oversized files by default.** Do not read binary files or files larger than 200 KB unless the user explicitly requests it.

## Off-Limits Zones

### Hard-Blocked (`.copilotignore` — do not read or list)

| Path | Reason |
|------|--------|
| `ai-ignore/` | Large generated references, captures, screenshots, and historical material |
| `archive/`, `docs/archive/` | Archived material, not active instructions |
| `**/bin/`, `**/obj/`, `**/Debug/`, `**/Release/`, `**/publish/`, `**/net*/` | Build artifacts |

### Soft-Blocked (may list, do not auto-read)

| Path | Reason | Rule |
|------|--------|------|
| `release/` | Packed binaries and artifacts | May `list_dir`; do not read binary content |
| `tools/` | Future standalone utilities | May `list_dir`; read only when explicitly requested |
| `UpgradeLog*.htm` | Visual Studio upgrade logs | May list; do not read |

## Active Zones

Only these locations should normally be read without explicit user permission:

| Path | Content |
|------|---------|
| `Iwesun.Runtime.Diagnostics/` | Core diagnostics library |
| `Iwesun.Runtime.Cli/` | Standalone CLI host |
| `Iwesun.Runtime.WebView2/` | Shared WebRuntime models and client |
| `docs/` | Active design and usage documentation |
| `.github/` | AI instructions and agent configuration |
| `AGENTS.md` | Repository entrypoint for coding agents |
| `Directory.Build.props` | Repository-wide build settings |
| `Iwesun.Runtime.slnx` | Solution entrypoint |

## File Reading Strategy

1. **Read no more than 2-5 files by default.** Expand only when the current hypothesis cannot be tested locally.
2. **Use minimal line ranges.** Read only the surrounding lines needed for the current task.
3. **Prefer one nearby hop over broad exploration.** If the first file only forwards behavior, step once to the owning implementation.
4. **Use subagents only for broad read-only exploration.**

## Requirements-First Workflow

This workflow is mandatory for task execution:

1. Convert relevant user dialogue into a requirements document before implementation.
2. Re-read the requirements document before execution and verify alignment.
3. Re-read the requirements document after execution to ensure details were not overshadowed.
4. Keep active requirements in `docs/REQUIREMENTS_ACTIVE.md` and update it when scope changes.

## Response Rules

- Keep answers concise and concrete.
- Do not dump large code blocks unless the user asks for them.
- When editing, preserve existing structure and avoid unrelated cleanup.
