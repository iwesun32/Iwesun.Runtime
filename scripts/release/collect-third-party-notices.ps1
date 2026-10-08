[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$projects = @(
    'modules/Diagnostics/src/Iwesun.Runtime.Diagnostics/Iwesun.Runtime.Diagnostics.csproj',
    'modules/WebView2/src/Iwesun.Runtime.WebView2/Iwesun.Runtime.WebView2.csproj',
    'modules/Cli/src/Iwesun.Runtime.Cli/Iwesun.Runtime.Cli.csproj',
    'modules/RemoteConsole/src/Iwesun.Runtime.RemoteConsole/Iwesun.Runtime.RemoteConsole.csproj',
    'modules/WebView2/samples/Iwesun.Runtime.WebView2.SampleHost/Iwesun.Runtime.WebView2.SampleHost.csproj'
)
$packages = foreach ($project in $projects) {
    $raw = & dotnet list (Join-Path $root $project) package --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { throw "Inventory failed for $project" }
    $parsed = ($raw -join "`n") | ConvertFrom-Json
    $parsed.projects.frameworks | ForEach-Object { @($_.topLevelPackages) + @($_.transitivePackages) } | Where-Object { $_.id }
}
$unique = @($packages | Select-Object id,resolvedVersion | Sort-Object id,resolvedVersion -Unique)
$cacheLine = & dotnet nuget locals global-packages --list
if ($LASTEXITCODE -ne 0 -or $cacheLine -notmatch 'global-packages:\s*(.+)') { throw 'NuGet cache not found.' }
$cache = $Matches[1].Trim()
$legal = Join-Path $root 'legal/third-party'
[void][IO.Directory]::CreateDirectory($legal)
$rows = [Collections.Generic.List[string]]::new()
$rows.Add('# Redistributed NuGet dependencies')
$rows.Add('')
$rows.Add('| Package | Version | Publisher license |')
$rows.Add('| --- | --- | --- |')
foreach ($package in $unique) {
    $packageRoot = Join-Path $cache ($package.id.ToLowerInvariant() + '/' + $package.resolvedVersion)
    $nuspec = Join-Path $packageRoot ($package.id.ToLowerInvariant() + '.nuspec')
    $licenseLine = Get-Content -LiteralPath $nuspec -TotalCount 40 | Where-Object { $_ -match '<license ' } | Select-Object -First 1
    if ($licenseLine -match '<license type="expression">([^<]+)</license>') {
        $licenseName = $Matches[1]
        if ($licenseName -ne 'MIT') { throw "New license needs review: $($package.id): $licenseName" }
    } elseif ($licenseLine -match '<license type="file">([^<]+)</license>') {
        $licenseFile = $Matches[1]
        $licenseName = 'Publisher license file'
        $filePath = [IO.Path]::GetFullPath((Join-Path $packageRoot $licenseFile))
        if (-not $filePath.StartsWith([IO.Path]::GetFullPath($packageRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid license path.' }
        $target = Join-Path $legal $package.id
        [void][IO.Directory]::CreateDirectory($target)
        Copy-Item -LiteralPath $filePath -Destination (Join-Path $target 'LICENSE.txt')
    } else { throw "License metadata missing: $($package.id)" }
    $rows.Add("| $($package.id) | $($package.resolvedVersion) | $licenseName |")
}
$dotnetNotices = Join-Path $cache 'microsoft.extensions.hosting/10.0.9/THIRD-PARTY-NOTICES.TXT'
Copy-Item -LiteralPath $dotnetNotices -Destination (Join-Path $legal 'DOTNET-THIRD-PARTY-NOTICES.TXT')
Copy-Item -LiteralPath (Join-Path $cache 'microsoft.web.webview2/1.0.4078.44/NOTICE.txt') -Destination (Join-Path $legal 'Microsoft.Web.WebView2/NOTICE.txt')
Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/dotnet/runtime/v10.0.9/LICENSE.TXT' -OutFile (Join-Path $legal 'DOTNET-LICENSE.TXT')
[IO.File]::WriteAllLines((Join-Path $legal 'DEPENDENCIES.md'), $rows, [Text.UTF8Encoding]::new($false))
"Dependency notices collected: packages=$($unique.Count)"
