param(
    [string]$ApkPath,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
Import-Module (Join-Path $PSScriptRoot 'AndroidPackageAudit.psm1') -Force -DisableNameChecking
$root = Split-Path -Parent $PSScriptRoot
if (!$ApkPath) { $ApkPath = Join-Path $root '.tools/android-build/bin/TodoApp.Maui/debug_net10.0-android/com.companyname.todoapp.maui-Signed.apk' }
if (!$ReportPath) { $ReportPath = Join-Path $root '.tools/android-audit/debug-report.json' }
$ApkPath = [IO.Path]::GetFullPath($ApkPath)
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
if ([IO.Path]::GetExtension($ApkPath) -ine '.apk' -or [IO.Path]::GetExtension($ReportPath) -ine '.json' -or
    [string]::Equals($ApkPath, $ReportPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an .apk input and a separate .json report. No input was changed.'
}
$aapt = Join-Path $root '.tools/android-sdk/build-tools/36.0.0/aapt2.exe'
$signer = Join-Path $root '.tools/android-sdk/build-tools/36.0.0/apksigner.bat'
$previousJavaHome = $env:JAVA_HOME
$report = [ordered]@{
    Schema = 1; CheckedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Scope = 'Android debug artifact baseline only'; ArtifactStatus = 'Failed'; ReleaseReady = $false
    ApkPath = $ApkPath; Sha256 = $null; Failure = $null
}

function Invoke-AuditTool([string]$Path, [string[]]$Arguments) {
    $previous = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $Path @Arguments 2>&1 | ForEach-Object { [string]$_ })
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) { throw "Android audit tool failed (exit $code): $([IO.Path]::GetFileName($Path))" }
    return $output
}

try {
    foreach ($path in @($ApkPath, $aapt, $signer, (Join-Path $root '.tools/jdk/bin/java.exe'))) {
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required audit input/tool is missing: $path" }
    }
    $env:JAVA_HOME = Join-Path $root '.tools/jdk'
    $report.Sha256 = (Get-FileHash -LiteralPath $ApkPath -Algorithm SHA256).Hash
    $report.AuditScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    $report.AuditPolicySha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'AndroidPackageAudit.psm1') -Algorithm SHA256).Hash
    $signature = @(Invoke-AuditTool $signer @('verify', '--verbose', $ApkPath))
    foreach ($scheme in @('v1', 'v2', 'v3')) {
        if (!($signature -cmatch "^Verified using $scheme scheme .*: true$")) { throw "Required APK signature scheme missing: $scheme" }
    }
    if (!($signature -cmatch '^Number of signers: 1$')) { throw 'Expected one APK signer.' }
    $report.Signatures = @('v1', 'v2', 'v3')
    $report.SignerTrust = 'Not established; no approved release certificate is pinned'
    $report.SignatureWarnings = @($signature | Where-Object { $_ -match '^WARNING:' }).Count
    $manifest = ConvertFrom-AaptXmlTree @(Invoke-AuditTool $aapt @('dump', 'xmltree', '--file', 'AndroidManifest.xml', $ApkPath))
    $report.Manifest = Assert-AndroidManifest $manifest
    $resourceIds = @{}
    foreach ($line in (Invoke-AuditTool $aapt @('dump', 'resources', $ApkPath))) {
        if ($line -match '^\s*resource (0x[0-9a-f]+) ([\w./]+)$') { $resourceIds[$Matches[2]] = '@' + $Matches[1] }
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($ApkPath)
    try { $entries = @($zip.Entries | Select-Object -ExpandProperty FullName) } finally { $zip.Dispose() }
    foreach ($abi in @('arm64-v8a', 'x86_64')) {
        foreach ($assembly in @('TodoApp.Maui', 'TodoApp.Domain', 'TodoApp.Infrastructure')) {
            if ($entries -cnotcontains "lib/$abi/lib_$assembly.dll.so") { throw 'Standalone APK lacks a required embedded assembly/ABI.' }
        }
    }
    $report.EmbeddedAssemblies = 'App/domain/infrastructure present for arm64-v8a and x86_64'
    $package = $report.Manifest.Package
    $widgetMetadata = Get-AndroidNode $manifest "/manifest/application/receiver[@name='$package.QuickCaptureWidgetProvider']/meta-data[@name='android.appwidget.provider']"
    Assert-AndroidValue $widgetMetadata.GetAttribute('resource') $resourceIds['xml/quick_capture_widget_info'] 'Widget provider metadata points to the wrong resource.'
    $shortcutMetadata = Get-AndroidNode $manifest "/manifest/application/activity/meta-data[@name='android.app.shortcuts']"
    Assert-AndroidValue $shortcutMetadata.GetAttribute('resource') $resourceIds['xml/quick_capture_shortcuts'] 'Launcher shortcut metadata changed.'
    foreach ($name in @('xml/quick_capture_widget_info', 'xml/quick_capture_shortcuts', 'layout/quick_capture_widget', 'drawable/quick_capture_widget_background', 'id/quick_capture_widget_button', 'string/quick_capture_widget_description')) {
        if (!$resourceIds.ContainsKey($name)) { throw 'Required capture resource is absent.' }
    }
    $checkedResources = New-Object 'System.Collections.Generic.List[string]'
    $modernWidget = $false
    $modernShortcut = $false
    foreach ($entry in $entries) {
        if ($entry -cmatch '^res/xml[^/]*/quick_capture_widget_info\.xml$') {
            $xml = ConvertFrom-AaptXmlTree @(Invoke-AuditTool $aapt @('dump', 'xmltree', '--file', $entry, $ApkPath))
            $provider = Get-AndroidNode $xml '/appwidget-provider'
            Assert-AndroidValue $provider.GetAttribute('updatePeriodMillis') '0' 'Widget periodic refresh became enabled.'
            Assert-AndroidValue $provider.GetAttribute('widgetCategory') '0x00000001' 'Widget category changed.'
            Assert-AndroidValue $provider.GetAttribute('initialLayout') $resourceIds['layout/quick_capture_widget'] 'Widget initial layout changed.'
            Assert-AndroidValue $provider.GetAttribute('minHeight') '64.000000dp' 'Widget minimum height changed.'
            if ($provider.HasAttribute('previewLayout')) {
                Assert-AndroidValue $provider.GetAttribute('previewLayout') $resourceIds['layout/quick_capture_widget'] 'Widget preview must remain generic.'
                Assert-AndroidValue $provider.GetAttribute('targetCellWidth') '2' 'Widget cell width changed.'
                Assert-AndroidValue $provider.GetAttribute('targetCellHeight') '1' 'Widget cell height changed.'
                $modernWidget = $true
            }
            $checkedResources.Add($entry)
        } elseif ($entry -cmatch '^res/xml[^/]*/quick_capture_shortcuts\.xml$') {
            $xml = ConvertFrom-AaptXmlTree @(Invoke-AuditTool $aapt @('dump', 'xmltree', '--file', $entry, $ApkPath))
            $intent = Get-AndroidNode $xml '/shortcuts/shortcut/intent'
            $shortcut = Get-AndroidNode $xml '/shortcuts/shortcut'
            Assert-AndroidValue $shortcut.GetAttribute('enabled') 'true' 'Launcher shortcut became disabled.'
            if ($shortcut.HasAttribute('shortcutId')) {
                Assert-AndroidValue $shortcut.GetAttribute('shortcutId') 'quick_capture' 'Launcher shortcut identity changed.'
                Assert-AndroidValue $shortcut.GetAttribute('shortcutShortLabel') $resourceIds['string/quick_capture_short_label'] 'Launcher shortcut label changed.'
                $modernShortcut = $true
            }
            Assert-AndroidValue $intent.GetAttribute('action') "$package.QUICK_CAPTURE" 'Launcher capture action changed.'
            Assert-AndroidValue $intent.GetAttribute('targetPackage') $package 'Launcher target package changed.'
            Assert-AndroidValue $intent.GetAttribute('targetClass') "$package.QuickCaptureActivity" 'Launcher capture target changed.'
            $checkedResources.Add($entry)
        }
    }
    if (!($checkedResources -cmatch '^res/xml[^/]*/quick_capture_widget_info\.xml$') -or !($checkedResources -cmatch '^res/xml[^/]*/quick_capture_shortcuts\.xml$')) { throw 'Packaged capture XML is missing.' }
    if (!$modernWidget -or !$modernShortcut) { throw 'Modern capture metadata is missing from the packaged resource variants.' }
    $layout = ConvertFrom-AaptXmlTree @(Invoke-AuditTool $aapt @('dump', 'xmltree', '--file', 'res/layout/quick_capture_widget.xml', $ApkPath))
    $button = Get-AndroidNode $layout '/FrameLayout/Button'
    Assert-AndroidValue $layout.SelectNodes('//*').Count 2 'Widget layout gained unexpected views.'
    Assert-AndroidValue $button.GetAttribute('id') $resourceIds['id/quick_capture_widget_button'] 'Widget click target changed.'
    Assert-AndroidValue $button.GetAttribute('contentDescription') $resourceIds['string/quick_capture_widget_description'] 'Widget accessible description changed.'
    Assert-AndroidValue $button.GetAttribute('text') $resourceIds['string/quick_capture_widget_button'] 'Widget text reference changed.'
    $report.CheckedResources = @($checkedResources)
    Assert-AndroidValue (Get-FileHash -LiteralPath $ApkPath -Algorithm SHA256).Hash $report.Sha256 'APK changed during audit; rebuild and rerun.'
    $report.OpenReleaseGates = @(
        'Development application ID/version and approved release signing identity'
        'Debug build only; release build, store/background-location review and distribution validation'
        'Backup/data-transfer policy and lost-key/restore acceptance; inspect recorded manifest attributes'
        'Explicit transport configuration review; HTTPS is enforced by existing client code'
        'Phone launcher/share/widget, lifecycle, offline/account and accessibility acceptance'
        'Real geofence/permission/notification/OEM/battery and provider acceptance'
        'Database upgrade/backup/recovery and secret redaction evidence'
    )
    $report.ArtifactStatus = 'Passed'
} catch {
    $report.Failure = $_.Exception.Message
    throw
} finally {
    $env:JAVA_HOME = $previousJavaHome
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($ReportPath))
    $temporaryReport = $ReportPath + '.tmp'
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $temporaryReport -Encoding UTF8
    Move-Item -LiteralPath $temporaryReport -Destination $ReportPath -Force
}
Write-Output "Android debug artifact audit passed. Release readiness remains BLOCKED. Report: $ReportPath"
