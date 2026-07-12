[CmdletBinding()]
param(
    [string]$InstallRoot = "$env:ProgramFiles\Iwesun Runtime",
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

Write-Host "Runtime install verification started."
Write-Host "InstallRoot: $InstallRoot"
Write-Host "DataRoot:    $DataRoot"

# Program Files payload
Assert-PathExists -Path (Join-Path $InstallRoot "bin\Iwesun.Runtime.Cli\Iwesun.Runtime.Cli.exe") -Label "CLI executable"
Assert-PathExists -Path (Join-Path $InstallRoot "bin\Iwesun.Runtime.Cli\Iwesun.Runtime.Cli.runtimeconfig.json") -Label "CLI runtime config"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.Diagnostics\Iwesun.Runtime.Diagnostics.dll") -Label "Diagnostics library"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.Data\Iwesun.Runtime.Data.dll") -Label "Data library"
Assert-PathExists -Path (Join-Path $InstallRoot "lib\Iwesun.Runtime.WebView2\Iwesun.Runtime.WebView2.dll") -Label "WebView2 interface library"

# ProgramData payload
Assert-PathExists -Path (Join-Path $DataRoot "config\Iwesun.Runtime.Cli.commands.json") -Label "CLI v3 command config"
Assert-PathExists -Path (Join-Path $DataRoot "docs\IWESUN_RUNTIME_CLI.md") -Label "CLI documentation"
Assert-PathExists -Path (Join-Path $DataRoot "samples\templates\RuntimeHost.Startup.Template.cs.txt") -Label "Startup template"
Assert-PathExists -Path (Join-Path $DataRoot "scripts\verify-runtime-install.ps1") -Label "Self-check script"

$cliExe = Join-Path $InstallRoot "bin\Iwesun.Runtime.Cli\Iwesun.Runtime.Cli.exe"
$helpOutput = & $cliExe --help 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "CLI help command failed with exit code $LASTEXITCODE.`n$helpOutput"
}

Write-Host "[OK] CLI --help returned successfully."
Write-Host "Runtime install verification completed successfully."
