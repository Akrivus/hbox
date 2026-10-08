$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $projectRoot
try {
    dotnet run --project Tests/UIVisibility/UIVisibilityTests.csproj
    if ($LASTEXITCODE -ne 0) { throw 'UI visibility regression tests failed.' }
} finally { Pop-Location }
