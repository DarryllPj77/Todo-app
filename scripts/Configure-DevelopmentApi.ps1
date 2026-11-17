# Run interactively on the development PC. Never supply passwords in chat or command arguments.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\TodoApp.Api\TodoApp.Api.csproj'
if ([string]::IsNullOrWhiteSpace($env:APPDATA)) {
    throw 'Run this Windows development helper from your local PowerShell terminal.'
}
[xml]$projectXml = [IO.File]::ReadAllText($project)
$secretId = [string]$projectXml.Project.PropertyGroup.UserSecretsId
if ([string]::IsNullOrWhiteSpace($secretId)) { throw 'API UserSecretsId is missing.' }
$secretFile = Join-Path $env:APPDATA ('Microsoft\UserSecrets\' + $secretId + '\secrets.json')
$existing = $null
if (Test-Path -LiteralPath $secretFile) {
    $existing = Get-Content -Raw -LiteralPath $secretFile | ConvertFrom-Json
}

function Test-ConfiguredSetting($Settings, [string]$FlatName, [string]$Section, [string]$Key) {
    if ($null -eq $Settings) { return $false }
    if (-not [string]::IsNullOrWhiteSpace([string]$Settings.$FlatName)) { return $true }
    return -not [string]::IsNullOrWhiteSpace([string]$Settings.$Section.$Key)
}

$updates = @{}
$password = $null
$passwordPointer = [IntPtr]::Zero
$connection = $null
try {
    Write-Host 'Development user-secrets are stored outside Git, but are NOT encrypted.'
    Write-Host 'This helper configures an existing PostgreSQL database on this PC only.'
    if (-not (Test-ConfiguredSetting $existing 'ConnectionStrings:TodoDatabase' 'ConnectionStrings' 'TodoDatabase')) {
        $database = (Read-Host 'Existing PostgreSQL database name (for example todoapp)').Trim()
        $username = (Read-Host 'PostgreSQL role for that database (for example todoapp_dev)').Trim()
        $portText = Read-Host 'Local PostgreSQL port [5432]'
        if ([string]::IsNullOrWhiteSpace($portText)) { $portText = '5432' }
        $portNumber = 0
        if (-not [int]::TryParse($portText, [ref]$portNumber) -or $portNumber -lt 1 -or $portNumber -gt 65535) {
            throw 'Enter a PostgreSQL port between 1 and 65535.'
        }
        if ([string]::IsNullOrWhiteSpace($database) -or [string]::IsNullOrWhiteSpace($username)) {
            throw 'Database name and role are required. No configuration was saved.'
        }
        $password = Read-Host 'PostgreSQL password (hidden)' -AsSecureString
        if ($password.Length -eq 0) { throw 'A database password is required. No configuration was saved.' }
        $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)
        $connection = New-Object System.Data.Common.DbConnectionStringBuilder
        $connection['Host'] = '127.0.0.1'
        $connection['Port'] = $portNumber
        $connection['Database'] = $database
        $connection['Username'] = $username
        $connection['Password'] = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
        $updates['ConnectionStrings:TodoDatabase'] = $connection.ConnectionString
    } else {
        Write-Host 'Preserving the existing database setting.'
    }
    if (-not (Test-ConfiguredSetting $existing 'Jwt:SigningKey' 'Jwt' 'SigningKey')) {
        $keyBytes = New-Object byte[] 32
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($keyBytes) } finally { $random.Dispose() }
        $updates['Jwt:SigningKey'] = [Convert]::ToBase64String($keyBytes)
        [Array]::Clear($keyBytes, 0, $keyBytes.Length)
    } else {
        Write-Host 'Preserving the existing JWT signing key.'
    }
    if ($updates.Count -gt 0) {
        # Use standard input so secrets do not appear in process command-line arguments.
        $updates | ConvertTo-Json -Compress | & dotnet user-secrets set --project $project
        if ($LASTEXITCODE -ne 0) { throw 'Could not save development user-secrets.' }
        Write-Host 'Development settings saved. Database authentication and migrations are NOT verified.'
    } else {
        Write-Host 'Both settings already exist; no changes made.'
    }
    if ($env:ConnectionStrings__TodoDatabase -or $env:Jwt__SigningKey) {
        Write-Warning 'Environment overrides are present and take precedence over user-secrets. They were not changed.'
    }
} finally {
    if ($passwordPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer) }
    if ($null -ne $password) { $password.Dispose() }
    if ($null -ne $connection) { $connection.Clear() }
    $updates.Clear()
    $existing = $null
}
