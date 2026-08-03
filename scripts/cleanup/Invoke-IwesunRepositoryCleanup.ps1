[CmdletBinding()]
param(
    [ValidateSet("Audit", "Quarantine")]
    [string]$Mode = "Audit"
)

$ErrorActionPreference = "Stop"

$runtimeRoot = [System.IO.Path]::GetFullPath("D:\Git Space\Runtime")
$archiveRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $runtimeRoot ".archive\retired-repositories\2026-07-22"))

$repositories = @(
    [pscustomobject]@{
        Name = "Data"
        Source = [System.IO.Path]::GetFullPath("D:\Git Space\Data")
        Destination = [System.IO.Path]::GetFullPath((Join-Path $archiveRoot "Data"))
        RequiredRuntimeMarker = [System.IO.Path]::GetFullPath(
            (Join-Path $runtimeRoot "Iwesun.Runtime.Data.Tests\Iwesun.Runtime.Data.Tests.csproj"))
    },
    [pscustomobject]@{
        Name = "Networks"
        Source = [System.IO.Path]::GetFullPath("D:\Git Space\Networks")
        Destination = [System.IO.Path]::GetFullPath((Join-Path $archiveRoot "Networks"))
        RequiredRuntimeMarker = [System.IO.Path]::GetFullPath(
            (Join-Path $runtimeRoot "Iwesun.Runtime.Networks\Iwesun.Runtime.Networks.csproj"))
    }
)

foreach ($repository in $repositories) {
    if (-not $repository.Source.StartsWith("D:\Git Space\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Cleanup source escaped the fixed Git Space boundary: $($repository.Source)"
    }
    if (-not $repository.Destination.StartsWith($archiveRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Cleanup destination escaped the fixed archive boundary: $($repository.Destination)"
    }
    if (-not [System.IO.File]::Exists($repository.RequiredRuntimeMarker)) {
        throw "Runtime migration marker is missing for $($repository.Name): $($repository.RequiredRuntimeMarker)"
    }

    $sourceExists = [System.IO.Directory]::Exists($repository.Source)
    $destinationExists = [System.IO.Directory]::Exists($repository.Destination)
    Write-Host "[$($repository.Name)] source=$sourceExists archive=$destinationExists"

    if ($Mode -eq "Audit") {
        continue
    }

    if (-not $sourceExists) {
        if ($destinationExists) {
            Write-Host "[$($repository.Name)] already quarantined."
            continue
        }
        throw "Neither source nor quarantine destination exists for $($repository.Name)."
    }
    if ($destinationExists) {
        throw "Quarantine destination already exists for $($repository.Name): $($repository.Destination)"
    }

    [System.IO.Directory]::CreateDirectory($archiveRoot) | Out-Null
    Move-Item -LiteralPath $repository.Source -Destination $repository.Destination
    Write-Host "[$($repository.Name)] quarantined => $($repository.Destination)"
}

Write-Host "Cleanup environment completed in $Mode mode. No files were deleted."
