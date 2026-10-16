$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $projectRoot
try {
    $jsonDll = Get-ChildItem 'Library/PackageCache/com.unity.nuget.newtonsoft-json*/Runtime/Newtonsoft.Json.dll' | Select-Object -First 1
    if (!$jsonDll) { throw 'Open the Unity project once to resolve its Newtonsoft.Json package.' }
    dotnet run --project Tests/Soccer/SoccerTests.csproj "-p:NewtonsoftPath=$($jsonDll.FullName)"
    if ($LASTEXITCODE -ne 0) { throw 'Soccer roster regression tests failed.' }
    python Tests/Soccer/SeedPatchTests.py
    if ($LASTEXITCODE -ne 0) { throw 'Soccer vendor seed patch tests failed.' }
    python Tests/Soccer/VendorBundleTests.py
    if ($LASTEXITCODE -ne 0) { throw 'Soccer complete vendor bundle tests failed.' }
} finally { Pop-Location }
