[CmdletBinding()]
param(
    [string]$ProductVersion = "1.0.47",
    [string]$NetworksVersion = "3.0.0-beta.6",
    [string]$PreviousRuntimeRoot = "$env:ProgramFiles\Iwesun\Runtime"
)

$ErrorActionPreference = "Stop"

function Get-Sha256Hash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $stream = [System.IO.File]::OpenRead($Path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [System.BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace("-", "")
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

if ($ProductVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "ProductVersion must use numeric major.minor.patch format."
}
if ($NetworksVersion -notmatch '^\d+\.\d+\.\d+-beta\.\d+$') {
    throw "NetworksVersion must use major.minor.patch-beta.number format."
}
$assemblyVersion = "$ProductVersion.0"
$informationalVersion = "$ProductVersion-beta.1"
$productCodeSeed = [System.Text.Encoding]::UTF8.GetBytes("Iwesun.Runtime/$ProductVersion")
$productCodeHasher = [System.Security.Cryptography.SHA256]::Create()
try {
    $productCodeHash = $productCodeHasher.ComputeHash($productCodeSeed)
}
finally {
    $productCodeHasher.Dispose()
}
$productCode = ([Guid]::new([byte[]]$productCodeHash[0..15])).ToString("D").ToUpperInvariant()
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$PreviousRuntimeRoot = [System.IO.Path]::GetFullPath($PreviousRuntimeRoot)
$solutionPath = Join-Path $repositoryRoot "Iwesun.Runtime.slnx"
$dataTestsProject = Join-Path $repositoryRoot "modules\Data\tests\Iwesun.Runtime.Data.Tests\Iwesun.Runtime.Data.Tests.csproj"
$networksTestsProject = Join-Path $repositoryRoot "modules\Networks\tests\Iwesun.Runtime.Networks.Tests\Iwesun.Runtime.Networks.Tests.csproj"
$webView2TestsProject = Join-Path $repositoryRoot "modules\WebView2\tests\Iwesun.Runtime.WebView2.Tests\Iwesun.Runtime.WebView2.Tests.csproj"
$functionalTestsProject = Join-Path $repositoryRoot "modules\Diagnostics\tests\Iwesun.Runtime.FunctionalTests\Iwesun.Runtime.FunctionalTests.csproj"
$binaryCompatibilityProject = Join-Path $repositoryRoot "modules\Diagnostics\validation\Iwesun.Runtime.BinaryCompatibilityHost\Iwesun.Runtime.BinaryCompatibilityHost.csproj"
$networksProject = Join-Path $repositoryRoot "modules\Networks\src\Iwesun.Runtime.Networks\Iwesun.Runtime.Networks.csproj"
$setupProject = Join-Path $repositoryRoot "modules\Packaging\setup\Iwesun.Runtime.Setup\Iwesun.Runtime.Setup.wixproj"
$verifyScript = Join-Path $repositoryRoot "scripts\release\verify-runtime-install.ps1"
$releaseAppRoot = Join-Path $repositoryRoot "artifacts\release\Iwesun.Runtime\app"
$releaseDataRoot = Join-Path $repositoryRoot "artifacts\release\Iwesun.Runtime\data"
$msiPath = Join-Path $repositoryRoot "artifacts\setup\Iwesun.Runtime.Setup.msi"
$portablePackagePath = Join-Path $repositoryRoot "artifacts\packages\Iwesun.Runtime.$informationalVersion.zip"
$networksPackagePath = Join-Path $repositoryRoot "artifacts\packages\Iwesun.Runtime.Networks.$NetworksVersion.nupkg"
$networksSymbolsPath = Join-Path $repositoryRoot "artifacts\packages\Iwesun.Runtime.Networks.$NetworksVersion.snupkg"
$releaseGuidePath = Join-Path $repositoryRoot "docs\IWESUN_RUNTIME_${ProductVersion}_BETA_RELEASE_GUIDE.md"
$releaseManifestPath = Join-Path $repositoryRoot "docs\IWESUN_RUNTIME_${ProductVersion}_BETA_RELEASE_MANIFEST.md"
$bundleRoot = Join-Path $repositoryRoot "artifacts\packages\Iwesun.Runtime.$informationalVersion"

if (-not (Test-Path -LiteralPath (Join-Path $PreviousRuntimeRoot "lib\Iwesun.Runtime.Diagnostics\Iwesun.Runtime.Diagnostics.dll"))) {
    throw "PreviousRuntimeRoot does not contain the required Diagnostics baseline: $PreviousRuntimeRoot"
}

Push-Location $repositoryRoot
try {
    & dotnet build-server shutdown
    if ($LASTEXITCODE -ne 0) { throw "Build server shutdown failed." }
    & dotnet build $binaryCompatibilityProject -c Release --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false "/p:BaselineRuntimeRoot=$PreviousRuntimeRoot"
    if ($LASTEXITCODE -ne 0) { throw "Previous-release binary compatibility host build failed." }
    & dotnet clean $solutionPath -c Debug /m:1 /nr:false
    if ($LASTEXITCODE -ne 0) { throw "Debug solution cleanup failed." }
    & dotnet clean $solutionPath -c Release /m:1 /nr:false
    if ($LASTEXITCODE -ne 0) { throw "Release solution cleanup failed." }
    & dotnet clean $setupProject -c Release /m:1 /nr:false
    if ($LASTEXITCODE -ne 0) { throw "Setup cleanup failed." }
    foreach ($configuration in @("Debug", "Release")) {
        & dotnet test $networksTestsProject -c $configuration --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "Networks $configuration tests failed." }
        & dotnet test $dataTestsProject -c $configuration --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "Data $configuration tests failed." }
        & dotnet test $webView2TestsProject -c $configuration --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "WebView2 $configuration tests failed." }
    }
    & dotnet build-server shutdown
    if ($LASTEXITCODE -ne 0) { throw "Post-test build server shutdown failed." }
    & dotnet build $solutionPath -c Debug --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false "/p:Version=$ProductVersion" "/p:AssemblyVersion=$assemblyVersion" "/p:FileVersion=$assemblyVersion" "/p:InformationalVersion=$informationalVersion"
    if ($LASTEXITCODE -ne 0) { throw "Debug solution build failed." }
    & dotnet build-server shutdown
    if ($LASTEXITCODE -ne 0) { throw "Post-Debug build server shutdown failed." }
    & dotnet build $solutionPath -c Release --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false "/p:Version=$ProductVersion" "/p:AssemblyVersion=$assemblyVersion" "/p:FileVersion=$assemblyVersion" "/p:InformationalVersion=$informationalVersion"
    if ($LASTEXITCODE -ne 0) { throw "Release solution build failed." }
    foreach ($configuration in @("Debug", "Release")) {
        & dotnet run --project $functionalTestsProject -c $configuration --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "Runtime $configuration functional tests failed." }
    }
    $binaryCompatibilityHost = Join-Path $repositoryRoot "modules\Diagnostics\validation\Iwesun.Runtime.BinaryCompatibilityHost\bin\Release\net10.0\Iwesun.Runtime.BinaryCompatibilityHost.dll"
    $previousBinaryCompatibilityHost = $env:IWESUN_RUNTIME_BINARY_COMPAT_HOST
    try {
        $env:IWESUN_RUNTIME_BINARY_COMPAT_HOST = $binaryCompatibilityHost
        & dotnet run --project $functionalTestsProject -c Release --no-build -- --child --scenario binary-compatibility
        if ($LASTEXITCODE -ne 0) { throw "Previous-release binary host failed against the current Runtime." }
    }
    finally {
        $env:IWESUN_RUNTIME_BINARY_COMPAT_HOST = $previousBinaryCompatibilityHost
    }
    & dotnet build $solutionPath -c Publish --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false "/p:Version=$ProductVersion" "/p:AssemblyVersion=$assemblyVersion" "/p:FileVersion=$assemblyVersion" "/p:InformationalVersion=$informationalVersion"
    if ($LASTEXITCODE -ne 0) { throw "Full Runtime staging failed." }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $verifyScript -InstallRoot $releaseAppRoot -DataRoot $releaseDataRoot
    if ($LASTEXITCODE -ne 0) { throw "Runtime staging verification failed." }
    & dotnet pack $networksProject -c Release --disable-build-servers /m:1 /nr:false "/p:Version=$NetworksVersion" "/p:AssemblyVersion=3.0.0.0" "/p:FileVersion=3.0.0.0" "/p:InformationalVersion=$NetworksVersion"
    if ($LASTEXITCODE -ne 0) { throw "Networks beta package build failed." }
    & dotnet build $setupProject -c Release -t:Rebuild --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false "/p:ProductVersion=$ProductVersion" "/p:ProductCode=$productCode"
    if ($LASTEXITCODE -ne 0) { throw "Runtime MSI rebuild failed." }
    foreach ($requiredArtifact in @($msiPath, $portablePackagePath, $networksPackagePath, $networksSymbolsPath, $releaseGuidePath, $releaseManifestPath)) {
        if (-not (Test-Path -LiteralPath $requiredArtifact)) {
            throw "Required beta artifact was not generated: $requiredArtifact"
        }
    }
    if (Test-Path -LiteralPath $bundleRoot) {
        throw "Beta bundle already exists; refusing to overwrite an existing candidate: $bundleRoot"
    }
    $null = New-Item -ItemType Directory -Path $bundleRoot
    $bundleFiles = @(
        @{ Source = $msiPath; Destination = (Join-Path $bundleRoot "Iwesun.Runtime.$informationalVersion.msi") },
        @{ Source = $portablePackagePath; Destination = (Join-Path $bundleRoot "Iwesun.Runtime.$informationalVersion.zip") },
        @{ Source = $networksPackagePath; Destination = (Join-Path $bundleRoot ([System.IO.Path]::GetFileName($networksPackagePath))) },
        @{ Source = $networksSymbolsPath; Destination = (Join-Path $bundleRoot ([System.IO.Path]::GetFileName($networksSymbolsPath))) },
        @{ Source = $releaseGuidePath; Destination = (Join-Path $bundleRoot ([System.IO.Path]::GetFileName($releaseGuidePath))) },
        @{ Source = $releaseManifestPath; Destination = (Join-Path $bundleRoot "RELEASE_MANIFEST.md") }
    )
    foreach ($bundleFile in $bundleFiles) {
        Copy-Item -LiteralPath $bundleFile.Source -Destination $bundleFile.Destination
    }
    $checksumLines = foreach ($bundleFile in $bundleFiles) {
        $item = Get-Item -LiteralPath $bundleFile.Destination
        $itemHash = Get-Sha256Hash -Path $bundleFile.Destination
        "$itemHash  $($item.Name)"
    }
    $checksumPath = Join-Path $bundleRoot "SHA256SUMS.txt"
    [System.IO.File]::WriteAllLines($checksumPath, $checksumLines, [System.Text.UTF8Encoding]::new($false))
    $msi = Get-Item -LiteralPath $msiPath
    $hash = Get-Sha256Hash -Path $msiPath
    Write-Host "Runtime full package completed."
    Write-Host "Version: $ProductVersion"
    Write-Host "Product: {$productCode}"
    Write-Host "Networks: $NetworksVersion"
    Write-Host "MSI:     $($msi.FullName)"
    Write-Host "Bytes:   $($msi.Length)"
    Write-Host "SHA256:  $hash"
    Write-Host "Bundle:  $bundleRoot"
    Write-Host "Checksums: $checksumPath"
}
finally {
    Pop-Location
}
