param([string]$PlanPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
Import-Module (Join-Path $PSScriptRoot 'AndroidReleasePlan.psm1') -Force -DisableNameChecking
$root = Split-Path -Parent $PSScriptRoot
if (!$PlanPath) { $PlanPath = Join-Path $root 'config/android-release-plan.json' }
$reportPath = Join-Path $root '.tools/android-audit/identity-plan-report.json'
$result = [pscustomobject]@{ PlanStatus = 'Failed'; ReleaseReady = $false; Failure = $null }
try {
    $plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
    $developmentSigner = $null
    $developmentReport = Join-Path $root '.tools/android-audit/debug-report.json'
    if (Test-Path -LiteralPath $developmentReport) {
        try {
            $debug = Get-Content -LiteralPath $developmentReport -Raw | ConvertFrom-Json
            if ($debug.ArtifactStatus -eq 'Passed' -and $debug.Manifest.Debuggable -eq $true -and
                $debug.Manifest.Package -ceq $plan.CurrentDevelopmentApplicationId -and
                (Test-Path -LiteralPath $debug.ApkPath -PathType Leaf) -and
                $debug.Sha256 -ceq (Get-FileHash -LiteralPath $debug.ApkPath -Algorithm SHA256).Hash) {
                $developmentSigner = $debug.SignerCertificateSha256
            }
        } catch { $developmentSigner = $null } # Optional stale evidence leaves an explicit open gate.
    }
    $result = Assert-AndroidReleasePlan $plan $developmentSigner
    [xml]$project = Get-Content -LiteralPath (Join-Path $root 'src/TodoApp.Maui/TodoApp.Maui.csproj') -Raw
    $activeId = @($project.SelectNodes('/Project/PropertyGroup/ApplicationId[not(@Condition)]'))
    if ($activeId.Count -ne 1 -or $activeId[0].InnerText -cne $plan.CurrentDevelopmentApplicationId) {
        throw 'The active application ID differs from this planning-only development baseline. Review implementation before updating the plan.'
    }
    $references = @()
    foreach ($directory in @('src/TodoApp.Maui', 'scripts', 'tests')) {
        foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $root $directory) -Recurse -File | Where-Object {
            $_.Extension -in @('.csproj', '.cs', '.xml', '.ps1', '.psm1') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        })) {
            if ((Get-Content -LiteralPath $file.FullName -Raw).Contains($plan.CurrentDevelopmentApplicationId)) {
                $references += $file.FullName.Substring($root.Length + 1).Replace('\', '/')
            }
        }
    }
    $result | Add-Member NoteProperty ActiveDevelopmentApplicationId $activeId[0].InnerText
    $result | Add-Member NoteProperty IdentityReferenceFiles @($references | Sort-Object -Unique)
    $result | Add-Member NoteProperty PlanSha256 (Get-FileHash -LiteralPath $PlanPath -Algorithm SHA256).Hash
    $result | Add-Member NoteProperty ValidatorSha256 (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'AndroidReleasePlan.psm1') -Algorithm SHA256).Hash
    $result | Add-Member NoteProperty CheckerSha256 (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    $result | Add-Member NoteProperty DevelopmentSignerExclusionChecked ([bool]$developmentSigner)
    $result | Add-Member NoteProperty CheckedAtUtc ([DateTimeOffset]::UtcNow.ToString('o'))
} catch {
    $result = [pscustomobject]@{ PlanStatus = 'Failed'; ReleaseReady = $false; Failure = $_.Exception.Message }
    throw
} finally {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportPath))
    $temporaryReport = $reportPath + '.tmp'
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $temporaryReport -Encoding UTF8
    Move-Item -LiteralPath $temporaryReport -Destination $reportPath -Force
}
Write-Output "Identity/signing plan is structurally valid; release remains BLOCKED. Open decisions/gates: $($result.OpenDecisionsAndGates.Count). Report: $reportPath"
