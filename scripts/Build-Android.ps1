param([switch]$InstallDependencies)

$ErrorActionPreference = 'Stop'
$solutionRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $solutionRoot '.tools\dotnet\dotnet.exe'
$projectPath = Join-Path $solutionRoot 'src\TodoApp.Maui\TodoApp.Maui.csproj'
$androidSdk = Join-Path $solutionRoot '.tools\android-sdk'
$javaSdk = Join-Path $solutionRoot '.tools\jdk'
$downloadCache = Join-Path $solutionRoot '.tools\android-downloads'
$artifacts = Join-Path $solutionRoot '.tools\android-build'
$previousDotnetRoot = $env:DOTNET_ROOT
try {
    if (Test-Path -LiteralPath $localDotnet) {
        $dotnetCommand = $localDotnet
        $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
    } else { $dotnetCommand = 'dotnet' }
    $commonArguments = @($projectPath, '-f', 'net10.0-android', '-p:TodoAppTargetFrameworks=net10.0-android',
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
    & $dotnetCommand build @commonArguments -t:SignAndroidPackage -p:AndroidPackageFormats=apk -p:EmbedAssembliesIntoApk=true
    if ($LASTEXITCODE -ne 0) { throw "Android build failed (exit $LASTEXITCODE). See docs/ANDROID_GEOFENCES.md." }
    Write-Output "Debug APK output: $artifacts. No device install, emulator launch or permission operation was performed."
} finally { $env:DOTNET_ROOT = $previousDotnetRoot }
