# CLI v3 Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the v2 CLI with a strictly validated, extensible v3 command catalog that compiles single commands and structured workflows into authoritative Runtime JSON frames.

**Architecture:** Split the executable into application, configuration, syntax, semantics, compilation, transport, and presentation units. The built-in and user JSON catalogs own all command routing; C# validates and compiles them without legacy inference or string-command workflow compatibility.

**Tech Stack:** C#/.NET 10, System.Text.Json, Windows named pipes, RuntimeDiagnosticFrame rtdiag/2.0 and rtdiag/3.0, existing functional-test executable.

---

### Task 1: Define v3 configuration and result contracts

**Files:**
- Create: `Iwesun.Runtime.Cli/Configuration/CliConfiguration.cs`
- Create: `Iwesun.Runtime.Cli/Semantics/CommandDescriptor.cs`
- Create: `Iwesun.Runtime.Cli/Semantics/ParameterDescriptor.cs`
- Create: `Iwesun.Runtime.Cli/Presentation/CliResult.cs`
- Create: `Iwesun.Runtime.Cli/Application/CliExitCode.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add a child scenario that expects the v3 contract types**

Add scenario `cli-v3-contracts` and assert that serializing a minimal `CliConfiguration` produces root properties `schema`, `application`, `endpoints`, `commands`, `workflows`, and `extensions`, and that `CliExitCode.Success == 0`, `Usage == 2`, `Configuration == 3`, `Transport == 4`, `Protocol == 5`, `RuntimeFailure == 6`, and `Cancelled == 130`.

- [ ] **Step 2: Run the scenario and verify it fails before the types exist**

Run: `dotnet build Iwesun.Runtime.FunctionalTests/Iwesun.Runtime.FunctionalTests.csproj -c Debug --no-restore -p:UseSharedCompilation=false`

Expected: FAIL with missing v3 contract types.

- [ ] **Step 3: Implement the contracts**

Define these stable shapes:

```csharp
public sealed class CliConfiguration
{
    public string Schema { get; init; } = "";
    public CliApplicationDescriptor Application { get; init; } = new();
    public Dictionary<string, CliEndpointDescriptor> Endpoints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<CommandDescriptor> Commands { get; init; } = [];
    public List<WorkflowDescriptor> Workflows { get; init; } = [];
    public CliExtensionsDescriptor Extensions { get; init; } = new();
}
```

Use `JsonElement?` for typed defaults and constraints, ordered `List<ParameterDescriptor>` for parameters, and explicit request fields `Category`, `Operation`, `Domain`, `Target`, `Action`.

- [ ] **Step 4: Run the focused scenario**

Run: `dotnet Iwesun.Runtime.FunctionalTests/bin/Debug/net10.0/Iwesun.Runtime.FunctionalTests.dll --child --scenario cli-v3-contracts`

Expected: JSON result with `Success:true`.

- [ ] **Step 5: Commit contracts**

```powershell
git add Iwesun.Runtime.Cli Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: define CLI v3 contracts"
```

### Task 2: Implement strict loading and catalog validation

**Files:**
- Create: `Iwesun.Runtime.Cli/Configuration/CliConfigurationLoader.cs`
- Create: `Iwesun.Runtime.Cli/Configuration/CliConfigurationValidator.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add failing validation cases**

Extend `cli-v3-contracts` to reject: missing schema, schema other than `iwesun.runtime.cli/3.0`, extension data containing `version`, `baseCommands`, or `compositeCommands`, duplicate command names, alias collisions, duplicate parameter positions, missing endpoint references, missing request route fields, workflow duplicate step IDs, and workflow steps that provide both/neither `command` and `request`.

- [ ] **Step 2: Verify validation tests fail**

Run the focused scenario and expect at least one failed check named `v3-schema-rejected` or `v3-alias-collision-rejected`.

- [ ] **Step 3: Implement loader and validator**

Use `JsonSerializerOptions(JsonSerializerDefaults.Web)` with `UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow`. Return typed `CliFailureException` values with codes beginning `CLI_CONFIG_`; do not migrate old JSON.

Validate names with `^[a-z0-9-]+(?:\.[a-z0-9-]+)*$`, require named-pipe endpoints, and validate defaults/enums through the declared parameter type.

- [ ] **Step 4: Verify all strict-validation checks pass**

Run: `dotnet Iwesun.Runtime.FunctionalTests/bin/Debug/net10.0/Iwesun.Runtime.FunctionalTests.dll --child --scenario cli-v3-contracts`

Expected: all schema and catalog checks pass.

- [ ] **Step 5: Commit strict configuration loading**

```powershell
git add Iwesun.Runtime.Cli/Configuration Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: validate CLI v3 configuration"
```

### Task 3: Implement user add/extend/replace/disable merging

**Files:**
- Create: `Iwesun.Runtime.Cli/Configuration/CliConfigurationMerger.cs`
- Modify: `Iwesun.Runtime.Cli/Configuration/CliConfiguration.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add merge behavior checks**

Build an in-memory base catalog and user catalog. Assert `add` creates a command, `extend` adds an alias and changes only allowed defaults/endpoint settings, `replace` changes the full request route, and `disable` removes a name and all aliases from lookup. Assert `extend` attempting to change target/action fails with `CLI_CONFIG_EXTENSION_ROUTE_FORBIDDEN`.

- [ ] **Step 2: Run and verify the merge checks fail**

Expected: focused scenario fails because `CliConfigurationMerger` does not exist.

- [ ] **Step 3: Implement deterministic merge rules**

Merge built-in first, user second. Require explicit operation objects under `extensions`; clone descriptors instead of mutating loaded base instances; run full validation after merge.

- [ ] **Step 4: Run merge checks**

Expected: add, extend, replace, disable, forbidden-route, and post-merge-collision checks pass.

- [ ] **Step 5: Commit the merger**

```powershell
git add Iwesun.Runtime.Cli/Configuration Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: merge CLI v3 user extensions"
```

### Task 4: Implement strict invocation syntax and parameter binding

**Files:**
- Create: `Iwesun.Runtime.Cli/Syntax/CliTokenizer.cs`
- Create: `Iwesun.Runtime.Cli/Syntax/CliInvocationParser.cs`
- Create: `Iwesun.Runtime.Cli/Semantics/SemanticCommand.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add syntax checks**

Assert these forms bind identically:

```text
pipe.acquire runtime.worker
pipe.acquire -requestedPipeName runtime.worker
pipe.acquire -requestedPipeName:runtime.worker
```

Also assert duplicate positional/named values, missing bool values, unknown arguments, invalid JSON, range failures, enum failures, and string-length failures produce stable `CLI_PARAMETER_*` codes.

- [ ] **Step 2: Verify checks fail before parser implementation**

- [ ] **Step 3: Implement tokenizer and binder**

Tokenize quoted values without interpreting PowerShell expressions. Treat single-dash tokens as named parameters. Do not support double-dash command parameters, implicit boolean true, key=value syntax, flags, namespace/verb guessing, or unknown-value type inference.

Bind values only through the descriptor type: `bool`, `int32`, `int64`, `double`, `json`, or `string`.

- [ ] **Step 4: Run the focused scenario**

Expected: all valid forms produce the same typed `SemanticCommand`; all invalid forms return the expected code.

- [ ] **Step 5: Commit syntax and semantics**

```powershell
git add Iwesun.Runtime.Cli/Syntax Iwesun.Runtime.Cli/Semantics Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: parse CLI v3 invocations"
```

### Task 5: Compile commands and workflows into Runtime frames

**Files:**
- Create: `Iwesun.Runtime.Cli/Compilation/CommandCompiler.cs`
- Create: `Iwesun.Runtime.Cli/Compilation/WorkflowCompiler.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add frame compilation checks**

Assert a single command preserves configured category, operation, domain, target, action, and typed args in `rtdiag/2.0`. Assert aliases compile identically except for presentation metadata.

Assert a workflow compiles into one `rtdiag/3.0` frame with stable step IDs, `$input` values, `$step` bindings, `when`, `continueOnError`, `delayMs`, `stopOnError`, and `deadlineMs`.

- [ ] **Step 2: Verify compilation checks fail**

- [ ] **Step 3: Implement command compiler**

Construct `RuntimeDiagnosticFrameHeader` directly from descriptor fields; never infer category/domain from action text.

- [ ] **Step 4: Implement workflow compiler**

Resolve `step.command` only as an exact catalog reference with no whitespace. Convert `$input` to typed args and `$step` to `RuntimeDiagnosticBatchBinding`; reject forward references and cycles during validation.

- [ ] **Step 5: Run focused compilation checks and commit**

```powershell
git add Iwesun.Runtime.Cli/Compilation Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: compile CLI v3 Runtime frames"
```

### Task 6: Isolate named-pipe transport and presentation

**Files:**
- Create: `Iwesun.Runtime.Cli/Transport/IRuntimeTransport.cs`
- Create: `Iwesun.Runtime.Cli/Transport/NamedPipeRuntimeTransport.cs`
- Create: `Iwesun.Runtime.Cli/Presentation/CliRenderer.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add transport boundary checks**

Use a test pipe server that fragments the 4-byte header and payload. Assert the client reads the full frame. Add connect timeout, request timeout, zero/negative length, oversized response, truncated response, and cancellation cases with `CLI_TRANSPORT_*` or `CLI_PROTOCOL_*` codes.

- [ ] **Step 2: Verify transport checks fail**

- [ ] **Step 3: Implement named-pipe transport**

Write 4-byte little-endian length and UTF-8 JSON. Implement an exact-read loop. Enforce endpoint connect timeout, request timeout, and maximum response bytes.

- [ ] **Step 4: Implement result rendering**

Default success output is the complete Runtime response frame. Local failures serialize `iwesun.runtime.cli.result/1.0` with `ok`, `code`, `message`, `command`, and typed details. Map failures to the specified exit codes.

- [ ] **Step 5: Run transport checks and commit**

```powershell
git add Iwesun.Runtime.Cli/Transport Iwesun.Runtime.Cli/Presentation Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: add CLI v3 transport and results"
```

### Task 7: Compose the v3 application and bootstrap options

**Files:**
- Create: `Iwesun.Runtime.Cli/Application/CliOptions.cs`
- Create: `Iwesun.Runtime.Cli/Application/CliApplication.cs`
- Replace: `Iwesun.Runtime.Cli/Program.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add application-level checks**

Assert `--help`, `--config=`, `--user-config=`, `--pipe=`, `--timeout-ms=`, and one command execute through `CliApplication`. Assert `--web-pipe` is rejected with exit code 2.

- [ ] **Step 2: Implement options**

Resolve built-in config from explicit path, executable directory, current directory, then embedded resource. Resolve user config from explicit path or ProgramData. `--pipe` overrides endpoint `diagnostics` without changing the catalog file.

- [ ] **Step 3: Implement application orchestration**

Load, merge, validate, parse, compile, send once, render, and return a stable exit code. Help must be generated from commands/workflows and parameters in the merged catalog.

- [ ] **Step 4: Reduce Program.cs to composition**

```csharp
using Iwesun.Runtime.Cli.Application;

return await CliApplication.RunAsync(args, CancellationToken.None);
```

- [ ] **Step 5: Run application checks and commit**

```powershell
git add Iwesun.Runtime.Cli/Application Iwesun.Runtime.Cli/Program.cs Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "refactor: compose CLI v3 application"
```

### Task 8: Build the authoritative v3 JSON catalog

**Files:**
- Create: `Iwesun.Runtime.Cli/Iwesun.Runtime.Cli.commands.json`
- Create: `Iwesun.Runtime.Cli/Iwesun.Runtime.Cli.user.example.json`
- Modify: `Iwesun.Runtime.Cli/Iwesun.Runtime.Cli.csproj`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add a catalog coverage check**

Load the real v3 JSON and assert every required domain exists: host, lifecycle, switchboard, breakpoint, hook, registry, process, thread, task, pipe, file, reflection, and web. Assert `diagnostics.focus` and `diagnostics.quiet` workflows are structured and every command target/action matches a registered Runtime target.

- [ ] **Step 2: Create the v3 built-in catalog**

Translate current functional capabilities to canonical names such as `switchboard.get`, `breakpoint.list`, `pipe.acquire`, and `web.snapshot`. Keep `aliases` empty by default except deliberate new v3 aliases. Define all category/operation/domain/target/action fields explicitly.

- [ ] **Step 3: Create the user example**

Demonstrate one alias extension, one custom command, and one structured workflow without CLI command strings.

- [ ] **Step 4: Update project content**

Copy both files to output/publish and embed only the built-in catalog.

- [ ] **Step 5: Run catalog coverage and commit**

```powershell
git add Iwesun.Runtime.Cli Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: add authoritative CLI v3 catalog"
```

### Task 9: Delete all v2 CLI implementation and configuration

**Files:**
- Delete: `Iwesun.Runtime.Cli/CommandParser.cs`
- Delete: `Iwesun.Runtime.Cli/CompositeCommandExecutor.cs`
- Delete: `Iwesun.Runtime.Cli/ProtocolBoundary.cs`
- Delete: `Iwesun.Runtime.Cli/Iwesun.Runtime.Cli.commands.v2.json`
- Delete: `Iwesun.Runtime.Cli/Iwesun.Runtime.Cli.user.v2.json`
- Delete: `Iwesun.Runtime.Cli/Iwesun.Runtime.Cli.user.v2.example.json`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/SampleHostCliFullScenario.cs`

- [ ] **Step 1: Migrate all functional CLI command strings to v3 names**

Replace old `sw.*`, `bp.*`, `reg.*`, and v2 config paths with canonical v3 names and new files. Preserve all existing assertions for breakpoint disconnect stability, explicit resume, quiet restoration, and safe shutdown.

- [ ] **Step 2: Delete legacy files**

Use file edits to delete the six v2 source/configuration files listed above.

- [ ] **Step 3: Scan active source and configuration**

Run: `rg -n "commands\.v2|user\.v2|baseCommands|compositeCommands|ParsedCommand|CompositeCommandDef|sw\.|bp\.|reg\." Iwesun.Runtime.Cli Iwesun.Runtime.FunctionalTests -g '*.cs' -g '*.json'`

Expected: no active v2 implementation or command references.

- [ ] **Step 4: Run CLI and SampleHost scenarios**

Run focused scenarios `cli`, `cli-numeric-breakpoint`, `bp-process-cli`, and `sample-host-cli-full`.

Expected: all succeed with v3 catalog and commands.

- [ ] **Step 5: Commit legacy deletion**

```powershell
git add -A Iwesun.Runtime.Cli Iwesun.Runtime.FunctionalTests
git commit -m "refactor: remove CLI v2"
```

### Task 10: Rewrite CLI documentation and complete verification

**Files:**
- Rewrite: `docs/IWESUN_RUNTIME_CLI.md`
- Modify: `docs/IWESUN_RUNTIME_USER_GUIDE.md`
- Modify: `docs/IWESUN_RUNTIME_DESIGN.md`
- Modify: `docs/UNIFIED_INTERFACE.md`
- Modify: `docs/REQUIREMENTS_ACTIVE.md`

- [ ] **Step 1: Generate the command reference from the v3 catalog**

Document endpoints, commands, workflows, extension operations, syntax, errors, and exit codes. Remove the v2 historical command body from active CLI documentation.

- [ ] **Step 2: Remove migration labels after implementation**

Update the user guide and design documents so CLI v3 is CURRENT, not migration-only.

- [ ] **Step 3: Scan the active repository for v2 traces**

Run: `rg -n "commands\.v2|user\.v2|baseCommands|compositeCommands|sw\.|bp\.|reg\." Iwesun.Runtime.Cli Iwesun.Runtime.FunctionalTests docs -g '*.cs' -g '*.json' -g '*.md' -g '!docs/archive/**' -g '!docs/superpowers/**'`

Expected: no active v2 references.

- [ ] **Step 4: Run full verification**

Run Debug and Release solution builds, then the full functional-test executable.

Expected: 0 build errors and all functional scenarios successful.

- [ ] **Step 5: Commit CLI v3 completion**

```powershell
git add docs Iwesun.Runtime.Cli Iwesun.Runtime.FunctionalTests
git commit -m "docs: publish CLI v3 reference"
```
