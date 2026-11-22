param(
    [string]$InterfaceAlias = 'Wi-Fi',
    [int]$HttpPort = 7180,
    [int]$HttpsPort = 7181
)

$ErrorActionPreference = 'Stop'
$address = Get-NetIPConfiguration -InterfaceAlias $InterfaceAlias |
    Where-Object IPv4DefaultGateway |
    ForEach-Object IPv4Address |
    Select-Object -First 1 -ExpandProperty IPAddress
if (-not $address) { throw "No IPv4 address with a gateway found on $InterfaceAlias." }

$origin = "http://${address}:$HttpPort"
$project = Join-Path $PSScriptRoot '../SourcingTenderManagement.CodeFirst.csproj'
Write-Host "Phone on the same Wi-Fi: $origin"
Write-Host "On this PC: https://localhost:$HttpsPort"
dotnet run --project $project --no-launch-profile -- --environment Development --urls "https://localhost:$HttpsPort;$origin" "--Links:PublicOrigin=$origin" "--Links:PhonePreviewHttpPort=$HttpPort"
exit $LASTEXITCODE
