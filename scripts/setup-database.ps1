$ErrorActionPreference = 'Stop'
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'EF tool restore failed.' }
dotnet restore
if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }
dotnet ef database update
if ($LASTEXITCODE -ne 0) { throw 'Database migration failed.' }
