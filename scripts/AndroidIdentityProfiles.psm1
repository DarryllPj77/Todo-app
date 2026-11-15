Set-StrictMode -Version 2

function Get-AndroidIdentityProfile(
    [ValidateSet('Development', 'Selected')][string]$IdentityProfile = 'Development',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
) {
    $IdentityProfile = if ($IdentityProfile -ieq 'Selected') { 'Selected' } else { 'Development' }
    $Configuration = if ($Configuration -ieq 'Release') { 'Release' } else { 'Debug' }
    $package = 'com.companyname.todoapp.maui'
    $version = '1.0'
    $artifacts = '.tools/android-build'
    if ($Configuration -eq 'Release') { $artifacts = '.tools/android-rel' }
    $report = $Configuration.ToLowerInvariant() + '-report.json'
    if ($IdentityProfile -eq 'Selected') {
        $package = 'io.github.darryllpj77.todoapp'
        $version = '1.0.0'
        $artifacts = if ($Configuration -eq 'Release') { '.tools/as-r' } else { '.tools/as-d' }
        $report = 'selected-' + $report
    }
    # Java component/action identities deliberately remain stable. Context-bound
    # intents and each APK's UID isolate the two installed application packages.
    return [pscustomobject]@{
        Name = $IdentityProfile; Configuration = $Configuration; Package = $package
        Components = 'com.companyname.todoapp.maui'; CaptureAction = 'com.companyname.todoapp.maui.QUICK_CAPTURE'
        VersionCode = '1'; VersionName = $version; Artifacts = $artifacts; AuditReport = $report
        EvaluationLabel = 'Todo App (identity test)'
    }
}

function Assert-AndroidIdentityChoice($Plan, $Profile) {
    if ($Profile.Name -eq 'Development') { return }
    Import-Module (Join-Path $PSScriptRoot 'AndroidReleasePlan.psm1') -Force -DisableNameChecking
    $null = Assert-AndroidReleasePlan $Plan
    if ($Plan.ApplicationIdDecision -cne 'Confirmed' -or $Plan.ProposedApplicationId -cne $Profile.Package) {
        throw 'Selected identity evaluation requires the matching user-confirmed application ID in the public plan.'
    }
    # Channel/custody are not needed to evaluate with the SDK development key.
    # They remain open gates before production signing or distribution.
}

Export-ModuleMember -Function Get-AndroidIdentityProfile, Assert-AndroidIdentityChoice
