# DFHack B-019 manual validation runbook

## Purpose

Validate the two read-only DFHack scripts used by Fortress Souls v0.1:

```text
fortress-souls/list-dwarves
fortress-souls/get-dwarf-snapshot
```

## Assumptions

- Dwarf Fortress is running.
- DFHack is running.
- A fortress map is loaded.
- Scripts are installed under:

```text
C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\scripts\fortress-souls\
```

## Validate dwarf list

```powershell
cd "C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack"

$out = "$env:TEMP\fortress-souls-list.json"
$err = "$env:TEMP\fortress-souls-list.err.txt"

.\dfhack-run.exe fortress-souls/list-dwarves > $out 2> $err
$exit = $LASTEXITCODE

"EXIT=$exit"
Get-Content $out -Raw | ConvertFrom-Json | Select-Object schemaVersion, count, mapLoaded, siteLoaded, worldLoaded
Get-Content $err -Raw
```

Expected:

```text
EXIT=0
schemaVersion = fortress-souls-dwarf-list.v0.1
count > 0
```

## Validate selected dwarf snapshot

Replace `6597` with a valid unit id from the list.

```powershell
cd "C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack"

$unitId = "6597"
$out = "$env:TEMP\fortress-souls-snapshot-$unitId.json"
$err = "$env:TEMP\fortress-souls-snapshot-$unitId.err.txt"

.\dfhack-run.exe fortress-souls/get-dwarf-snapshot $unitId > $out 2> $err
$exit = $LASTEXITCODE

"EXIT=$exit"

$snapshot = Get-Content $out -Raw | ConvertFrom-Json

$snapshot |
  Select-Object schemaVersion, requestedUnitId, soulPresent, mapLoaded, siteLoaded, worldLoaded |
  Format-List

$snapshot.identity | Format-List
$snapshot.promptCandidates.topSkills | Select-Object -First 5 token, effective, totalExperience | Format-Table -AutoSize
$snapshot.promptCandidates.extremeTraits | Select-Object -First 8 token, value, deviationFromNeutral50, polarity | Format-Table -AutoSize

Get-Content $err -Raw
```

Expected:

```text
EXIT=0
schemaVersion = fortress-souls-dwarf-snapshot.v0.1
soulPresent = True
```

## Failure handling

If `ConvertFrom-Json` fails, inspect raw stdout:

```powershell
Get-Content $out -Raw
```

DFHack/Lua stack traces can appear on stdout. The backend must treat stdout as untrusted until JSON parsing succeeds.

## Validated run

The B-019 validation run on 2026-06-19 produced:

```text
ListCount            : 7
ValidSnapshotCount   : 7
ErrorSnapshotCount   : 0
InvalidSnapshotCount : 0
```
