Set-StrictMode -Version 2

function Assert-AndroidValue($Actual, $Expected, [string]$Reason) {
    if ([string]$Actual -cne [string]$Expected) { throw $Reason }
}

function Assert-AndroidNames($Actual, $Expected, [string]$Reason) {
    $left = @($Actual | Sort-Object -CaseSensitive)
    $right = @($Expected | Sort-Object -CaseSensitive)
    if (($left -join "`n") -cne ($right -join "`n")) { throw $Reason }
}

# Normalize aapt2's compiled XML tree to an XML DOM. Android attribute
# namespaces are removed deliberately; no application content is read here.
function ConvertFrom-AaptXmlTree([string[]]$Lines) {
    $document = New-Object System.Xml.XmlDocument
    $stack = New-Object 'System.Collections.Generic.List[object]'
    foreach ($line in $Lines) {
        if ($line -match '^\s*$|^\s*N:') { continue }
        if ($line -match '^(?<space>\s*)E: (?<name>[\w.-]+)(?: \(line=\d+\))?$') {
            $indent = $Matches.space.Length
            $node = $document.CreateElement($Matches.name)
            while ($stack.Count -gt 0 -and $stack[$stack.Count - 1].Indent -ge $indent) { $stack.RemoveAt($stack.Count - 1) }
            if ($stack.Count -eq 0) { [void]$document.AppendChild($node) }
            else { [void]$stack[$stack.Count - 1].Node.AppendChild($node) }
            $stack.Add(@{ Indent = $indent; Node = $node })
        } elseif ($line -match '^\s*A: (?:(?:http://schemas.android.com/apk/res/android|android):)?(?<name>[\w.-]+)(?:\(0x[0-9a-fA-F]+\))?=(?<value>.+)$') {
            if ($stack.Count -eq 0) { throw 'Attribute outside a compiled XML element.' }
            $name = $Matches.name
            $value = $Matches.value
            if ($value.StartsWith('"')) {
                if ($value -notmatch '^("(?:\\.|[^"\\])*")(?: \(Raw: .+\))?$') { throw 'Unsupported quoted aapt2 attribute.' }
                $value = ConvertFrom-Json $Matches[1]
            } elseif ($value -notmatch '^(?:@0x[0-9a-fA-F]+|0x[0-9a-fA-F]+|#[0-9a-fA-F]{8}|-?\d+(?:\.\d+)?(?:dp|sp)?|true|false|@null)$') {
                throw 'Unsupported aapt2 attribute representation.'
            }
            $node = $stack[$stack.Count - 1].Node
            if ($node.HasAttribute($name)) { throw 'Duplicate normalized Android attribute.' }
            $node.SetAttribute($name, [string]$value)
        } else { throw 'Unsupported aapt2 XML tree output. Review the installed tool version.' }
    }
    if ($null -eq $document.DocumentElement) { throw 'Empty compiled XML tree.' }
    return ,$document
}

function Get-AndroidNode([xml]$Document, [string]$XPath) {
    $nodes = $Document.SelectNodes($XPath)
    if ($nodes.Count -ne 1) { throw "Expected one Android node: $XPath" }
    return ,$nodes[0]
}

function Assert-AndroidManifest([xml]$Manifest, [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug') {
    $Configuration = if ($Configuration -ieq 'Release') { 'Release' } else { 'Debug' }
    $package = 'com.companyname.todoapp.maui'
    $root = Get-AndroidNode $Manifest '/manifest'
    Assert-AndroidValue $root.GetAttribute('package') $package 'Unexpected APK application ID.'
    $sdk = Get-AndroidNode $Manifest '/manifest/uses-sdk'
    Assert-AndroidValue $sdk.GetAttribute('minSdkVersion') '21' 'Minimum SDK changed; review the acceptance matrix.'
    Assert-AndroidValue $sdk.GetAttribute('targetSdkVersion') '36' 'Target SDK changed; review native behavior before updating this baseline.'
    $permissions = @('ACCESS_NETWORK_STATE', 'INTERNET', 'POST_NOTIFICATIONS', 'ACCESS_COARSE_LOCATION',
        'ACCESS_FINE_LOCATION', 'ACCESS_BACKGROUND_LOCATION', 'RECEIVE_BOOT_COMPLETED') | ForEach-Object { "android.permission.$_" }
    $permissions += "$package.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"
    $permissionNodes = @($Manifest.SelectNodes('/manifest/*[starts-with(name(),"uses-permission")]'))
    Assert-AndroidNames @($permissionNodes | ForEach-Object { $_.GetAttribute('name') }) $permissions 'APK permission set changed.'
    foreach ($permission in $permissionNodes) {
        Assert-AndroidValue $permission.LocalName 'uses-permission' 'Permission declaration form changed.'
        if ($permission.HasAttribute('maxSdkVersion')) { throw 'Permission SDK limit changed.' }
    }
    $declaration = Get-AndroidNode $Manifest '/manifest/permission'
    Assert-AndroidValue $declaration.GetAttribute('name') "$package.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION" 'Unexpected declared permission.'
    Assert-AndroidValue $declaration.GetAttribute('protectionLevel') '0x00000002' 'Internal receiver permission must remain signature protected.'
    $app = Get-AndroidNode $Manifest '/manifest/application'
    Assert-AndroidValue $app.GetAttribute('allowBackup') 'false' 'Managed backup must remain disabled.'
    Assert-AndroidValue $app.GetAttribute('usesCleartextTraffic') 'false' 'Cleartext traffic must remain explicitly disabled.'
    foreach ($attribute in @('fullBackupContent', 'dataExtractionRules', 'networkSecurityConfig')) {
        if ($app.GetAttribute($attribute) -cnotmatch '^@0x[0-9a-f]+$') { throw 'Backup/transport configuration reference is missing.' }
    }
    if ($app.HasAttribute('backupAgent')) { throw 'A custom backup agent requires a separate reviewed policy.' }
    if ($Configuration -eq 'Debug') {
        Assert-AndroidValue $app.GetAttribute('debuggable') 'true' 'Debug audit requires an explicitly debuggable artifact.'
    } elseif ($app.GetAttribute('debuggable') -cnotin @('', 'false')) { throw 'Release evaluation must not be debuggable.' }
    $main = @($app.SelectNodes('activity') | Where-Object { $_.GetAttribute('name') -cmatch '^crc[0-9a-f]+\.MainActivity$' })
    if ($main.Count -ne 1) { throw 'Expected one generated main Activity.' }
    Assert-AndroidNames @($main[0].SelectNodes('intent-filter/action') | ForEach-Object { $_.GetAttribute('name') }) @('android.intent.action.MAIN') 'Main launcher action changed.'
    Assert-AndroidNames @($main[0].SelectNodes('intent-filter/category') | ForEach-Object { $_.GetAttribute('name') }) @('android.intent.category.LAUNCHER') 'Main launcher category changed.'
    Assert-AndroidValue $main[0].SelectNodes('meta-data[@name="android.app.shortcuts"]').Count 1 'Shortcut metadata must belong to the main launcher Activity.'
    $capture = Get-AndroidNode $Manifest "/manifest/application/activity[@name='$package.QuickCaptureActivity']"
    Assert-AndroidValue $capture.GetAttribute('taskAffinity') '' 'Capture task affinity changed.'
    if (!$capture.HasAttribute('taskAffinity')) { throw 'Capture requires an explicit empty task affinity.' }
    Assert-AndroidValue $capture.GetAttribute('excludeFromRecents') 'true' 'Capture must remain excluded from Recents.'
    Assert-AndroidValue $capture.SelectNodes('intent-filter').Count 1 'Unexpected capture intent filter count.'
    Assert-AndroidNames @($capture.SelectNodes('intent-filter/action') | ForEach-Object { $_.GetAttribute('name') }) @('android.intent.action.SEND') 'Capture must accept only single shares.'
    Assert-AndroidNames @($capture.SelectNodes('intent-filter/category') | ForEach-Object { $_.GetAttribute('name') }) @('android.intent.category.DEFAULT') 'Unexpected share category.'
    $data = Get-AndroidNode $Manifest "/manifest/application/activity[@name='$package.QuickCaptureActivity']/intent-filter/data"
    Assert-AndroidValue $data.GetAttribute('mimeType') 'text/plain' 'Capture share MIME type broadened.'
    Assert-AndroidValue $data.Attributes.Count 1 'Capture share filter must not introduce URI routing.'
    foreach ($name in @('BackgroundGeofenceReceiver', 'QuickCaptureWidgetProvider')) {
        $node = Get-AndroidNode $Manifest "/manifest/application/receiver[@name='$package.$name']"
        Assert-AndroidValue $node.GetAttribute('exported') 'false' 'Device worker/widget receiver became exported.'
    }
    $widget = Get-AndroidNode $Manifest "/manifest/application/receiver[@name='$package.QuickCaptureWidgetProvider']"
    Assert-AndroidNames @($widget.SelectNodes('intent-filter/action') | ForEach-Object { $_.GetAttribute('name') }) @('android.appwidget.action.APPWIDGET_UPDATE') 'Widget receiver actions changed.'
    $lifecycle = Get-AndroidNode $Manifest "/manifest/application/receiver[@name='$package.BackgroundGeofenceLifecycleReceiver']"
    Assert-AndroidNames @($lifecycle.SelectNodes('intent-filter/action') | ForEach-Object { $_.GetAttribute('name') }) @('android.intent.action.BOOT_COMPLETED', 'android.intent.action.MY_PACKAGE_REPLACED') 'Geofence lifecycle actions changed.'
    $exported = New-Object 'System.Collections.Generic.List[string]'
    foreach ($component in $app.SelectNodes('activity|activity-alias|receiver|service|provider')) {
        $name = $component.GetAttribute('name')
        $flag = $component.GetAttribute('exported')
        if ($flag -notin @('', 'true', 'false')) { throw 'Unknown component exported value.' }
        if ($flag -eq '' -and $component.SelectNodes('intent-filter').Count -gt 0) { throw 'Filtered component lacks an explicit export boundary.' }
        if ($flag -ne 'true') { continue }
        if (($component.LocalName -eq 'activity' -and ($name -ceq $main[0].GetAttribute('name') -or $name -ceq "$package.QuickCaptureActivity")) -or
            ($component.LocalName -eq 'receiver' -and $name -ceq "$package.BackgroundGeofenceLifecycleReceiver")) {
            $exported.Add($name); continue
        }
        if ($component.LocalName -eq 'receiver' -and $name -cmatch '^crc[0-9a-f]+\.ReminderBootReceiver$') {
            Assert-AndroidNames @($component.SelectNodes('intent-filter/action') | ForEach-Object { $_.GetAttribute('name') }) @('android.intent.action.BOOT_COMPLETED') 'Reminder boot actions changed.'
            $exported.Add($name); continue
        }
        if ($component.LocalName -eq 'receiver' -and $name -ceq 'androidx.profileinstaller.ProfileInstallReceiver' -and $component.GetAttribute('permission') -ceq 'android.permission.DUMP') {
            Assert-AndroidNames @($component.SelectNodes('intent-filter/action') | ForEach-Object { $_.GetAttribute('name') }) @('androidx.profileinstaller.action.INSTALL_PROFILE', 'androidx.profileinstaller.action.SKIP_FILE', 'androidx.profileinstaller.action.SAVE_PROFILE', 'androidx.profileinstaller.action.BENCHMARK_OPERATION') 'Profile installer action set changed.'
            $exported.Add($name); continue
        }
        throw 'Unexpected exported APK component.'
    }
    Assert-AndroidValue $main[0].GetAttribute('exported') 'true' 'Main Activity no longer exported.'
    Assert-AndroidValue $capture.GetAttribute('exported') 'true' 'Capture Activity no longer exported.'
    Assert-AndroidValue $lifecycle.GetAttribute('exported') 'true' 'Lifecycle receiver no longer exported.'
    return [pscustomobject]@{
        Package = $package; VersionCode = $root.GetAttribute('versionCode'); VersionName = $root.GetAttribute('versionName')
        MinSdk = 21; TargetSdk = 36; Configuration = $Configuration; Debuggable = ($Configuration -eq 'Debug')
        AllowBackup = $app.GetAttribute('allowBackup')
        FullBackupContent = $app.GetAttribute('fullBackupContent')
        DataExtractionRules = $app.GetAttribute('dataExtractionRules')
        UsesCleartextTraffic = $app.GetAttribute('usesCleartextTraffic')
        NetworkSecurityConfig = $app.GetAttribute('networkSecurityConfig')
        Permissions = $permissions; ExportedComponents = @($exported)
    }
}

function Assert-AndroidExclusionSet($Node, [string[]]$Domains) {
    Assert-AndroidNames @($Node.SelectNodes('*') | ForEach-Object { $_.LocalName }) @($Domains | ForEach-Object { 'exclude' }) 'Backup rules contain an include or unexpected child.'
    Assert-AndroidNames @($Node.SelectNodes('exclude') | ForEach-Object { $_.GetAttribute('domain') }) $Domains 'Backup domains are missing, duplicated or unexpected.'
    foreach ($rule in $Node.SelectNodes('exclude')) {
        Assert-AndroidValue $rule.GetAttribute('path') '.' 'Backup exclusion must cover the complete domain.'
        Assert-AndroidValue $rule.Attributes.Count 2 'Unexpected backup exclusion attribute.'
        Assert-AndroidValue $rule.SelectNodes('*').Count 0 'Unexpected nested backup rule.'
    }
}

function Assert-AndroidBackupRules([xml]$Rules, [ValidateSet('Legacy', 'Extraction')][string]$Mode, [bool]$DeviceDomains = $false) {
    $domains = @('root', 'file', 'database', 'sharedpref', 'external')
    if ($DeviceDomains -or $Mode -eq 'Extraction') { $domains += @('device_root', 'device_file', 'device_database', 'device_sharedpref') }
    if ($Mode -eq 'Legacy') {
        $root = Get-AndroidNode $Rules '/full-backup-content'
        Assert-AndroidValue $root.Attributes.Count 0 'Unexpected legacy backup root attribute.'
        Assert-AndroidExclusionSet $root $domains
    } else {
        $root = Get-AndroidNode $Rules '/data-extraction-rules'
        Assert-AndroidValue $root.Attributes.Count 0 'Unexpected extraction root attribute.'
        Assert-AndroidNames @($root.SelectNodes('*') | ForEach-Object { $_.LocalName }) @('cloud-backup', 'device-transfer') 'Extraction modes changed or are missing.'
        $cloud = Get-AndroidNode $Rules '/data-extraction-rules/cloud-backup'
        Assert-AndroidValue $cloud.GetAttribute('disableIfNoEncryptionCapabilities') 'true' 'Cloud encryption condition changed.'
        Assert-AndroidValue $cloud.Attributes.Count 1 'Unexpected cloud backup attribute.'
        $transfer = Get-AndroidNode $Rules '/data-extraction-rules/device-transfer'
        Assert-AndroidValue $transfer.Attributes.Count 0 'Unexpected device transfer attribute.'
        Assert-AndroidExclusionSet $cloud $domains
        Assert-AndroidExclusionSet $transfer $domains
    }
}

function Assert-AndroidNetworkRules([xml]$Rules) {
    $root = Get-AndroidNode $Rules '/network-security-config'
    Assert-AndroidValue $root.Attributes.Count 0 'Unexpected network configuration attribute.'
    Assert-AndroidNames @($root.SelectNodes('*') | ForEach-Object { $_.LocalName }) @('base-config') 'Network policy contains domain/debug overrides.'
    $base = Get-AndroidNode $Rules '/network-security-config/base-config'
    Assert-AndroidValue $base.GetAttribute('cleartextTrafficPermitted') 'false' 'Network policy allows cleartext.'
    Assert-AndroidValue $base.Attributes.Count 1 'Unexpected base network configuration attribute.'
    Assert-AndroidNames @($base.SelectNodes('*') | ForEach-Object { $_.LocalName }) @('trust-anchors') 'Network trust structure changed.'
    $anchors = Get-AndroidNode $Rules '/network-security-config/base-config/trust-anchors'
    Assert-AndroidValue $anchors.Attributes.Count 0 'Unexpected trust anchor attribute.'
    Assert-AndroidNames @($anchors.SelectNodes('*') | ForEach-Object { $_.LocalName }) @('certificates') 'Network certificate sources changed.'
    $certificates = Get-AndroidNode $Rules '/network-security-config/base-config/trust-anchors/certificates'
    Assert-AndroidValue $certificates.GetAttribute('src') 'system' 'Network trust must use system certificates only.'
    Assert-AndroidValue $certificates.Attributes.Count 1 'Unexpected certificate trust attribute.'
    Assert-AndroidValue $certificates.SelectNodes('*').Count 0 'Unexpected nested certificate rule.'
}

function Get-AndroidSignerFingerprint([string[]]$Lines, [string]$ExpectedSha256) {
    $digests = @($Lines | Where-Object { $_ -cmatch '^Signer #1 certificate SHA-256 digest: [0-9a-fA-F]{64}$' })
    if ($digests.Count -ne 1) { throw 'Expected exactly one signer certificate SHA-256 digest.' }
    $fingerprint = ($digests[0] -split ': ', 2)[1].ToUpperInvariant()
    if ($ExpectedSha256) {
        if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'Expected signer fingerprint must contain 64 hexadecimal characters.' }
        Assert-AndroidValue $fingerprint $ExpectedSha256.ToUpperInvariant() 'Signer certificate does not match the supplied fingerprint.'
    }
    return $fingerprint
}

function Assert-AndroidBuildEvidence($Evidence, [ValidateSet('Debug', 'Release')][string]$Configuration, [string]$ApkSha256) {
    $Configuration = if ($Configuration -ieq 'Release') { 'Release' } else { 'Debug' }
    Assert-AndroidValue $Evidence.Configuration $Configuration 'Build settings configuration does not match the audit.'
    Assert-AndroidValue $Evidence.ApkSha256 $ApkSha256 'Build settings belong to another APK.'
    Assert-AndroidValue $Evidence.Properties.AndroidKeyStore 'false' 'Evaluation helper must use development signing.'
    Assert-AndroidValue $Evidence.Properties.RuntimeIdentifiers 'android-arm64;android-x64' 'Unexpected Android runtime selection.'
    if ($Configuration -eq 'Release') {
        foreach ($property in @('RunAOTCompilation', 'AndroidEnableProfiledAot', 'PublishTrimmed', 'AndroidUseAssemblyStore', 'UseMonoRuntime', 'JsonSerializerIsReflectionEnabledByDefault')) {
            Assert-AndroidValue $Evidence.Properties.$property 'true' 'Release evaluation defaults changed; review build/runtime compatibility.'
        }
        Assert-AndroidValue $Evidence.Properties.TrimMode 'partial' 'Release trimming mode changed.'
    }
}

function Get-AndroidAssemblyLayout([string[]]$NonEmptyEntries, [ValidateSet('Debug', 'Release')][string]$Configuration) {
    $required = @()
    foreach ($abi in @('arm64-v8a', 'x86_64')) {
        if ($Configuration -eq 'Release') { $required += "lib/$abi/libassemblies.$abi.blob.so" }
        foreach ($assembly in @('TodoApp.Maui', 'TodoApp.Domain', 'TodoApp.Infrastructure')) {
            if ($Configuration -eq 'Debug') { $required += "lib/$abi/lib_$assembly.dll.so" }
            else { $required += "lib/$abi/libaot-$assembly.dll.so" }
        }
    }
    foreach ($entry in $required) {
        if ($NonEmptyEntries -cnotcontains $entry) { throw 'Required nonempty assembly/AOT payload is missing for the selected configuration/ABI.' }
    }
    $format = 'Individual ELF-wrapped app/domain/infrastructure assemblies'
    if ($Configuration -eq 'Release') { $format = 'Mono assembly-store containers and app/domain/infrastructure AOT modules; individual managed store entries not decoded' }
    return [pscustomobject]@{ Format = $format; Abis = @('arm64-v8a', 'x86_64'); CheckedEntries = $required }
}

Export-ModuleMember -Function ConvertFrom-AaptXmlTree, Get-AndroidNode, Assert-AndroidValue, Assert-AndroidNames, Assert-AndroidManifest, Assert-AndroidBackupRules, Assert-AndroidNetworkRules, Get-AndroidSignerFingerprint, Assert-AndroidBuildEvidence, Get-AndroidAssemblyLayout
