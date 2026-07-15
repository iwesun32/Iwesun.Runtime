[CmdletBinding()]
param(
    [string]$InstallRoot = "$env:ProgramFiles\Iwesun\Runtime",
    [string]$DataRoot = "$env:ProgramData\Iwesun\Runtime"
)

$ErrorActionPreference = "Stop"

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
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.Data\Iwesun.Runtime.Data.dll") -Label "Data library"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.WebView2\Iwesun.Runtime.WebView2.dll") -Label "WebView2 interface library"
$diagnosticsDll = Join-Path $InstallRoot "lib\Iwesun.Runtime.Diagnostics\Iwesun.Runtime.Diagnostics.dll"
$webView2Dll = Join-Path $InstallRoot "lib\Iwesun.Runtime.WebView2\Iwesun.Runtime.WebView2.dll"
$remoteConsoleExe = Join-Path $InstallRoot "bin\Iwesun.Runtime.RemoteConsole\Iwesun.Runtime.RemoteConsole.exe"
$remoteConsoleProtocolDll = Join-Path $InstallRoot "bin\Iwesun.Runtime.RemoteConsole\Iwesun.Runtime.RemoteConsole.Protocol.dll"
Assert-FileVersionEquals -ReferencePath $diagnosticsDll -CandidatePath $webView2Dll -Label "WebView2 synchronized release"
Assert-FileVersionEquals -ReferencePath $diagnosticsDll -CandidatePath $remoteConsoleExe -Label "RemoteConsole synchronized release"
Assert-FileVersionEquals -ReferencePath $diagnosticsDll -CandidatePath $remoteConsoleProtocolDll -Label "RemoteConsole Protocol synchronized release"

# Program Files documentation and integration payload
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_USER_GUIDE.md") -Label "User guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_QUICK_START.md") -Label "Quick start guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_CLI.md") -Label "CLI guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_WINDOWS_SERVICE.md") -Label "Windows Service guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_REMOTE_ACCESS.md") -Label "Remote access guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\IWESUN_RUNTIME_REMOTE_CONSOLE.md") -Label "RemoteConsole service guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEBVIEW2_JSON_PIPE_CLI_PLAN.md") -Label "WebView2 pipe plan"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEB_RUNTIME_CONTROL.md") -Label "WebView2 control guide"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEBVIEW2_RUNTIME_CAPABILITIES.md") -Label "WebView2 capability status"
Assert-PathExists -Path (Join-Path $InstallRoot "docs\WEBVIEW2_RELEASE_STATUS.md") -Label "WebView2 release status"
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
Assert-PathExists -Path (Join-Path $InstallRoot "skills\iwesun-runtime-integration\SKILL.md") -Label "Integration skill"
Assert-PathExists -Path (Join-Path $InstallRoot "skills\iwesun-runtime-integration\references\webview2-runtime.md") -Label "WebView2 integration skill reference"
Assert-PathExists -Path (Join-Path $InstallRoot "skills\iwesun-runtime-integration\references\remote-console.md") -Label "RemoteConsole integration skill reference"
Assert-PathExists -Path (Join-Path $InstallRoot "scripts\verify-runtime-install.ps1") -Label "Self-check script"

# ProgramData mutable payload
Assert-PathExists -Path (Join-Path $DataRoot "config\RuntimeCliSystemConfig.json") -Label "CLI v3 system config"
Assert-PathExists -Path (Join-Path $DataRoot "config\RuntimeCliSystemMetadata.json") -Label "CLI v3 system metadata"

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
    }
}
finally {
    if (Test-Path -LiteralPath $sampleVerificationRoot) {
        Remove-Item -LiteralPath $sampleVerificationRoot -Recurse -Force
    }
}

Write-Host "Runtime install verification completed successfully."
