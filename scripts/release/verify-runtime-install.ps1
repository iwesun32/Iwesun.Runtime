[CmdletBinding()]
param(
    [string]$InstallRoot = "$env:ProgramFiles\Iwesun\Runtime",
    [string]$DataRoot = "$env:ProgramData\Iwesun\Runtime"
)

$ErrorActionPreference = "Stop"
$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$DataRoot = [System.IO.Path]::GetFullPath($DataRoot)

function Assert-PathExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Missing [$Label]: $Path"
    }

    Write-Host "[OK] $Label => $Path"
}

function Assert-FileVersionEquals {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ReferencePath,
        [Parameter(Mandatory = $true)]
        [string]$CandidatePath,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    $referenceVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ReferencePath)
    $candidateVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($CandidatePath)
    $referenceVersion = $referenceVersionInfo.FileVersion
    $referenceProductVersion = $referenceVersionInfo.ProductVersion
    $candidateVersion = $candidateVersionInfo.FileVersion
    $candidateProductVersion = $candidateVersionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($referenceVersion) -or $candidateVersion -ne $referenceVersion) {
        throw "Version mismatch [$Label]: expected $referenceVersion, got $candidateVersion at $CandidatePath"
    }
    if ([string]::IsNullOrWhiteSpace($referenceProductVersion) -or $candidateProductVersion -ne $referenceProductVersion) {
        throw "ProductVersion mismatch [$Label]: expected $referenceProductVersion, got $candidateProductVersion at $CandidatePath"
    }

    Write-Host "[OK] $Label version => $candidateVersion / $candidateProductVersion"
}

function Assert-PathAbsent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (Test-Path -LiteralPath $Path) {
        throw "Unexpected [$Label]: $Path"
    }

    Write-Host "[OK] $Label is absent"
}

Write-Host "Runtime install verification started."
Write-Host "InstallRoot: $InstallRoot"
Write-Host "DataRoot:    $DataRoot"

# Program Files payload
Assert-PathExists -Path (Join-Path $InstallRoot "bin\Iwesun.Runtime.Cli\Iwesun.Runtime.Cli.exe") -Label "CLI executable"
Assert-PathExists -Path (Join-Path $InstallRoot "bin\Iwesun.Runtime.Cli\Iwesun.Runtime.Cli.runtimeconfig.json") -Label "CLI runtime config"
Assert-PathExists -Path (Join-Path $InstallRoot "bin\Iwesun.Runtime.RemoteConsole\Iwesun.Runtime.RemoteConsole.exe") -Label "RemoteConsole service executable"
Assert-PathExists -Path (Join-Path $InstallRoot "bin\Iwesun.Runtime.RemoteConsole\Iwesun.Runtime.RemoteConsole.runtimeconfig.json") -Label "RemoteConsole runtime config"
Assert-PathExists -Path (Join-Path $InstallRoot "bin\Iwesun.Runtime.RemoteConsole\Iwesun.Runtime.RemoteConsole.Protocol.dll") -Label "RemoteConsole protocol library"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.Diagnostics\Iwesun.Runtime.Diagnostics.dll") -Label "Diagnostics library"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.Diagnostics\Debug\Iwesun.Runtime.Diagnostics.dll") -Label "Diagnostics Debug library"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.Diagnostics\Release\Iwesun.Runtime.Diagnostics.dll") -Label "Diagnostics Release library"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.Diagnostics\Microsoft.Extensions.Hosting.WindowsServices.dll") -Label "Windows Service runtime dependency"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.Diagnostics\Debug\Microsoft.Extensions.Hosting.WindowsServices.dll") -Label "Windows Service Debug dependency"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.Diagnostics\Release\Microsoft.Extensions.Hosting.WindowsServices.dll") -Label "Windows Service Release dependency"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.Data\Iwesun.Runtime.Data.dll") -Label "Runtime Data and RecordStore V2 library"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Networks\Iwesun.Networks.dll") -Label "Networks library"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.WebView2\Iwesun.Runtime.WebView2.dll") -Label "WebView2 interface library"
$diagnosticsDll = Join-Path $InstallRoot "lib\Iwesun.Runtime.Diagnostics\Iwesun.Runtime.Diagnostics.dll"
$recordStoreDll = Join-Path $InstallRoot "lib\Iwesun.Runtime.Data\Iwesun.Runtime.Data.dll"
$networksDll = Join-Path $InstallRoot "lib\Iwesun.Networks\Iwesun.Networks.dll"
$webView2Dll = Join-Path $InstallRoot "lib\Iwesun.Runtime.WebView2\Iwesun.Runtime.WebView2.dll"
$remoteConsoleExe = Join-Path $InstallRoot "bin\Iwesun.Runtime.RemoteConsole\Iwesun.Runtime.RemoteConsole.exe"
$remoteConsoleProtocolDll = Join-Path $InstallRoot "bin\Iwesun.Runtime.RemoteConsole\Iwesun.Runtime.RemoteConsole.Protocol.dll"
Assert-FileVersionEquals -ReferencePath $diagnosticsDll -CandidatePath $webView2Dll -Label "WebView2 synchronized release"
$diagnosticsFileVersion = (Get-Item -LiteralPath $diagnosticsDll).VersionInfo.FileVersion
$recordStoreFileVersion = (Get-Item -LiteralPath $recordStoreDll).VersionInfo.FileVersion
if ($diagnosticsFileVersion -ne $recordStoreFileVersion) {
	throw "FileVersion mismatch [RecordStore synchronized release]: expected $diagnosticsFileVersion, got $recordStoreFileVersion at $recordStoreDll"
}
Write-Host "[OK] RecordStore synchronized FileVersion => $recordStoreFileVersion"
$networksVersionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($networksDll)
if ($networksVersionInfo.FileVersion -ne "1.2.0.0") {
    throw "FileVersion mismatch [Networks release]: expected 1.2.0.0, got $($networksVersionInfo.FileVersion) at $networksDll"
}
Write-Host "[OK] Networks release FileVersion => $($networksVersionInfo.FileVersion)"
$recordStoreHash = (Get-FileHash -LiteralPath $recordStoreDll -Algorithm SHA256).Hash
$legacyDataCopies = @(Get-ChildItem -LiteralPath $InstallRoot -Recurse -Filter "Iwesun.Data.dll" -File)
if ($legacyDataCopies.Count -ne 0) {
    throw "Legacy Iwesun.Data.dll must not be present in the Runtime payload: $($legacyDataCopies.FullName -join ', ')"
}
Write-Host "[OK] Legacy Iwesun.Data.dll is absent"
$recordStoreCopies = @(Get-ChildItem -LiteralPath $InstallRoot -Recurse -Filter "Iwesun.Runtime.Data.dll" -File)
foreach ($copy in $recordStoreCopies) {
    $copyHash = (Get-FileHash -LiteralPath $copy.FullName -Algorithm SHA256).Hash
    if ($copyHash -ne $recordStoreHash) {
        throw "RecordStore payload contains a stale or mismatched copy: $($copy.FullName)"
    }
}
Write-Host "[OK] RecordStore payload copies synchronized => $($recordStoreCopies.Count)"
Assert-FileVersionEquals -ReferencePath $diagnosticsDll -CandidatePath $remoteConsoleExe -Label "RemoteConsole synchronized release"
Assert-FileVersionEquals -ReferencePath $diagnosticsDll -CandidatePath $remoteConsoleProtocolDll -Label "RemoteConsole Protocol synchronized release"

# Program Files documentation and integration payload
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_USER_GUIDE.md") -Label "User guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_QUICK_START.md") -Label "Quick start guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\RELEASE_NOTES.md") -Label "Release notes"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_CLI.md") -Label "CLI guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_WINDOWS_SERVICE.md") -Label "Windows Service guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_REMOTE_ACCESS.md") -Label "Remote access guide"
$remoteConsoleGuidePath = Join-Path $InstallRoot "docs\IWESUN_RUNTIME_REMOTE_CONSOLE.md"
Assert-PathExists -Path $remoteConsoleGuidePath -Label "RemoteConsole service guide"
$remoteConsoleGuide = Get-Content -LiteralPath $remoteConsoleGuidePath -Raw
foreach ($requiredText in @("file.upload", "file.download", "Copy-Item")) {
    if ($remoteConsoleGuide.IndexOf($requiredText, [StringComparison]::Ordinal) -lt 0) {
        throw "RemoteConsole service guide does not document required capability boundary: $requiredText"
    }
}
Write-Host "[OK] RemoteConsole file capability boundary documented"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\RUNTIME_ROOT_DATA_STRUCTURE.md") -Label "Runtime root data structure guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\DATA_PROJECT_RUNTIME_ROOT.md") -Label "Data project technical guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\02-api\RECORD_STORE_V2_PUBLIC_API.md") -Label "RecordStore V2 API guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\01-design\RECORD_STORE_DESIGN_V2.md") -Label "RecordStore V2 design"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\01-design\RECORD_STORE_V2_EMPTY_BUCKET_LIFECYCLE_REVIEW_2026-07-18.md") -Label "RecordStore V2 empty bucket lifecycle review"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\RUNTIME_DATA_RELEASE_NOTES.md") -Label "Runtime Data release notes"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\RELEASE_STATUS.md") -Label "Runtime Data release status"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\03-reference\RECORD_STORE_V2_COMPLETE_SAFETY_AND_PERFORMANCE_REPORT_2026-07-18.md") -Label "RecordStore V2 verification report"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\README.md") -Label "Runtime Data documentation index"
Assert-PathAbsent -Path (Join-Path $InstallRoot "docs\RECORD_STORE_MIGRATION_PLAN.md") -Label "retired Runtime migration plan"
Assert-PathAbsent -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\02-api\DLIST_TO_RECORD_STORE_V2_MIGRATION.md") -Label "retired DList migration guide"
Assert-PathAbsent -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\02-api\RECORD_STORE_1_0_25_TO_V2_MIGRATION.md") -Label "retired RecordStore V1 migration guide"
Assert-PathAbsent -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\01-design\RECORD_STORE_V2_COMPATIBILITY_AUDIT.md") -Label "retired compatibility audit"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Networks\README.md") -Label "Iwesun.Networks documentation index"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Networks\docs\RELEASE_STATUS.md") -Label "Iwesun.Networks release status"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Networks\docs\03-reference\IWESUN_NETWORKS_1_2_0_RELEASE_NOTES.md") -Label "Iwesun.Networks 1.2.0 release notes"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Networks\docs\02-endpoints\TRACKED_REQUEST_REPLY.md") -Label "Iwesun.Networks tracked endpoint guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\RUNTIME_DATA_RELEASE_NOTES.md") -Label "Runtime Data preserved release notes"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\Iwesun.Runtime.Data\docs\02-api\RECORD_STORE_V2_PUBLIC_API.md") -Label "Runtime Data preserved V2 API"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEBVIEW2_JSON_PIPE_CLI_PLAN.md") -Label "WebView2 pipe plan"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEB_RUNTIME_CONTROL.md") -Label "WebView2 control guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEBVIEW2_RUNTIME_CAPABILITIES.md") -Label "WebView2 capability status"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\SCRIPT_REFLECTION_PLAN.md") -Label "WebView2 script and reflection guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEBVIEW2_RELEASE_STATUS.md") -Label "WebView2 release status"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\DOM_SNAPSHOT_API.md") -Label "WebView2 DOM snapshot API guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\DATA_STREAM_MONITOR_RECORDER.md") -Label "WebView2 data stream recorder guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\FULL_PAGE_EVIDENCE_API.md") -Label "WebView2 full page evidence API guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEBVIEW2_1.0.30_EVIDENCE_RELEASE.md") -Label "WebView2 1.0.30 evidence release notes"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEBVIEW2_1.0.26_UPGRADE.md") -Label "WebView2 1.0.26 upgrade guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEBVIEW2_SAMPLE_HOST.md") -Label "WebView2 sample host guide"
$publishedDocs = @(Get-ChildItem -LiteralPath (Join-Path $InstallRoot "docs") -Filter "*.md" -File)
if ($publishedDocs.Count -lt 12) {
    throw "Published documentation set is incomplete. Expected at least 12 Markdown files, found $($publishedDocs.Count)."
}
Write-Host "[OK] Published documentation set => $($publishedDocs.Count) Markdown files"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\templates\RuntimeCreation.Replacements.Template.txt") -Label "Replacement quick list"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\source\Iwesun.Runtime.SampleHost\installed\Iwesun.Runtime.SampleHost.Installed.csproj") -Label "Installed SampleHost project"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\templates\RuntimeHost.Startup.Minimal.Template.cs.txt") -Label "Minimal startup template"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\templates\RuntimeHost.DiagnosticsExamples.Template.cs.txt") -Label "Diagnostics examples template"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\templates\RuntimeHost.ManagedWorker.Template.cs.txt") -Label "Managed Worker template"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\source\Iwesun.Runtime.SampleHost\Program.cs") -Label "SampleHost source"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\source\Iwesun.Runtime.WebView2.SampleHost\Iwesun.Runtime.WebView2.SampleHost.csproj") -Label "WebView2 SampleHost project source"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\source\Iwesun.Runtime.WebView2.SampleHost\MainWindow.xaml.cs") -Label "WebView2 SampleHost window source"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\source\Iwesun.Runtime.WebView2.SampleHost\DataStreamRecorderSample.cs") -Label "WebView2 data recorder sample source"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\source\Iwesun.Networks.Examples\Iwesun.Networks.Examples.csproj") -Label "Networks example project source"
Assert-PathExists -Path (Join-Path $InstallRoot "samples\source\Iwesun.Networks.Examples\Program.cs") -Label "Networks example source"
Assert-PathExists -Path (Join-Path $InstallRoot "bin\Iwesun.Runtime.WebView2.SampleHost\Iwesun.Runtime.WebView2.SampleHost.exe") -Label "Published WebView2 SampleHost"
Assert-PathExists -Path (Join-Path $InstallRoot "skills\iwesun-runtime-integration\SKILL.md") -Label "Integration skill"
Assert-PathExists -Path (Join-Path $InstallRoot "skills\iwesun-runtime-integration\references\webview2-runtime.md") -Label "WebView2 integration skill reference"
Assert-PathExists -Path (Join-Path $InstallRoot "skills\iwesun-runtime-integration\references\remote-console.md") -Label "RemoteConsole integration skill reference"
Assert-PathExists -Path (Join-Path $InstallRoot "skills\iwesun-runtime-integration\references\record-store.md") -Label "RecordStore integration skill reference"
Assert-PathExists -Path (Join-Path $InstallRoot "scripts\verify-runtime-install.ps1") -Label "Self-check script"

# ProgramData mutable payload
Assert-PathExists -Path (Join-Path $DataRoot "config\RuntimeCliSystemConfig.json") -Label "CLI v3 system config"
Assert-PathExists -Path (Join-Path $DataRoot "config\RuntimeCliSystemMetadata.json") -Label "CLI v3 system metadata"
$cliConfigPath = Join-Path $DataRoot "config\RuntimeCliSystemConfig.json"
$cliConfigText = Get-Content -LiteralPath $cliConfigPath -Raw
foreach ($requiredCommand in @("web.script.evaluate", "web.script.audit", "web.network.rule.add", "web.monitor.filter.add", "web.highlight", "web.data-recorder.create", "web.data-recorder.start", "web.data-recorder.status", "web.data-recorder.list", "web.data-recorder.update", "web.data-recorder.stop", "web.data-recorder.delete", "web.data-recorder.events")) {
    if ($cliConfigText -notmatch [regex]::Escape($requiredCommand)) {
        throw "CLI system config is missing required WebView2 command: $requiredCommand"
    }
}
Write-Host "[OK] WebView2 CLI command set"

$cliExe = Join-Path $InstallRoot "bin\Iwesun.Runtime.Cli\Iwesun.Runtime.Cli.exe"
$helpOutput = & $cliExe --help 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "CLI help command failed with exit code $LASTEXITCODE.`n$helpOutput"
}

Write-Host "[OK] CLI --help returned successfully."

$remoteCliConfigText = Get-Content -LiteralPath (Join-Path $DataRoot "config\RuntimeCliSystemConfig.json") -Raw
foreach ($requiredCommand in @("console.info", "console.submit", "console.follow", "console.approve", "workspace.create", "console.file.list", "file.upload")) {
    if ($remoteCliConfigText -notmatch [regex]::Escape($requiredCommand)) {
        throw "CLI system config is missing required RemoteConsole command: $requiredCommand"
    }
}
Write-Host "[OK] RemoteConsole CLI command set"

$installedSampleProject = Join-Path $InstallRoot "samples\source\Iwesun.Runtime.SampleHost\installed\Iwesun.Runtime.SampleHost.Installed.csproj"
$sampleProjectText = Get-Content -LiteralPath $installedSampleProject -Raw
if ($sampleProjectText -match "<ProjectReference\b") {
    throw "Installed SampleHost must not use Runtime source ProjectReference."
}
if ($sampleProjectText -notmatch "IwesunRuntimeRoot" -or $sampleProjectText -notmatch "DiagnosticsVariant") {
    throw "Installed SampleHost does not select Diagnostics from the installation root by build configuration."
}
Write-Host "[OK] Installed SampleHost reference boundary"

$sampleVerificationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("iwesun-runtime-sample-" + [Guid]::NewGuid().ToString("N"))
try {
    foreach ($configuration in @("Debug", "Release")) {
        $outputRoot = Join-Path $sampleVerificationRoot $configuration
        & dotnet build $installedSampleProject -c $configuration --nologo `
            "-p:IwesunRuntimeRoot=$InstallRoot" `
            "-p:BaseOutputPath=$(Join-Path $outputRoot 'bin\')" `
            "-p:BaseIntermediateOutputPath=$(Join-Path $outputRoot 'obj\')"
        if ($LASTEXITCODE -ne 0) {
            throw "Installed SampleHost $configuration build failed with exit code $LASTEXITCODE."
        }

        $outputDll = Get-ChildItem -LiteralPath (Join-Path $outputRoot "bin") -Recurse -Filter "Iwesun.Runtime.Diagnostics.dll" -File | Select-Object -First 1
        if ($null -eq $outputDll) {
            throw "Installed SampleHost $configuration output did not contain Iwesun.Runtime.Diagnostics.dll."
        }
        $installedDll = Join-Path $InstallRoot "lib\Iwesun.Runtime.Diagnostics\$configuration\Iwesun.Runtime.Diagnostics.dll"
        if ((Get-FileHash -LiteralPath $outputDll.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $installedDll -Algorithm SHA256).Hash) {
            throw "Installed SampleHost $configuration output DLL hash does not match the installed $configuration Diagnostics DLL."
        }
        Write-Host "[OK] Installed SampleHost $configuration DLL hash"

		$sampleDataDll = Get-ChildItem -LiteralPath (Join-Path $outputRoot "bin") -Recurse -Filter "Iwesun.Runtime.Data.dll" -File | Select-Object -First 1
		if ($null -eq $sampleDataDll) {
			throw "Installed SampleHost $configuration output did not contain Iwesun.Runtime.Data.dll."
		}
		$installedDataDll = Join-Path $InstallRoot "lib\Iwesun.Runtime.Data\Iwesun.Runtime.Data.dll"
		if ((Get-FileHash -LiteralPath $sampleDataDll.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $installedDataDll -Algorithm SHA256).Hash) {
			throw "Installed SampleHost $configuration output Iwesun.Runtime.Data.dll hash does not match the installed RecordStore library."
		}
		Write-Host "[OK] Installed SampleHost $configuration RecordStore DLL hash"
    }
}
finally {
    if (Test-Path -LiteralPath $sampleVerificationRoot) {
        Remove-Item -LiteralPath $sampleVerificationRoot -Recurse -Force
    }
}

Write-Host "Runtime install verification completed successfully."
