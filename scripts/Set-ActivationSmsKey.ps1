param([Security.SecureString]$ApiKey)

$ErrorActionPreference = 'Stop'
if ($null -eq $ApiKey) {
    $ApiKey = Read-Host 'Melipayamak console API key' -AsSecureString
}
if ($ApiKey.Length -eq 0) { throw 'The SMS key is empty.' }

if ($PSVersionTable.PSEdition -eq 'Desktop') {
    Add-Type -AssemblyName System.Security
}
$folder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'QeshmNobatGUI'
[IO.Directory]::CreateDirectory($folder) | Out-Null
$path = Join-Path $folder 'activation-sms.dat'
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($ApiKey)
try {
    $keyBytes = [Text.Encoding]::UTF8.GetBytes([Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer).Trim())
    if ($keyBytes.Length -eq 0) { throw 'The SMS key is empty.' }
    $purpose = [Text.Encoding]::UTF8.GetBytes('NobatQeshm.SmsKey.v1')
    $protected = [Security.Cryptography.ProtectedData]::Protect($keyBytes, $purpose, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes($path, $protected)
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    if ($null -ne $keyBytes) { [Array]::Clear($keyBytes, 0, $keyBytes.Length) }
}
Write-Output 'SMS key saved for the current Windows account.'
