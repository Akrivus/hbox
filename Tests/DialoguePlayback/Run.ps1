$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $projectRoot
try {
    dotnet run --project Tests/DialoguePlayback/DialoguePlaybackTests.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Dialogue playback regression tests failed.' }
} finally { Pop-Location }
