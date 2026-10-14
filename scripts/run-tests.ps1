Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repoRoot
try {
    Write-Host "Running Lindrake unit tests..."
    dotnet test tests/Lindrake.UnitTests/Lindrake.UnitTests.csproj -f net10.0

    Write-Host "Running Lindrake integration tests..."
    dotnet test tests/Lindrake.IntegrationTests/Lindrake.IntegrationTests.csproj -f net10.0
}
finally {
    Pop-Location
}
