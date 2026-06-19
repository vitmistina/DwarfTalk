param(
    [Parameter(Mandatory = $true)]
    [string]$RepoRoot,

    [string]$DfHackHackPath = "C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack"
)

$ErrorActionPreference = "Stop"

$SourceScriptDir = Join-Path $DfHackHackPath "scripts\fortress-souls"
$TargetScriptDir = Join-Path $RepoRoot "dfhack\scripts\fortress-souls"

New-Item -ItemType Directory -Force -Path $TargetScriptDir | Out-Null

$ScriptNames = @(
    "diagnose.lua",
    "list-dwarves.lua",
    "get-dwarf-snapshot.lua"
)

foreach ($ScriptName in $ScriptNames) {
    Copy-Item (Join-Path $SourceScriptDir $ScriptName) (Join-Path $TargetScriptDir $ScriptName) -Force
}

Write-Host "Copied validated DFHack scripts into:"
Write-Host $TargetScriptDir
Write-Host ""
Write-Host "Docs live under docs/, scripts under dfhack/scripts/fortress-souls/, and samples under dfhack/samples/."
