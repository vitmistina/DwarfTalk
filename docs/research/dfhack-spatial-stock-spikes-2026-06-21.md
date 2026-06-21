# R-003: Spatial Vision and Fortress Stock Spikes

**Status:** Completed research spike

**Date:** 2026-06-21

**Scope:** Next-version evidence only; no v0.1 product, API, prompt, or adapter changes

## Research outcome

Two fixed-name, read-only DFHack Lua commands now emit compact versioned JSON:

- `fortress-souls/research-spatial-vision`
- `fortress-souls/research-stock-summary`

The spatial command accepts either `unit <unitId> <radius> <zOffset>` or
`area <x> <y> <z> <width> <height>`. A request covers one z-level and is capped
at 25 by 25 tiles (625 cells); unit radius is capped at 12. Coordinates must be
integers and the complete rectangle must be valid on the loaded map.

The stock command follows the installed DFHack `gui/dfstatus` baseline: start
at `df.global.world.items.other.IN_PLAY`, exclude rotten, dumped, forbidden,
construction, and trader items, and sum `item:getStackSize()`. Each excluded
item is assigned to its first matching reason so arithmetic is unambiguous.

Neither command writes files or accepts an output path. They do not call
mutation helpers, arbitrary commands, UI selection, or model-controlled input.

## Environment

Live evidence was captured from:

```text
Operating system: Windows (PowerShell)
Dwarf Fortress: v0.53.14 win64 STEAM
DFHack: 53.14-r2
DFHack git description: 53.14-r2-0-g2e2b9213
Fortress state: world, site, and map loaded
```

The authoritative local references inspected were:

- `hack/scripts/gui/dfstatus.lua`
- `hack/scripts/devel/export-map.lua`
- `hack/lua/tile-material.lua`
- the installed DFHack Lua API documentation
- ADR-0003 and the existing Fortress Souls DFHack research/runbook

An attempted external official-source search was unavailable with HTTP 403.
No conclusion below depends on that failed lookup.

## Spatial findings

The final live query used validated citizen `6597`, whose reported position was
`(70,85,173)`:

```powershell
& $DfHackRun fortress-souls/research-spatial-vision unit 6597 1 0
```

Result: exit code 0, valid JSON, 6,765 UTF-8 bytes, 9 unique row-major cells,
and this deterministic grid:

```text
#^.
^u.
^BB
```

The legend contained only the five symbols present: wall, ramp, floor, unit,
and building. All nine cells explicitly reported `hidden: false`; all nine
geological materials resolved through their geological layer. The unit cell
reported two units and 16 items at their true position.

The semantic cell shape includes terrain type and attributes, hidden/visible
state, liquid, walkability, geological and construction material, plant,
building/zones, construction, units with threat flags, contained/loose items,
and flows. Mineral material is resolved only from matching mineral block events.
If neither a verified event nor geological layer resolves, the material remains
an explicit `unknown` with a reason.

Symbol precedence is fixed and emitted with every response:

```text
hidden > great danger > danger > invader > unit > magma > water > fire >
building/zone > construction > item > plant > other flow > terrain
```

`plugins.map-render.render_map_rect` was verified as available in the installed
API but was not used. Its tileset-dependent output is diagnostic rendering, not
the semantic source.

### Spatial anomaly

The first live unit query returned `INVALID_ARGUMENT` because this DFHack build
returns an `x,y,z` tuple from `dfhack.units.getPosition` and
`dfhack.items.getPosition`, while the first implementation expected a position
object. The final script accepts either representation. The retry succeeded;
there was no crash or game-state change.

## Stock findings

The final live command was:

```powershell
& $DfHackRun fortress-souls/research-stock-summary
```

Result: exit code 0, valid JSON, 3,135 UTF-8 bytes. The observed arithmetic was:

```text
IN_PLAY item objects:       500
Total stack quantity:       596
Usable quantity:            596
Excluded quantity:            0
Categorized quantity:       236
Uncategorized quantity:     360
Exact drinks:                60
```

The capture also distinguished prepared food, seeds, meat, fish, raw fish,
plants, plant growths, wood, fuel, cloth, leather, weapons, ammunition, armour,
tools, metal bars, stone, furniture, and finished goods. Ownership,
containment, and general in-inventory object counts are reported as context but
do not change the `dfstatus`-compatible usable baseline. Each usable item enters
at most one category, so a container is not added to the category of its
contents.

The requested bookkeeping paths were checked directly in the live build:

```text
df.global.plotinfo.positions.bookkeeper_precision -> nil
df.global.plotinfo.bookkeeper_settings             -> nil
df.global.plotinfo.bookkeeper_precision             -> nil
df.global.plotinfo.positions.bookkeeper_settings    -> nil
```

Therefore no direct game estimate or deterministic reproduction of its rounding
was verified. Category responses contain exact counts and omit approximate
values. The live exact value `60 drinks` must not be presented as a verified
game-style `~60` until a supported bookkeeping source and rounding rule are
confirmed.

## UI comparison

The installed `gui/dfstatus.lua` source confirms that its exact internal drink,
food, wood, fuel, cloth, hide, and selected metal-bar counts use the same
IN_PLAY exclusions and stack-size arithmetic as this spike.

A visual comparison against the running Dwarf Fortress stock screen was not
performed. The task prohibited UI click emulation, and no non-mutating capture
of the visible UI was available. Exact-to-visible and approximate-count
comparison remains a manual follow-up; this report does not claim it passed.

## Evidence and validation

Captured samples:

- `dfhack/samples/research/spatial-vision.live-2026-06-21.json`
- `dfhack/samples/research/stock-summary.live-2026-06-21.json`
- `dfhack/samples/research/spatial-vision.synthetic.json`
- `dfhack/samples/research/stock-summary.synthetic.json`

Live samples declare `provenance.kind = live-dfhack`; offline fixtures declare
`provenance.kind = synthetic`. The focused validator parses all four samples and
asserts dimensions, row-major determinism, cell/grid agreement, legend
completeness, coordinate uniqueness, stock arithmetic, category arithmetic,
exclusion arithmetic, and absence of known mutation calls.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\validate-dfhack-research.ps1
```

Observed result: all four samples and the read-only source scan passed.

## Windows run instructions

Install only the two named research scripts explicitly:

```powershell
$DfHackHack = 'C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack'
$DfHackRun = Join-Path $DfHackHack 'dfhack-run.exe'
$Target = Join-Path $DfHackHack 'scripts\fortress-souls'

Copy-Item .\dfhack\scripts\fortress-souls\research-spatial-vision.lua $Target
Copy-Item .\dfhack\scripts\fortress-souls\research-stock-summary.lua $Target
```

Run bounded queries and parse stdout as JSON:

```powershell
& $DfHackRun fortress-souls/research-spatial-vision unit 6597 2 0 |
  ConvertFrom-Json

& $DfHackRun fortress-souls/research-spatial-vision area 68 83 173 5 5 |
  ConvertFrom-Json

& $DfHackRun fortress-souls/research-stock-summary |
  ConvertFrom-Json
```

For application use, retain ADR-0003 process controls: TCP preflight, a fixed
command allowlist, structured arguments, a short timeout with process kill,
stdout/stderr capture, and an output-size cap. Direct manual `dfhack-run`
commands do not provide those process-level controls themselves; the Lua work is
bounded by 625 cells, 200,000 scanned items, 10,000 units, 5,000 flows per map
block, 2,048 events per map block, 40 items per cell, 20 emitted flows per cell,
and 20 zones per cell.

## Recommended DTOs

Application-owned DTOs should remain smaller than the research envelopes:

```text
SpatialQuery { mode, unitId?, radius?, zOffset?, x?, y?, z?, width?, height? }
SpatialMap { schemaVersion, bounds, gameTime, cells, grid, legend, warnings }
SpatialCell { position, terrain, hidden, visible, liquid, walkable, material,
              plant?, building?, construction?, units[], items[], flows[], symbol }

StockSummary { schemaVersion, gameTime, totals, excludedByReason, categories,
               bookkeeping, warnings }
StockCategory { exact, approximate? }
```

The application should validate these DTOs after JSON parsing and before prompt
use. Research-only descriptions, raw IDs, and unsupported fields should not be
promoted automatically.

## Limitations and follow-up

- Verify exact counts against the visible stock UI without automation that
  changes game UI state.
- Determine the supported DF 0.53 bookkeeping precision/settings structure and
  reproduce rounding only after comparison at multiple precision levels.
- Capture live water, magma, fire/smoke/mist, hidden tiles, zones,
  constructions, mineral veins, contained items, and hostile units.
- Verify squad-assigned equipment independently of generic `in_inventory`.
- Decide a future closed command allowlist and DTO contract before any product
  integration; do not add these research commands to the v0.1 adapter.

The recommended next implementation step is another read-only evidence pass
covering those sparse spatial cases and a human-recorded stock UI comparison,
followed by a next-version backlog item and contract review. No architecture or
v0.1 backlog status changes are warranted by this spike.
