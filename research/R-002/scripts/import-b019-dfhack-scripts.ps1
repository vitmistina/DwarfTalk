param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [string]$DfHackHackPath = "C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack"
)

$ErrorActionPreference = "Stop"

$SourceScriptDir = Join-Path $DfHackHackPath "scripts\fortress-souls"
$TargetScriptDir = Join-Path $RepoRoot "dfhack\scripts\fortress-souls"

New-Item -ItemType Directory -Force -Path $TargetScriptDir | Out-Null

Copy-Item (Join-Path $SourceScriptDir "list-dwarves.lua") (Join-Path $TargetScriptDir "list-dwarves.lua") -Force
Copy-Item (Join-Path $SourceScriptDir "get-dwarf-snapshot.lua") (Join-Path $TargetScriptDir "get-dwarf-snapshot.lua") -Force

Write-Host "Copied validated DFHack scripts into:"
Write-Host $TargetScriptDir
Write-Host ""
Write-Host "Next, copy docs and samples from this artifact bundle into the repo root."
