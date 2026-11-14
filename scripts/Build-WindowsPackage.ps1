param()

# Compile an unsigned test MSIX only. Installation/trust/signing are separate
# developer actions; the normal Run-Windows.ps1 remains unpackaged.
$ErrorActionPreference = 'Stop'
$solutionRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $solutionRoot '.tools\dotnet\dotnet.exe'
$projectPath = Join-Path $solutionRoot 'src\TodoApp.Maui\TodoApp.Maui.csproj'
$artifacts = Join-Path $solutionRoot '.tools\windows-msix'
# A trailing backslash inside a quoted native argument escapes its closing
# quote under Windows PowerShell 5.1. MSBuild accepts a forward slash here.
$packages = (Join-Path $artifacts 'packages') + '/'
$previousDotnetRoot = $env:DOTNET_ROOT
try {
    if (Test-Path -LiteralPath $localDotnet) {
        $dotnetCommand = $localDotnet
        $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
    } else { $dotnetCommand = 'dotnet' }
    & $dotnetCommand build $projectPath --artifacts-path $artifacts `
        -p:TodoAppTargetFrameworks=net10.0-windows10.0.19041.0 `
        -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageSigningEnabled=false "-p:AppxPackageDir=$packages" --nologo -m:1
    if ($LASTEXITCODE -ne 0) { throw "Unsigned Windows package build failed (exit $LASTEXITCODE). See docs/NATIVE_GEOFENCES.md." }
    Write-Output "Unsigned test package output: $artifacts. Not installed or trusted; review docs/NATIVE_GEOFENCES.md before installation."
} finally { $env:DOTNET_ROOT = $previousDotnetRoot }
