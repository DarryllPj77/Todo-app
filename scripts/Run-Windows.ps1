param([switch]$BuildOnly)

$ErrorActionPreference = 'Stop'
$solutionRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $solutionRoot '.tools\dotnet\dotnet.exe'
$projectPath = Join-Path $solutionRoot 'src\TodoApp.Maui\TodoApp.Maui.csproj'
$previousDotnetRoot = $env:DOTNET_ROOT

try {
    if (Test-Path -LiteralPath $localDotnet) {
        $dotnetCommand = $localDotnet
        $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
    } else {
        $dotnetCommand = 'dotnet'
    }

    $buildArguments = @('build', $projectPath,
        '-p:TodoAppTargetFrameworks=net10.0-windows10.0.19041.0', '--nologo', '-m:1')
    if (-not $BuildOnly) {
        $buildArguments += '-t:Run'
    }

    & $dotnetCommand @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Windows build/run failed (exit $LASTEXITCODE). Check the MAUI setup instructions in README.md."
    }
} finally {
    $env:DOTNET_ROOT = $previousDotnetRoot
}
