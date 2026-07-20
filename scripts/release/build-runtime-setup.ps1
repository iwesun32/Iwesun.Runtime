[CmdletBinding()]
param(
    [string]$ProductVersion = "1.0.31"
)

$ErrorActionPreference = "Stop"
if ($ProductVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "ProductVersion must use numeric major.minor.patch format."
}
$assemblyVersion = "$ProductVersion.0"
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$solutionPath = Join-Path $repositoryRoot "Iwesun.Runtime.slnx"
$dataSolutionPath = Join-Path (Split-Path -Parent $repositoryRoot) "Data\Data.slnx"
$networksSolutionPath = Join-Path (Split-Path -Parent $repositoryRoot) "Networks\Networks.slnx"
$releaseProject = Join-Path $repositoryRoot "Iwesun.Runtime.Release\Iwesun.Runtime.Release.csproj"
$setupProject = Join-Path $repositoryRoot "Iwesun.Runtime.Setup\Iwesun.Runtime.Setup.wixproj"
$verifyScript = Join-Path $repositoryRoot "scripts\release\verify-runtime-install.ps1"
$releaseAppRoot = Join-Path $repositoryRoot "artifacts\release\Iwesun.Runtime\app"
$releaseDataRoot = Join-Path $repositoryRoot "artifacts\release\Iwesun.Runtime\data"
$msiPath = Join-Path $repositoryRoot "artifacts\setup\Iwesun.Runtime.Setup.msi"

Push-Location $repositoryRoot
try {
    & dotnet build-server shutdown
    if ($LASTEXITCODE -ne 0) { throw "Build server shutdown failed." }
    & dotnet clean $solutionPath -c Debug /m:1 /nr:false
    if ($LASTEXITCODE -ne 0) { throw "Debug solution cleanup failed." }
    & dotnet clean $solutionPath -c Release /m:1 /nr:false
    if ($LASTEXITCODE -ne 0) { throw "Release solution cleanup failed." }
    & dotnet clean $setupProject -c Release /m:1 /nr:false
    if ($LASTEXITCODE -ne 0) { throw "Setup cleanup failed." }
    foreach ($configuration in @("Debug", "Release")) {
        & dotnet test $networksSolutionPath -c $configuration --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "Networks $configuration tests failed." }
        & dotnet test $dataSolutionPath -c $configuration --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { throw "Data $configuration tests failed." }
    }
    & dotnet build $solutionPath -c Debug --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false "/p:Version=$ProductVersion" "/p:AssemblyVersion=$assemblyVersion" "/p:FileVersion=$assemblyVersion" "/p:InformationalVersion=$ProductVersion"
    if ($LASTEXITCODE -ne 0) { throw "Debug solution build failed." }
    & dotnet build $solutionPath -c Release --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false "/p:Version=$ProductVersion" "/p:AssemblyVersion=$assemblyVersion" "/p:FileVersion=$assemblyVersion" "/p:InformationalVersion=$ProductVersion"
    if ($LASTEXITCODE -ne 0) { throw "Release solution build failed." }
    foreach ($configuration in @("Debug", "Release")) {
        & dotnet run --project (Join-Path $repositoryRoot "Iwesun.Runtime.FunctionalTests\Iwesun.Runtime.FunctionalTests.csproj") -c $configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "Runtime $configuration functional tests failed." }
    }
    & dotnet msbuild $releaseProject /t:PublishRuntimeRelease /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false /p:Configuration=Release "/p:Version=$ProductVersion" "/p:AssemblyVersion=$assemblyVersion" "/p:FileVersion=$assemblyVersion" "/p:InformationalVersion=$ProductVersion"
    if ($LASTEXITCODE -ne 0) { throw "Full Runtime staging failed." }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $verifyScript -InstallRoot $releaseAppRoot -DataRoot $releaseDataRoot
    if ($LASTEXITCODE -ne 0) { throw "Runtime staging verification failed." }
    & dotnet build $setupProject -c Release -t:Rebuild --disable-build-servers /m:1 /nr:false /p:BuildInParallel=false /p:UseSharedCompilation=false "/p:ProductVersion=$ProductVersion"
    if ($LASTEXITCODE -ne 0) { throw "Runtime MSI rebuild failed." }
    if (-not (Test-Path -LiteralPath $msiPath)) { throw "Runtime MSI was not generated: $msiPath" }
    $msi = Get-Item -LiteralPath $msiPath
    $hash = Get-FileHash -LiteralPath $msiPath -Algorithm SHA256
    Write-Host "Runtime full package completed."
    Write-Host "Version: $ProductVersion"
    Write-Host "MSI:     $($msi.FullName)"
    Write-Host "Bytes:   $($msi.Length)"
    Write-Host "SHA256:  $($hash.Hash)"
}
finally {
    Pop-Location
}
