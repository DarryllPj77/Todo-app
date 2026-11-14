$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
Import-Module (Join-Path $PSScriptRoot '../scripts/AndroidPackageAudit.psm1') -Force -DisableNameChecking
$package = 'com.companyname.todoapp.maui'
[xml]$baseline = @"
<manifest package="$package">
  <uses-sdk minSdkVersion="21" targetSdkVersion="36" />
  <permission name="$package.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION" protectionLevel="0x00000002" />
  <application debuggable="true" allowBackup="false" usesCleartextTraffic="false" fullBackupContent="@0x12345670" dataExtractionRules="@0x12345671" networkSecurityConfig="@0x12345672">
    <activity name="crc123abc.MainActivity" exported="true">
      <intent-filter><action name="android.intent.action.MAIN"/><category name="android.intent.category.LAUNCHER"/></intent-filter>
      <meta-data name="android.app.shortcuts" resource="@0x12345678"/>
    </activity>
    <activity name="$package.QuickCaptureActivity" exported="true" taskAffinity="" excludeFromRecents="true">
      <intent-filter><action name="android.intent.action.SEND"/><category name="android.intent.category.DEFAULT"/><data mimeType="text/plain"/></intent-filter>
    </activity>
    <receiver name="$package.BackgroundGeofenceReceiver" exported="false" />
    <receiver name="$package.QuickCaptureWidgetProvider" exported="false">
      <intent-filter><action name="android.appwidget.action.APPWIDGET_UPDATE"/></intent-filter>
    </receiver>
    <receiver name="$package.BackgroundGeofenceLifecycleReceiver" exported="true">
      <intent-filter><action name="android.intent.action.BOOT_COMPLETED"/><action name="android.intent.action.MY_PACKAGE_REPLACED"/></intent-filter>
    </receiver>
  </application>
</manifest>
"@
foreach ($name in @('ACCESS_NETWORK_STATE', 'INTERNET', 'POST_NOTIFICATIONS', 'ACCESS_COARSE_LOCATION', 'ACCESS_FINE_LOCATION', 'ACCESS_BACKGROUND_LOCATION', 'RECEIVE_BOOT_COMPLETED')) {
    $node = $baseline.CreateElement('uses-permission'); $node.SetAttribute('name', "android.permission.$name"); [void]$baseline.DocumentElement.AppendChild($node)
}
$node = $baseline.CreateElement('uses-permission'); $node.SetAttribute('name', "$package.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION"); [void]$baseline.DocumentElement.AppendChild($node)
$null = Assert-AndroidManifest $baseline
$script:passed = 1

function Test-Rejected([string]$Name, [scriptblock]$Change) {
    $copy = $baseline.CloneNode($true)
    & $Change $copy
    $rejected = $false
    try { $null = Assert-AndroidManifest $copy } catch { $rejected = $true }
    if (!$rejected) { throw "Audit accepted unsafe drift: $Name" }
    $script:passed++
}
Test-Rejected 'unknown exported receiver' { param($d) $n=$d.CreateElement('receiver'); $n.SetAttribute('name','foreign.Receiver'); $n.SetAttribute('exported','true'); [void]$d.SelectSingleNode('/manifest/application').AppendChild($n) }
Test-Rejected 'backup enabled' { param($d) $d.SelectSingleNode('//application').SetAttribute('allowBackup','true') }
Test-Rejected 'cleartext enabled' { param($d) $d.SelectSingleNode('//application').SetAttribute('usesCleartextTraffic','true') }
Test-Rejected 'backup policy unlinked' { param($d) $d.SelectSingleNode('//application').RemoveAttribute('fullBackupContent') }
Test-Rejected 'extraction policy unlinked' { param($d) $d.SelectSingleNode('//application').RemoveAttribute('dataExtractionRules') }
Test-Rejected 'network policy unlinked' { param($d) $d.SelectSingleNode('//application').RemoveAttribute('networkSecurityConfig') }
Test-Rejected 'custom backup agent' { param($d) $d.SelectSingleNode('//application').SetAttribute('backupAgent','foreign.Agent') }
Test-Rejected 'unprotected profile receiver' { param($d) $n=$d.CreateElement('receiver'); $n.SetAttribute('name','androidx.profileinstaller.ProfileInstallReceiver'); $n.SetAttribute('exported','true'); [void]$d.SelectSingleNode('/manifest/application').AppendChild($n) }
Test-Rejected 'shortcut metadata detached from main Activity' { param($d) $n=$d.SelectSingleNode('//meta-data'); [void]$n.ParentNode.RemoveChild($n) }
Test-Rejected 'alias impersonates capture' { param($d) $n=$d.CreateElement('activity-alias'); $n.SetAttribute('name',"$package.QuickCaptureActivity"); $n.SetAttribute('exported','true'); [void]$d.SelectSingleNode('/manifest/application').AppendChild($n) }
Test-Rejected 'exported worker' { param($d) $d.SelectSingleNode("//receiver[@name='$package.BackgroundGeofenceReceiver']").SetAttribute('exported','true') }
Test-Rejected 'exported widget' { param($d) $d.SelectSingleNode("//receiver[@name='$package.QuickCaptureWidgetProvider']").SetAttribute('exported','true') }
Test-Rejected 'widget action broadening' { param($d) $d.SelectSingleNode("//receiver[@name='$package.QuickCaptureWidgetProvider']/intent-filter/action").SetAttribute('name','android.intent.action.VIEW') }
Test-Rejected 'missing explicit export' { param($d) $d.SelectSingleNode("//receiver[@name='$package.QuickCaptureWidgetProvider']").RemoveAttribute('exported') }
Test-Rejected 'share MIME wildcard' { param($d) $d.SelectSingleNode('//data').SetAttribute('mimeType','text/*') }
Test-Rejected 'share URI routing' { param($d) $d.SelectSingleNode('//data').SetAttribute('scheme','https') }
Test-Rejected 'multiple sharing' { param($d) $d.SelectSingleNode("//activity[@name='$package.QuickCaptureActivity']/intent-filter/action").SetAttribute('name','android.intent.action.SEND_MULTIPLE') }
Test-Rejected 'permission expansion' { param($d) $d.SelectSingleNode('//uses-permission').SetAttribute('name','android.permission.READ_CONTACTS') }
Test-Rejected 'signature permission weakened' { param($d) $d.SelectSingleNode('//permission').SetAttribute('protectionLevel','0') }
Test-Rejected 'unreviewed target SDK' { param($d) $d.SelectSingleNode('//uses-sdk').SetAttribute('targetSdkVersion','37') }
Test-Rejected 'wrong package' { param($d) $d.DocumentElement.SetAttribute('package','another.app') }
Test-Rejected 'release candidate mistaken for debug' { param($d) $d.SelectSingleNode('//application').SetAttribute('debuggable','false') }
Test-Rejected 'missing empty affinity' { param($d) $d.SelectSingleNode("//activity[@name='$package.QuickCaptureActivity']").RemoveAttribute('taskAffinity') }
Test-Rejected 'unexpected lifecycle action' { param($d) $d.SelectSingleNode("//receiver[@name='$package.BackgroundGeofenceLifecycleReceiver']/intent-filter/action").SetAttribute('name','foreign.ACTION') }

$tree = @'
N: android=http://schemas.android.com/apk/res/android (line=1)
  E: manifest (line=1)
    A: package="example.app" (Raw: "example.app")
      E: application (line=2)
        A: http://schemas.android.com/apk/res/android:debuggable(0x0101000f)=true
          E: receiver (line=3)
            A: http://schemas.android.com/apk/res/android:name(0x01010003)="first.Receiver" (Raw: "first.Receiver")
          E: receiver (line=4)
            A: http://schemas.android.com/apk/res/android:name(0x01010003)="second.Receiver" (Raw: "second.Receiver")
'@ -split "`n"
$parsed = ConvertFrom-AaptXmlTree $tree
Assert-AndroidValue $parsed.SelectNodes('//receiver').Count 2 'Parser lost sibling component boundaries.'
Assert-AndroidValue $parsed.SelectSingleNode('//receiver[2]').GetAttribute('name') 'second.Receiver' 'Parser associated attributes with the wrong node.'
$script:passed++
foreach ($badTree in @(@('A: name="orphan"'), @('E: root', '  A: name="one"', '  A: name="two"'), @('E: root', '  unsupported-output'))) {
    $rejected = $false
    try { $null = ConvertFrom-AaptXmlTree $badTree } catch { $rejected = $true }
    if (!$rejected) { throw 'Malformed aapt2 output was accepted.' }
    $script:passed++
}
$resources = Join-Path $PSScriptRoot '../src/TodoApp.Maui/Platforms/Android/Resources'
[xml]$legacy = Get-Content -LiteralPath (Join-Path $resources 'xml/backup_rules.xml') -Raw
[xml]$deviceLegacy = Get-Content -LiteralPath (Join-Path $resources 'xml-v24/backup_rules.xml') -Raw
[xml]$extraction = Get-Content -LiteralPath (Join-Path $resources 'xml/data_extraction_rules.xml') -Raw
[xml]$network = Get-Content -LiteralPath (Join-Path $resources 'xml/network_security_config.xml') -Raw
Assert-AndroidBackupRules $legacy 'Legacy'
Assert-AndroidBackupRules $deviceLegacy 'Legacy' $true
Assert-AndroidBackupRules $extraction 'Extraction'
Assert-AndroidNetworkRules $network
$script:passed += 4

function Test-PolicyRejected([string]$Name, [xml]$Document, [scriptblock]$Change, [scriptblock]$Validate) {
    $copy = $Document.CloneNode($true)
    & $Change $copy
    $rejected = $false
    try { & $Validate $copy } catch { $rejected = $true }
    if (!$rejected) { throw "Backup/transport audit accepted unsafe drift: $Name" }
    $script:passed++
}
$checkLegacy = { param($d) Assert-AndroidBackupRules $d 'Legacy' }
$checkDeviceLegacy = { param($d) Assert-AndroidBackupRules $d 'Legacy' $true }
$checkExtraction = { param($d) Assert-AndroidBackupRules $d 'Extraction' }
$checkNetwork = { param($d) Assert-AndroidNetworkRules $d }
Test-PolicyRejected 'missing secret preferences exclusion' $legacy { param($d) $n=$d.SelectSingleNode('//exclude[@domain="sharedpref"]'); [void]$n.ParentNode.RemoveChild($n) } $checkLegacy
Test-PolicyRejected 'narrow task backup exclusion' $legacy { param($d) $d.SelectSingleNode('//exclude[@domain="file"]').SetAttribute('path','one-task.db3') } $checkLegacy
Test-PolicyRejected 'backup include' $legacy { param($d) [void]$d.DocumentElement.AppendChild($d.CreateElement('include')) } $checkLegacy
Test-PolicyRejected 'missing device protected secrets' $deviceLegacy { param($d) $n=$d.SelectSingleNode('//exclude[@domain="device_sharedpref"]'); [void]$n.ParentNode.RemoveChild($n) } $checkDeviceLegacy
Test-PolicyRejected 'missing cloud task exclusion' $extraction { param($d) $n=$d.SelectSingleNode('//cloud-backup/exclude[@domain="database"]'); [void]$n.ParentNode.RemoveChild($n) } $checkExtraction
Test-PolicyRejected 'missing transfer secret exclusion' $extraction { param($d) $n=$d.SelectSingleNode('//device-transfer/exclude[@domain="sharedpref"]'); [void]$n.ParentNode.RemoveChild($n) } $checkExtraction
Test-PolicyRejected 'transfer mode omitted' $extraction { param($d) $n=$d.SelectSingleNode('//device-transfer'); [void]$n.ParentNode.RemoveChild($n) } $checkExtraction
Test-PolicyRejected 'weakened cloud encryption condition' $extraction { param($d) $d.SelectSingleNode('//cloud-backup').SetAttribute('disableIfNoEncryptionCapabilities','false') } $checkExtraction
Test-PolicyRejected 'unreviewed transfer mode' $extraction { param($d) [void]$d.DocumentElement.AppendChild($d.CreateElement('cross-platform-transfer')) } $checkExtraction
Test-PolicyRejected 'network cleartext exception' $network { param($d) $d.SelectSingleNode('//base-config').SetAttribute('cleartextTrafficPermitted','true') } $checkNetwork
Test-PolicyRejected 'user certificate trust' $network { param($d) $d.SelectSingleNode('//certificates').SetAttribute('src','user') } $checkNetwork
Test-PolicyRejected 'debug certificate override' $network { param($d) [void]$d.DocumentElement.AppendChild($d.CreateElement('debug-overrides')) } $checkNetwork
Test-PolicyRejected 'domain override' $network { param($d) [void]$d.DocumentElement.AppendChild($d.CreateElement('domain-config')) } $checkNetwork
Write-Output "Android audit policy/parser tests passed: $script:passed"
