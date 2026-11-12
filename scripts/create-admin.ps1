param([Parameter(Mandatory=$true)][string]$Email)
$ErrorActionPreference = 'Stop'
$secret = Read-Host 'New administrator password (8+ characters, upper/lowercase, number, symbol)' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
try {
    $env:TENDER_ADMIN_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    dotnet run --no-launch-profile -- --create-admin $Email
    if ($LASTEXITCODE -ne 0) { throw 'Administrator creation failed.' }
} finally {
    Remove-Item Env:TENDER_ADMIN_PASSWORD -ErrorAction SilentlyContinue
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}
