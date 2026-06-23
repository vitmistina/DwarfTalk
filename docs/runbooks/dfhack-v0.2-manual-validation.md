# DFHack v0.2 manual validation runbook

## Purpose

Validate the two read-only DFHack perception scripts used by Fortress Souls
v0.2:

```text
fortress-souls/get-dwarf-surroundings
fortress-souls/get-stock-summary
```

This is optional manual evidence for live mode. Fake mode remains the default
supported development path and the only required CI path.

## Assumptions

- Dwarf Fortress is running.
- DFHack is running.
- A fortress map is loaded.
- The validated repo scripts live under:

```text
dfhack/scripts/fortress-souls/
```

- Retained live product samples live under:

```text
dfhack/samples/perception/
```

- The DFHack runtime scripts directory is:

```text
C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\scripts\fortress-souls\
```

## Manual script preparation

Copy only the validated v0.2 perception scripts from the repository into the
DFHack runtime scripts directory:

```powershell
# Run from the repository root.
$repoRoot = (Get-Location).Path
$repoScripts = @(
  (Join-Path $repoRoot 'dfhack\scripts\fortress-souls\get-dwarf-surroundings.lua'),
  (Join-Path $repoRoot 'dfhack\scripts\fortress-souls\get-stock-summary.lua')
)
$dfhackRuntime = 'C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\scripts\fortress-souls\'

foreach ($script in $repoScripts) {
  Copy-Item -LiteralPath $script -Destination $dfhackRuntime -Force
}
```

This is a manual preparation step for optional live-mode validation, not a
turnkey setup flow.

## Choose a valid dwarf ID

The `look_around` query is session-bound in the app, but direct DFHack
validation still needs one valid unit ID. Reuse the existing read-only list
script to pick an eligible dwarf:

```powershell
$dfhackRun = 'C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\dfhack-run.exe'
$list = (& $dfhackRun fortress-souls/list-dwarves) | ConvertFrom-Json
$list.items | Select-Object id, displayName, professionName | Format-Table -AutoSize
```

## Validate `look_around`

Replace `6597` and `1` with a valid ID and bounded radius from the list above.
The current v0.2 implementation accepts `radius = 1` or `2`.

```powershell
$dfhackRun = 'C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\dfhack-run.exe'
$unitId = '6603'
$radius = 2
$out = "$env:TEMP\fortress-souls-look-around-$unitId.json"
$err = "$env:TEMP\fortress-souls-look-around-$unitId.err.txt"

& $dfhackRun fortress-souls/get-dwarf-surroundings $unitId $radius > $out 2> $err
$exit = $LASTEXITCODE

"EXIT=$exit"
$sample = Get-Content $out -Raw | ConvertFrom-Json
$sample | Select-Object schemaVersion, bounds, legend, warnings | Format-List
$sample.cells | Format-Table -AutoSize
Get-Content $err -Raw
```

Expected:

```text
EXIT=0
schemaVersion = fortress-souls-dwarf-surroundings.v0.2
bounds.radius = 1 or 2
cells.Count = 9 or 25
```

The product result is intentionally filtered. It must not expose absolute map
coordinates, z-levels, raw unit IDs, item lists, material details, or other
research-only fields.

### Hidden-cell proof

The retained hidden-cell proof for v0.2 was captured on 2026-06-23 from:

```text
fortress-souls/get-dwarf-surroundings 6603 2
```

and retained at:

```text
dfhack/samples/perception/look-around.hidden.live-2026-06-23.json
```

For future reruns, search the currently eligible dwarves for a bounded result
that contains one or more hidden cells:

```powershell
$dfhackRun = 'C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\dfhack-run.exe'
$list = (& $dfhackRun fortress-souls/list-dwarves) | ConvertFrom-Json
$radius = 2

$list.items |
  ForEach-Object {
    $sample = (& $dfhackRun fortress-souls/get-dwarf-surroundings $_.id $radius) | ConvertFrom-Json
    [pscustomobject]@{
      id = $_.id
      displayName = $_.displayName
      hiddenCount = @($sample.cells | Where-Object { $_.visibility -eq 'hidden' }).Count
    }
  } |
  Format-Table -AutoSize
```

When a live hidden cell is found, verify that those cells expose only:

```text
dx
dy
visibility = hidden
```

Then retain that stdout as a dated sample under `dfhack/samples/perception/`.

### Current retained live samples

The retained direct live product samples are:

```text
dfhack/samples/perception/look-around.live-2026-06-23.json
dfhack/samples/perception/look-around.hidden.live-2026-06-23.json
```

The first sample was captured from `fortress-souls/get-dwarf-surroundings 6597 1`
and validates the direct product command path. The second sample was captured
from `fortress-souls/get-dwarf-surroundings 6603 2` and proves the strict
hidden-cell redaction path on a live fortress state.

## Validate `inspect_stocks`

```powershell
$dfhackRun = 'C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\dfhack-run.exe'
$out = "$env:TEMP\fortress-souls-stock-summary.json"
$err = "$env:TEMP\fortress-souls-stock-summary.err.txt"

& $dfhackRun fortress-souls/get-stock-summary > $out 2> $err
$exit = $LASTEXITCODE

"EXIT=$exit"
$sample = Get-Content $out -Raw | ConvertFrom-Json
$sample | Select-Object schemaVersion, gameTime, warnings | Format-List
$sample.categories | ConvertTo-Json -Depth 6
Get-Content $err -Raw
```

Expected:

```text
EXIT=0
schemaVersion = fortress-souls-stock-summary.v0.2
```

The result must expose only exact allowlisted counts. Approximate values,
bookkeeping precision, excluded-item bookkeeping, and uncategorized totals do
not belong in the product DTO.

### Optional stock-screen comparison

If you want extra manual confidence, compare the retained exact counts for:

- `drinks`
- `preparedFood`
- `wood`
- `stone`

against the visible in-game stock screen and record any mismatch. This remains
optional evidence while the product intentionally omits approximate values.

### Current retained live sample

The current retained direct live product sample is:

```text
dfhack/samples/perception/stock-summary.live-2026-06-23.json
```

It was captured from `fortress-souls/get-stock-summary` in a loaded fortress on
2026-06-23.
