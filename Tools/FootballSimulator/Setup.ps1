param(
    [string]$Package,
    [string]$ProjectRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent),
    [switch]$Verify,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$taskArguments = @((Join-Path $PSScriptRoot 'setup.py'), '--project-root', $ProjectRoot)
if ($Verify) { $taskArguments += '--verify' }
elseif ([string]::IsNullOrWhiteSpace($Package)) { throw 'Pass -Package with the original .unitypackage path, or use -Verify.' }
else { $taskArguments += @('--package', $Package) }
if ($DryRun) { $taskArguments += '--dry-run' }
python @taskArguments
if ($LASTEXITCODE -ne 0) { throw 'FootballSimulator setup failed; see the diagnostic above.' }
