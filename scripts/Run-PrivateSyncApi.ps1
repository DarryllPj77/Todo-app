# Starts only the loopback API. Tailscale certificate issuance and Serve mapping are separate steps.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ts = Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'
$tlsDirectory = Join-Path $env:LOCALAPPDATA 'TodoApp\DevelopmentTls'
$certPath = Join-Path $tlsDirectory 'todoapp-private-sync.crt'
$keyPath = Join-Path $tlsDirectory 'todoapp-private-sync.key'
if (-not (Test-Path -LiteralPath $ts)) { throw 'Install and sign in to the approved Tailscale network first.' }
if (-not ((Test-Path -LiteralPath $certPath) -and (Test-Path -LiteralPath $keyPath))) {
    throw 'The private-sync certificate/key files are missing. Complete the approved certificate setup first.'
}
$raw = & $ts status --json
if ($LASTEXITCODE -ne 0) { throw 'Could not read Tailscale status.' }
$state = ($raw -join "`n") | ConvertFrom-Json
if ($state.BackendState -ne 'Running' -or -not $state.Self.Online) { throw 'Connect Tailscale before starting this endpoint.' }
$dnsName = ([string]$state.Self.DNSName).TrimEnd('.')
if ($dnsName -notmatch '^[a-z0-9-]+\.[a-z0-9.-]+\.ts\.net$') { throw 'Unexpected private-network DNS name.' }
$cert = New-Object Security.Cryptography.X509Certificates.X509Certificate2($certPath)
try {
    if ($cert.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false) -ne $dnsName -or
        $cert.NotBefore.ToUniversalTime() -gt [DateTime]::UtcNow -or $cert.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) {
        throw 'Certificate hostname or validity does not match this connected device. Renew/review it; validation will not be bypassed.'
    }
} finally { $cert.Dispose() }

$settings = @{
    ASPNETCORE_ENVIRONMENT = 'Development'
    ASPNETCORE_URLS = 'https://127.0.0.1:7115'
    Kestrel__Certificates__Default__Path = $certPath
    Kestrel__Certificates__Default__KeyPath = $keyPath
}
$previous = @{}
try {
    foreach ($name in $settings.Keys) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
    }
    Write-Host ('Private client origin: https://' + $dnsName + '/')
    Write-Host 'Kestrel binds HTTPS loopback port 7115 only. Keep this terminal running; Ctrl+C stops it.'
    & dotnet run --project (Join-Path $root 'src\TodoApp.Api') --no-build --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw 'Private API startup/run failed. Build the API and inspect configuration before retrying.' }
} finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
