Set-StrictMode -Version 2

function Assert-AndroidReleasePlan($Plan, [string]$KnownDevelopmentSignerSha256) {
    $required = @('Schema', 'Stage', 'ReleaseReady', 'CurrentDevelopmentApplicationId', 'ProposedApplicationId',
        'ApplicationIdDecision', 'Distribution', 'DistributionDecision', 'InitialVersionCode', 'InitialVersionName',
        'CustodianRole', 'CustodyDecision', 'SigningModel', 'KeyStorage', 'IndependentBackupCopies',
        'RecoveryRehearsal', 'ApprovedAppSigningCertificateSha256', 'ApprovedUploadCertificateSha256', 'MigrationStrategy', 'NativeAcceptance')
    $actual = @($Plan.PSObject.Properties | Select-Object -ExpandProperty Name | Sort-Object -CaseSensitive)
    if (($actual -join "`n") -cne (($required | Sort-Object -CaseSensitive) -join "`n")) {
        throw 'Plan fields changed. Only the documented public planning fields are accepted; do not store secrets or key paths.'
    }
    if ($Plan.Schema -isnot [int] -or $Plan.Schema -ne 1 -or $Plan.Stage -cne 'PlanningOnly' -or
        $Plan.ReleaseReady -isnot [bool] -or $Plan.ReleaseReady) { throw 'This validator accepts a planning-only, not-release-ready document.' }
    if ($Plan.CurrentDevelopmentApplicationId -cne 'com.companyname.todoapp.maui') { throw 'Development identity baseline changed.' }
    if ($Plan.ProposedApplicationId -isnot [string] -or $Plan.ProposedApplicationId -cnotmatch '^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*){2,}$' -or
        $Plan.ProposedApplicationId -ceq $Plan.CurrentDevelopmentApplicationId -or $Plan.ProposedApplicationId -cmatch '^(com\.example|com\.companyname)\.') {
        throw 'Choose a distinct lowercase permanent application ID with at least three segments and no placeholder namespace.'
    }
    foreach ($field in @('ApplicationIdDecision', 'DistributionDecision', 'CustodyDecision')) {
        if ($Plan.$field -cnotin @('Pending', 'Confirmed')) { throw 'Decision status must be Pending or Confirmed.' }
    }
    if ($Plan.InitialVersionCode -isnot [int] -or $Plan.InitialVersionCode -ne 1 -or
        $Plan.InitialVersionName -isnot [string] -or $Plan.InitialVersionName -cnotmatch '^1\.0\.0$') {
        throw 'This initial, separately installed application plan starts at versionCode 1 / versionName 1.0.0.'
    }
    if ($Plan.CustodianRole -cnotin @('ProjectOwner', 'OrganizationOwner') -or $Plan.KeyStorage -cne 'ExternalEncryptedStore' -or
        $Plan.IndependentBackupCopies -isnot [int] -or $Plan.IndependentBackupCopies -lt 2) { throw 'Record an owner-controlled external encrypted store and at least two independent encrypted backups.' }
    if ($Plan.Distribution -ceq 'PrivateApk') {
        if ($Plan.SigningModel -cne 'OwnerManagedAppSigningKey') { throw 'Private APK delivery requires the owner-managed app-signing model.' }
        if ($null -ne $Plan.ApprovedUploadCertificateSha256) { throw 'Private APK planning must not confuse an upload certificate with the app signer.' }
    } elseif ($Plan.Distribution -ceq 'GooglePlayInternal') {
        if ($Plan.SigningModel -cne 'PlayAppSigningSeparateUploadKey') { throw 'Play planning must distinguish app-signing and upload keys.' }
    } else { throw 'Unsupported planned distribution path.' }
    foreach ($field in @('ApprovedAppSigningCertificateSha256', 'ApprovedUploadCertificateSha256')) {
        if ($null -ne $Plan.$field -and ($Plan.$field -isnot [string] -or $Plan.$field -cnotmatch '^[0-9A-F]{64}$')) {
            throw 'Only null or a public uppercase SHA-256 certificate fingerprint belongs in certificate fields.'
        }
        if ($KnownDevelopmentSignerSha256 -and $null -ne $Plan.$field -and
            $Plan.$field -ceq $KnownDevelopmentSignerSha256.ToUpperInvariant()) { throw 'The known SDK development certificate cannot be an approved production app/upload signer.' }
    }
    if ($null -ne $Plan.ApprovedAppSigningCertificateSha256 -and
        $Plan.ApprovedAppSigningCertificateSha256 -ceq $Plan.ApprovedUploadCertificateSha256) { throw 'Do not reuse one certificate for the two planned Play key roles.' }
    if ($Plan.RecoveryRehearsal -cne 'Pending' -or $Plan.NativeAcceptance -cne 'Pending' -or
        $Plan.MigrationStrategy -cne 'SeparateInstallNoAutomaticLocalMigration') {
        throw 'This planning document cannot certify recovery/native acceptance or an automatic local-data migration.'
    }
    $open = @()
    foreach ($field in @('ApplicationIdDecision', 'DistributionDecision', 'CustodyDecision')) {
        if ($Plan.$field -ceq 'Pending') { $open += $field }
    }
    if ($null -eq $Plan.ApprovedAppSigningCertificateSha256) { $open += 'ApprovedAppSigningCertificateSha256' }
    if ($Plan.Distribution -ceq 'GooglePlayInternal' -and $null -eq $Plan.ApprovedUploadCertificateSha256) { $open += 'ApprovedUploadCertificateSha256' }
    if (!$KnownDevelopmentSignerSha256) { $open += 'DevelopmentSignerExclusionEvidence' }
    $open += @('RecoveryRehearsal', 'NativeAcceptance', 'PermanentIdentityImplementation', 'DistributionAcceptance')
    return [pscustomobject]@{ PlanStatus = 'StructurallyValid'; Stage = 'PlanningOnly'; ReleaseReady = $false; ProposedApplicationId = $Plan.ProposedApplicationId; Distribution = $Plan.Distribution; OpenDecisionsAndGates = $open }
}

Export-ModuleMember -Function Assert-AndroidReleasePlan
