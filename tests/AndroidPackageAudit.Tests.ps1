$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
Import-Module (Join-Path $PSScriptRoot '../scripts/AndroidPackageAudit.psm1') -Force -DisableNameChecking
$package = 'com.companyname.todoapp.maui'
[xml]$baseline = @"
<manifest package="$package">
  <uses-sdk minSdkVersion="21" targetSdkVersion="36" />
  <permission name="$package.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION" protectionLevel="0x00000002" />
  <application debuggable="true" allowBackup="true">
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
Write-Output "Android audit policy/parser tests passed: $script:passed"
