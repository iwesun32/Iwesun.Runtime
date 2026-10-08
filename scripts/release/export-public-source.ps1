[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceRoot,
    [Parameter(Mandatory = $true)][string]$DestinationRoot,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
$destination = [IO.Path]::GetFullPath($DestinationRoot).TrimEnd('\')
if ($source -eq $destination -or $destination.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an independent checkout, outside the development source.'
}
foreach ($root in @($source, $destination)) {
    if (-not (Test-Path -LiteralPath (Join-Path $root '.git'))) { throw "Git checkout required: $root" }
}
if ($Apply -and @(& git -C $destination status --porcelain).Count -ne 0) {
    throw 'The destination must be clean before importing a source snapshot.'
}

function Test-PublicPath([string]$Path) {
    if ($Path -match '(^|/)(bin|obj|artifacts|archive|ai-ignore|_ai_ignore|node_modules|packages|\.vs|\.codex|\.agents|TestResults)/') { return $false }
    if ($Path -match '\.(dll|exe|pdb|zip|msi|nupkg|snupkg|pfx|pem|key|p12|log|jsonl|db|cache|tmp)$') { return $false }
    if ($Path -match '(^|/)\.env($|\.)') { return $false }
    if ($Path -match '^modules/(Diagnostics|Data|Networks|WebView2|Cli|RemoteConsole|Packaging)/') { return $true }
    if ($Path -match '^docs/' -and $Path -notmatch '(^|/)(HANDOFF_|IWESUN_RUNTIME_PUBLIC_RELEASE_DESIGN|IWESUN_RUNTIME_SOURCE_LICENSE_GOVERNANCE_DRAFT)') { return $true }
    if ($Path -match '^(scripts/release|skills/iwesun-runtime-integration|\.github)/') { return $true }
    return $Path -in @('AGENTS.md','copilot-instructions.md','.gitignore','.copilotignore','.codexignore','Directory.Build.props','Directory.Build.targets','Iwesun.Runtime.slnx','global.json','NuGet.Config','nuget.config')
}

$paths = @(& git -C $source -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Source inventory failed.' }
$selected = @($paths | Sort-Object -Unique | Where-Object { (Test-PublicPath $_) -and (Test-Path -LiteralPath (Join-Path $source $_) -PathType Leaf) })
$selectedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($path in $selected) { [void]$selectedSet.Add($path) }
$existing = @(& git -C $destination -c core.quotepath=false ls-files)
if ($LASTEXITCODE -ne 0) { throw 'Destination inventory failed.' }
$removed = @($existing | Where-Object { -not $selectedSet.Contains($_) })

foreach ($path in @($selected) + @($removed)) {
    $resolved = [IO.Path]::GetFullPath((Join-Path $destination $path))
    if (-not $resolved.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path escaped destination.' }
    if ((Test-Path -LiteralPath $resolved) -and ((Get-Item -LiteralPath $resolved).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse point rejected: $path" }
}
if ($Apply) {
    foreach ($path in $selected) {
        $target = Join-Path $destination $path
        [void][IO.Directory]::CreateDirectory((Split-Path $target))
        Copy-Item -LiteralPath (Join-Path $source $path) -Destination $target
    }
    foreach ($path in $removed) {
        $target = Join-Path $destination $path
        if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target }
    }
}
[pscustomobject]@{
    Applied = [bool]$Apply
    SourceRoot = $source
    DestinationRoot = $destination
    CopiedFiles = $selected.Count
    RemovedTrackedFiles = $removed.Count
    TablesSelected = @($selected | Where-Object { $_ -like 'modules/Tables/*' }).Count
    WebSelected = @($selected | Where-Object { $_ -like 'modules/Web/*' }).Count
    Recovery = 'Development checkout remains unchanged; removed destination files remain in its Git history.'
} | Format-List
