$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
Import-Module (Join-Path $PSScriptRoot '../scripts/AndroidReleasePlan.psm1') -Force -DisableNameChecking
$baseline = Get-Content (Join-Path $PSScriptRoot '../config/android-release-plan.json') -Raw | ConvertFrom-Json
$result = Assert-AndroidReleasePlan $baseline
if ($result.ReleaseReady -ne $false -or $result.Stage -cne 'PlanningOnly') { throw 'A valid plan incorrectly approved release.' }
$script:passed = 1
$pending = $baseline | ConvertTo-Json | ConvertFrom-Json
$pending.ApplicationIdDecision = 'Pending'
$pendingResult = Assert-AndroidReleasePlan $pending
if ($pendingResult.OpenDecisionsAndGates -notcontains 'ApplicationIdDecision') { throw 'An undecided identity was treated as confirmed.' }
$script:passed++
function Test-PlanRejected([string]$Name, [scriptblock]$Change) {
    $copy = $baseline | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    & $Change $copy
    $rejected = $false
    try { $null = Assert-AndroidReleasePlan $copy } catch { $rejected = $true }
    if (!$rejected) { throw "Identity plan accepted invalid input: $Name" }
    $script:passed++
}
Test-PlanRejected 'release approval claim' { param($p) $p.ReleaseReady=$true }
Test-PlanRejected 'string false instead of boolean' { param($p) $p.ReleaseReady='false' }
Test-PlanRejected 'applied plan stage' { param($p) $p.Stage='Production' }
Test-PlanRejected 'unknown schema' { param($p) $p.Schema=2 }
Test-PlanRejected 'secret field' { param($p) $p | Add-Member NoteProperty SigningPassword 'dummy-test-value' }
Test-PlanRejected 'key file path field' { param($p) $p | Add-Member NoteProperty KeystorePath 'C:\dummy-test.p12' }
Test-PlanRejected 'missing field' { param($p) $p.PSObject.Properties.Remove('CustodyDecision') }
Test-PlanRejected 'active identity changed' { param($p) $p.CurrentDevelopmentApplicationId='another.app.identity' }
Test-PlanRejected 'placeholder permanent ID' { param($p) $p.ProposedApplicationId='com.companyname.todo' }
Test-PlanRejected 'same permanent and development ID' { param($p) $p.ProposedApplicationId=$p.CurrentDevelopmentApplicationId }
Test-PlanRejected 'uppercase application ID' { param($p) $p.ProposedApplicationId='io.github.Owner.todo' }
Test-PlanRejected 'invalid application ID' { param($p) $p.ProposedApplicationId='io.github.my-owner.todo' }
Test-PlanRejected 'fake decision state' { param($p) $p.ApplicationIdDecision='AssumedApproved' }
Test-PlanRejected 'invalid initial version code' { param($p) $p.InitialVersionCode=0 }
Test-PlanRejected 'unexpected version seed' { param($p) $p.InitialVersionName='0.0.1' }
Test-PlanRejected 'unknown custodian role' { param($p) $p.CustodianRole='Assistant' }
Test-PlanRejected 'repository keystore storage' { param($p) $p.KeyStorage='Repository' }
Test-PlanRejected 'single backup copy' { param($p) $p.IndependentBackupCopies=1 }
Test-PlanRejected 'unknown distribution route' { param($p) $p.Distribution='Published' }
Test-PlanRejected 'wrong private APK signing model' { param($p) $p.Distribution='PrivateApk'; $p.SigningModel='PlayAppSigningSeparateUploadKey' }
Test-PlanRejected 'upload role confused with APK signing' { param($p) $p.Distribution='PrivateApk'; $p.SigningModel='OwnerManagedAppSigningKey'; $p.ApprovedUploadCertificateSha256=('A'*64) }
Test-PlanRejected 'malformed public certificate identity' { param($p) $p.ApprovedAppSigningCertificateSha256='not-a-certificate-fingerprint' }
Test-PlanRejected 'unproved recovery pass' { param($p) $p.RecoveryRehearsal='Passed' }
Test-PlanRejected 'unproved native pass' { param($p) $p.NativeAcceptance='Passed' }
Test-PlanRejected 'automatic local migration promise' { param($p) $p.MigrationStrategy='Automatic' }
$play = $baseline | ConvertTo-Json | ConvertFrom-Json
$play.Distribution='GooglePlayInternal'; $play.SigningModel='PlayAppSigningSeparateUploadKey'
$null = Assert-AndroidReleasePlan $play
$script:passed++
$play.ApprovedAppSigningCertificateSha256=('A'*64); $play.ApprovedUploadCertificateSha256=('B'*64)
$null = Assert-AndroidReleasePlan $play
$script:passed++
$play.ApprovedUploadCertificateSha256=$play.ApprovedAppSigningCertificateSha256
$rejected=$false; try { $null=Assert-AndroidReleasePlan $play } catch { $rejected=$true }
if (!$rejected) { throw 'Plan conflated app-signing and upload certificates.' }
$script:passed++
$confirmed=$baseline | ConvertTo-Json | ConvertFrom-Json
$confirmed.ApplicationIdDecision='Confirmed'; $confirmed.DistributionDecision='Confirmed'; $confirmed.CustodyDecision='Confirmed'
$confirmedResult=Assert-AndroidReleasePlan $confirmed
if ($confirmedResult.ReleaseReady -ne $false -or $confirmedResult.OpenDecisionsAndGates -notcontains 'RecoveryRehearsal') { throw 'Confirmed choices incorrectly closed release/recovery gates.' }
$script:passed++
foreach ($field in @('ApprovedAppSigningCertificateSha256', 'ApprovedUploadCertificateSha256')) {
    $copy=$baseline | ConvertTo-Json | ConvertFrom-Json
    $copy.Distribution='GooglePlayInternal'; $copy.SigningModel='PlayAppSigningSeparateUploadKey'
    $copy.$field=('D'*64)
    $rejected=$false; try { $null=Assert-AndroidReleasePlan $copy ('D'*64) } catch { $rejected=$true }
    if (!$rejected) { throw 'Plan approved a known development certificate for a production role.' }
    $script:passed++
}
Write-Output "Android identity/signing plan tests passed: $script:passed"
