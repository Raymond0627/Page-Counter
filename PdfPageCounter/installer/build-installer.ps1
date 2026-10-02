<#
.SYNOPSIS
    Builds the PDF Page Counter setup.exe.

.DESCRIPTION
    Runs the full packaging pipeline:
      1. dotnet publish  - self-contained .NET 8, win-x64
      2. stages the publish output into installer\staging
      3. stages the Windows App Runtime redistributable into installer\redist
      4. compiles installer\setup.iss into installer\output\*Setup.exe

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Version
    Overrides AppVersion passed to the Inno Setup script.

.PARAMETER SkipPublish
    Reuses the existing installer\staging and installer\redist folders and only
    recompiles the installer. Useful when iterating on setup.iss.

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version 1.2.0
    .\build-installer.ps1 -SkipPublish
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Version,
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

$installerDir = $PSScriptRoot
$solutionDir  = Split-Path -Parent $installerDir
$appProj      = Join-Path $solutionDir 'PdfPageCounter.App\PdfPageCounter.App.csproj'
$stagingDir   = Join-Path $installerDir 'staging'
$redistDir    = Join-Path $installerDir 'redist'
$outputDir    = Join-Path $installerDir 'output'

$rid            = 'win-x64'
$wasdkVersion   = '2.4.0'
$runtimeMsixRel = "tools\MSIX\win10-x64\Microsoft.WindowsAppRuntime.2.msix"
$runtimeMsixName= 'Microsoft.WindowsAppRuntime.2.msix'
$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
)

function Find-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($candidate in @('C:\dotnet\dotnet.exe', "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe")) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw 'The .NET SDK could not be found. Install .NET 8 SDK or pass a path via PATH.'
}

function Find-Iscc {
    foreach ($candidate in $isccCandidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    $cmd = Get-Command ISCC -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw 'Inno Setup (ISCC.exe) was not found. Install Inno Setup 6.'
}

if (-not $SkipPublish) {
    $dotnet = Find-Dotnet
    Write-Host "==> dotnet: $dotnet" -ForegroundColor Cyan

    # Always start from a clean staging folder so removed files cannot linger
    # in the installer from a previous run.
    Write-Host '==> Cleaning staging folders' -ForegroundColor Cyan
    Remove-Item -LiteralPath $stagingDir, $redistDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $stagingDir, $redistDir -Force | Out-Null

    Write-Host '==> Publishing self-contained ($rid, $Configuration)' -ForegroundColor Cyan
    & $dotnet publish $appProj `
        -c $Configuration `
        -p:Platform=x64 `
        -r $rid `
        --self-contained true `
        -o $stagingDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    # The Windows App SDK is framework-dependent (WindowsAppSDKSelfContained=false
    # in the csproj), so the runtime framework package must ship with the setup.
    $msix = Join-Path $env:USERPROFILE ".nuget\packages\microsoft.windowsappsdk.runtime\$wasdkVersion\$runtimeMsixRel"
    if (-not (Test-Path -LiteralPath $msix)) {
        & $dotnet restore $appProj | Out-Null
    }
    if (-not (Test-Path -LiteralPath $msix)) {
        throw "Windows App Runtime redistributable not found at: $msix"
    }

    Write-Host "==> Staging Windows App Runtime $wasdkVersion redistributable" -ForegroundColor Cyan
    Copy-Item -LiteralPath $msix -Destination (Join-Path $redistDir $runtimeMsixName) -Force
}

foreach ($required in @($stagingDir, $redistDir)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Missing '$required'. Run without -SkipPublish to build it."
    }
}

# setup.iss is the single source of truth for the executable name, so the two
# cannot drift apart when the app is renamed.
$issText = Get-Content -LiteralPath (Join-Path $installerDir 'setup.iss') -Raw
$appExeName = if ($issText -match '#define\s+MyAppExeName\s+"([^"]+)"') { $Matches[1] } else { $null }
if (-not $appExeName) {
    throw 'Could not read "#define MyAppExeName" from setup.iss.'
}

$stagedExe = Join-Path $stagingDir $appExeName
if (-not (Test-Path -LiteralPath $stagedExe)) {
    throw "Staged app executable not found: $stagedExe"
}

$iscc = Find-Iscc
Write-Host "==> Inno Setup: $iscc" -ForegroundColor Cyan

$issArgs = @((Join-Path $installerDir 'setup.iss'))
if ($Version) { $issArgs += "/DMyAppVersion=$Version" }

Write-Host '==> Compiling setup.iss' -ForegroundColor Cyan
& $iscc @issArgs
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }

$setup = Get-ChildItem -LiteralPath $outputDir -Filter '*.exe' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $setup) { throw "No setup.exe was produced in $outputDir." }

Write-Host ''
Write-Host '==> Build complete' -ForegroundColor Green
Write-Host ("    " + $setup.FullName)
Write-Host ("    {0:N1} MB" -f ($setup.Length / 1MB))
