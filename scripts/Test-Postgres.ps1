$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:TODOAPP_TEST_POSTGRES)) {
    throw 'Set TODOAPP_TEST_POSTGRES to a development PostgreSQL connection with CREATEDB permission. See docs/DATABASE.md.'
}

$solutionRoot = Split-Path -Parent $PSScriptRoot
$testFilter = Join-Path $solutionRoot 'TodoApp.Core.slnf'
$testArtifacts = Join-Path $solutionRoot '.tools\postgres-checks'
& dotnet test $testFilter --artifacts-path $testArtifacts --nologo -m:1
if ($LASTEXITCODE -ne 0) {
    throw "PostgreSQL test run failed (exit $LASTEXITCODE)."
}
