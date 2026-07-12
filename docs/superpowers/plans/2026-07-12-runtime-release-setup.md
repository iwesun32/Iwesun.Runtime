# Runtime Release and Setup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a validated Runtime staging tree, portable ZIP, SHA-256 manifest, and Windows x64 MSI containing binaries, CLI v3 JSON, authoritative documentation, skills, and source samples.

**Architecture:** `Iwesun.Runtime.Release` owns deterministic staging and validation. `Iwesun.Runtime.Setup` consumes only the validated staging tree through WiX Toolset SDK 7.0.0, installs immutable content to Program Files, initializes mutable data in ProgramData, and manages PATH and major upgrades.

**Tech Stack:** MSBuild/.NET 10, PowerShell verification, WiX Toolset SDK 7.0.0, Windows Installer, framework-dependent win-x64 publish.

---

### Task 1: Add release manifest and source index

**Files:**
- Create: `Iwesun.Runtime.Release/RuntimeReleaseManifest.json`
- Create: `Iwesun.Runtime.Release/API-SOURCE-INDEX.md`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add a `release-manifest` functional scenario**

Assert the manifest declares required projects, configs, docs, skill files, Runtime source files, SampleHost source files, forbidden v2 patterns, and output directories.

- [ ] **Step 2: Verify the scenario fails before the manifest exists**

- [ ] **Step 3: Create the manifest**

Use JSON arrays named `binaries`, `configs`, `docs`, `skills`, `runtimeSources`, `sampleHostSources`, and `forbiddenFiles`. Include exact relative paths from the approved release specification.

- [ ] **Step 4: Create API-SOURCE-INDEX.md**

Document that `RuntimeInjector` lives in `RuntimeHostTemplate.cs`; list RuntimeHostTemplate, RuntimeShutdownCoordinator, RuntimeStateContracts, and RManagedState with namespace and purpose.

- [ ] **Step 5: Run the focused scenario and commit**

```powershell
git add Iwesun.Runtime.Release Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: define Runtime release manifest"
```

### Task 2: Rebuild the staging project around CLI v3

**Files:**
- Rewrite: `Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj`
- Modify: `Iwesun.Runtime.slnx`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add a failing `release-staging` scenario**

Invoke `dotnet msbuild Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj -t:PublishRuntimeRelease -p:Configuration=Release -p:RuntimeIdentifier=win-x64`. Assert the complete directory layout and all manifest files exist.

- [ ] **Step 2: Define staging roots**

Use `artifacts/release/Iwesun.Runtime`, `artifacts/release/Iwesun.Runtime-$(Version)-win-x64.zip`, and `artifacts/checksums/SHA256SUMS.txt`.

- [ ] **Step 3: Publish five projects**

Publish CLI and SampleHost to separate `bin` directories with `RuntimeIdentifier=win-x64`, `SelfContained=false`, `UseAppHost=true`. Publish Diagnostics, Data, and WebView2 to separate `lib` directories with `SelfContained=false`.

After publishing CLI, copy `Iwesun.Runtime.Cli.exe` to `iwrt.exe` in the same staging directory. The copied apphost continues to launch `Iwesun.Runtime.Cli.dll`, while PATH exposes the stable `iwrt.exe` command without renaming the managed assembly.

- [ ] **Step 4: Collect configs, docs, skills, and source**

Copy only `Iwesun.Runtime.Cli.commands.json` and `Iwesun.Runtime.Cli.user.example.json`; approved docs; `skills/iwesun-runtime-integration/**`; Runtime integration source; complete SampleHost source/templates; and install verification scripts.

- [ ] **Step 5: Run the staging scenario and commit**

```powershell
git add Iwesun.Runtime.Release Iwesun.Runtime.slnx Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: stage complete Runtime release"
```

### Task 3: Implement release validation, ZIP, and SHA-256 output

**Files:**
- Create: `scripts/release/verify-runtime-release.ps1`
- Create: `scripts/release/new-runtime-checksums.ps1`
- Modify: `Iwesun.Runtime.Release/Iwesun.Runtime.Release.csproj`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add failure-path checks**

Copy staging to a test directory, remove one required DLL, and assert verification fails. Restore it, add a `commands.v2.json`, and assert verification fails. Change schema from `iwesun.runtime.cli/3.0` and assert verification fails.

- [ ] **Step 2: Implement strict release verification**

Read `RuntimeReleaseManifest.json`; require all files; reject forbidden filenames and JSON roots; deserialize the CLI config and require exact v3 schema.

- [ ] **Step 3: Implement deterministic archive and checksums**

Sort relative file paths ordinally. Create the portable ZIP after verification. Generate SHA-256 lines as lowercase hash plus two spaces plus relative path for staging files, ZIP, and later MSI.

- [ ] **Step 4: Wire verification before packaging**

The MSBuild target must stop immediately when verification exits nonzero; ZIP/checksum targets run only afterward.

- [ ] **Step 5: Run positive and negative scenarios and commit**

```powershell
git add scripts/release Iwesun.Runtime.Release Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: validate and archive Runtime release"
```

### Task 4: Add the WiX 7 setup project

**Files:**
- Create: `Iwesun.Runtime.Setup/Iwesun.Runtime.Setup.wixproj`
- Create: `Iwesun.Runtime.Setup/Package.wxs`
- Create: `Iwesun.Runtime.Setup/Folders.wxs`
- Create: `Iwesun.Runtime.Setup/Features.wxs`
- Create: `Iwesun.Runtime.Setup/RuntimePrerequisite.wxs`
- Modify: `Iwesun.Runtime.slnx`

- [ ] **Step 1: Add a setup build check**

Assert `dotnet build Iwesun.Runtime.Setup/Iwesun.Runtime.Setup.wixproj -c Release -p:Platform=x64` fails if validated staging is absent and succeeds after `PublishRuntimeRelease`.

- [ ] **Step 2: Create the WiX SDK project**

Use:

```xml
<Project Sdk="WixToolset.Sdk/7.0.0">
  <PropertyGroup>
    <Platform>x64</Platform>
    <InstallerPlatform>x64</InstallerPlatform>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="WixToolset.Netfx.wixext" Version="7.0.0" />
  </ItemGroup>
</Project>
```

Reference the validated staging root through one MSBuild property. Do not run dotnet publish from the setup project.

- [ ] **Step 3: Define package identity and upgrade**

Use one stable UpgradeCode committed in `Package.wxs`, per-machine scope, compressed media, major upgrade scheduling, manufacturer `Iwesun`, and version from the shared MSBuild `Version` property.

- [ ] **Step 4: Define installation folders and features**

Create `ProgramFiles64Folder/Iwesun/Runtime` with bin, lib, config, docs, skills, samples, source, scripts; create `CommonAppDataFolder/Iwesun/Runtime` with config, logs, runtime. Add the validated staging root as a named `BindPath` and use WiX 7 `Files Include="!(bindpath.RuntimeStaging)\**"` authoring rooted at the install directory, excluding no files because staging validation has already enforced the payload boundary.

- [ ] **Step 5: Add .NET 10 launch condition**

Use WiX NetFx extension detection for x64 .NET 10 Desktop/Runtime availability and block install with a clear message when absent. Verify the exact registry/property detection on the build host before committing.

- [ ] **Step 6: Commit Setup skeleton**

```powershell
git add Iwesun.Runtime.Setup Iwesun.Runtime.slnx
git commit -m "feat: add WiX Runtime setup project"
```

### Task 5: Install PATH and preserve ProgramData

**Files:**
- Create: `Iwesun.Runtime.Setup/Environment.wxs`
- Create: `Iwesun.Runtime.Setup/UserData.wxs`
- Modify: `Iwesun.Runtime.Setup/Features.wxs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add MSI authoring assertions**

Inspect generated MSI tables or WiX intermediate output and assert one system PATH environment row points to `[INSTALLFOLDER]bin\Iwesun.Runtime.Cli`, ProgramData folders are permanent, and user config creation is guarded by file absence.

- [ ] **Step 2: Add PATH component**

Use a dedicated permanent-safe Environment row with action `set`, part `last`, system scope, and the CLI install directory. Ensure uninstall removes only this authored value.

- [ ] **Step 3: Add ProgramData components**

Create logs/runtime directories and initialize `Iwesun.Runtime.Cli.user.json` only when absent. Mark user-data components permanent/never-overwrite so major upgrades and default uninstall preserve them.

- [ ] **Step 4: Build and inspect MSI**

Expected: MSI builds and assertions confirm PATH and ProgramData policies.

- [ ] **Step 5: Commit installation behavior**

```powershell
git add Iwesun.Runtime.Setup Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "feat: configure Runtime install state"
```

### Task 6: Add installed-product verification

**Files:**
- Rewrite: `scripts/release/verify-runtime-install.ps1`
- Create: `scripts/release/test-runtime-setup.ps1`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Implement read-only installed-tree verification**

Require installed binaries, v3 configs, docs, skill, source, SampleHost, and PATH. Run `iwrt --help`; launch SampleHost with a unique diagnostics pipe; query host status; request safe shutdown; require exit code 0.

- [ ] **Step 2: Implement elevated setup lifecycle test**

Install MSI silently with logging, write a unique marker into ProgramData user config, install a higher test version, assert the marker remains, uninstall, assert Program Files and PATH are removed, and assert ProgramData remains.

- [ ] **Step 3: Add non-elevated guard**

If the process is not elevated, the lifecycle script exits with a distinct skipped code and message; it must never partially install.

- [ ] **Step 4: Run read-only verification against staging**

Expected: tree, CLI help, SampleHost query, and safe shutdown checks succeed before MSI lifecycle testing.

- [ ] **Step 5: Commit setup verification**

```powershell
git add scripts/release Iwesun.Runtime.FunctionalTests/Program.cs
git commit -m "test: verify Runtime setup lifecycle"
```

### Task 7: Complete documentation and full release verification

**Files:**
- Rewrite: `docs/05-runtime-tooling/RUNTIME_RELEASE_PACKAGING.md`
- Modify: `docs/IWESUN_RUNTIME_USER_GUIDE.md`
- Modify: `docs/README.md`
- Modify: `docs/REQUIREMENTS_ACTIVE.md`

- [ ] **Step 1: Document build commands and outputs**

Document the exact Release target, Setup build command, output paths, .NET 10 prerequisite, install layout, PATH behavior, ProgramData preservation, ZIP use, checksum verification, silent install, upgrade, uninstall, and manual user-data cleanup.

- [ ] **Step 2: Link setup documentation from the user guide and index**

- [ ] **Step 3: Run full build and functional tests**

Run Release x64 solution build, complete functional-test executable, Release staging target, Setup build, release verifier, and checksum verifier.

Expected: all commands exit 0; all functional scenarios pass; staging contains no v2 JSON.

- [ ] **Step 4: Run elevated MSI lifecycle test when authority is available**

Expected: install, PATH, CLI help, SampleHost query/shutdown, upgrade preservation, uninstall cleanup, and ProgramData retention all pass. If elevation is unavailable, report this single unexecuted environmental check explicitly; do not claim MSI lifecycle completion.

- [ ] **Step 5: Commit completed release system**

```powershell
git add Iwesun.Runtime.Release Iwesun.Runtime.Setup scripts/release docs Iwesun.Runtime.FunctionalTests Iwesun.Runtime.slnx
git commit -m "docs: publish Runtime setup workflow"
```
