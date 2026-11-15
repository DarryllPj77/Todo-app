param(
    [switch]$InstallDependencies,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedSignerSha256
)

$ErrorActionPreference = 'Stop'
$Configuration = if ($Configuration -ieq 'Release') { 'Release' } else { 'Debug' }
$solutionRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $solutionRoot '.tools\dotnet\dotnet.exe'
$projectPath = Join-Path $solutionRoot 'src\TodoApp.Maui\TodoApp.Maui.csproj'
$androidSdk = Join-Path $solutionRoot '.tools\android-sdk'
$javaSdk = Join-Path $solutionRoot '.tools\jdk'
$downloadCache = Join-Path $solutionRoot '.tools\android-downloads'
$artifacts = Join-Path $solutionRoot '.tools\android-build'
if ($Configuration -eq 'Release') { $artifacts = Join-Path $solutionRoot '.tools\android-rel' }
$previousDotnetRoot = $env:DOTNET_ROOT
try {
    if (Test-Path -LiteralPath $localDotnet) {
        $dotnetCommand = $localDotnet
        $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
    } else { $dotnetCommand = 'dotnet' }
    # Android supplies its own RIDs. Do not infer a Windows host publish RID
    # merely because Release enables PublishTrimmed.
    $commonArguments = @($projectPath, '-f', 'net10.0-android', '-c', $Configuration, '-p:TodoAppTargetFrameworks=net10.0-android',
        '-p:UseDefaultPublishRuntimeIdentifier=false',
        "-p:AndroidSdkDirectory=$androidSdk", "-p:JavaSdkDirectory=$javaSdk", "-p:XamarinBuildDownloadDir=$downloadCache",
        '--artifacts-path', $artifacts, '--nologo', '-m:1')
    if ($InstallDependencies) {
        # Explicit opt-in downloads SDK/JDK and accepts their development licenses.
        & $dotnetCommand build @commonArguments -t:InstallAndroidDependencies -p:AcceptAndroidSDKLicenses=True
        if ($LASTEXITCODE -ne 0) { throw 'Android dependency installation failed. See docs/ANDROID_GEOFENCES.md.' }
    }
    if (!(Test-Path -LiteralPath (Join-Path $javaSdk 'bin\java.exe')) -or !(Test-Path -LiteralPath (Join-Path $androidSdk 'platform-tools'))) {
        throw 'Android SDK/JDK missing. Install the Android workload first, then run this script with -InstallDependencies to accept/install the required SDK/JDK dependencies.'
    }
    # Both profiles are evaluation builds using the SDK development keystore.
    # Never load a production keystore or signing password through this helper.
    & $dotnetCommand build @commonArguments -t:SignAndroidPackage -p:AndroidKeyStore=false -p:AndroidPackageFormats=apk -p:EmbedAssembliesIntoApk=true
    if ($LASTEXITCODE -ne 0) { throw "Android build failed (exit $LASTEXITCODE). See docs/ANDROID_GEOFENCES.md." }
    $apk = Join-Path $artifacts ("bin/TodoApp.Maui/{0}_net10.0-android/com.companyname.todoapp.maui-Signed.apk" -f $Configuration.ToLowerInvariant())
    $settingsJson = & $dotnetCommand msbuild $projectPath '-p:TargetFramework=net10.0-android' '-p:TodoAppTargetFrameworks=net10.0-android' '-p:UseDefaultPublishRuntimeIdentifier=false' "-p:Configuration=$Configuration" '-p:EmbedAssembliesIntoApk=true' '-p:AndroidKeyStore=false' '-getProperty:RuntimeIdentifiers,RunAOTCompilation,AndroidEnableProfiledAot,PublishTrimmed,TrimMode,JsonSerializerIsReflectionEnabledByDefault,AndroidUseAssemblyStore,UseMonoRuntime,AndroidKeyStore'
    if ($LASTEXITCODE -ne 0) { throw 'Could not record evaluated Android build settings.' }
    $settings = ($settingsJson -join "`n") | ConvertFrom-Json
    $evidence = [ordered]@{ Configuration = $Configuration; ApkSha256 = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash; Properties = $settings.Properties }
    $settingsPath = Join-Path $artifacts 'build-settings.json'
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    $auditArguments = @{ Configuration = $Configuration; ApkPath = $apk; BuildSettingsPath = $settingsPath }
    if ($ExpectedSignerSha256) { $auditArguments.ExpectedSignerSha256 = $ExpectedSignerSha256 }
    & (Join-Path $PSScriptRoot 'Test-AndroidPackage.ps1') @auditArguments
    Write-Output "$Configuration evaluation APK output: $artifacts. Development signing only; no install or publishing was performed."
} finally { $env:DOTNET_ROOT = $previousDotnetRoot }
