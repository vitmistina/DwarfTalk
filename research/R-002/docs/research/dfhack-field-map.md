# R-002 / B-019 DFHack field map for Fortress Souls v0.1

**Status:** validated from DFHack manual run  
**Date:** 2026-06-19  
**Validated scripts:**

- `fortress-souls/list-dwarves`
- `fortress-souls/get-dwarf-snapshot`

## Validation result

The B-019 validation run produced:

```text
ListCount            : 7
ValidSnapshotCount   : 7
ErrorSnapshotCount   : 0
InvalidSnapshotCount : 0
```

This means the v0.1 script pair is good enough to become the first real `DfHackDwarfFortressAdapter` contract, behind a strict command allowlist.

## Validated sample fortress

| Id | Profession | Job | Skills | Traits | Values | Needs | Mannerisms | Inventory | Wounds | Nobles | Position |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 6597 | Miner | PickupEquipment | 7 | 50 | 2 | 21 | 0 | 8 | 0 | 0 | 70,85,173 |
| 6598 | Planter |  | 6 | 50 | 3 | 22 | 0 | 8 | 0 | 0 | 72,90,173 |
| 6599 | Woodworker | PickupEquipment | 8 | 50 | 3 | 22 | 0 | 8 | 0 | 0 | 70,88,173 |
| 6600 | Stonecrafter |  | 6 | 50 | 2 | 22 | 1 | 8 | 0 | 0 | 70,85,173 |
| 6601 | Fisherdwarf | Fish | 6 | 50 | 3 | 19 | 1 | 8 | 0 | 0 | 67,87,173 |
| 6602 | Mason |  | 8 | 50 | 2 | 23 | 0 | 8 | 0 | 0 | 72,89,173 |
| 6603 | expedition leader |  | 10 | 50 | 2 | 21 | 2 | 8 | 0 | 1 | 65,81,173 |

## Adapter contract

### `list-dwarves.lua`

Command:

```powershell
.\dfhack-run.exe fortress-souls/list-dwarves
```

Output schema:

```text
fortress-souls-dwarf-list.v0.1
```

Validated useful fields:

| Field | Decision | Notes |
|---|---|---|
| `id` | keep | Unit id as string for API path and frontend selection. |
| `rawId` | keep debug | Numeric unit id. |
| `displayName` | keep | DFHack readable name including nickname and profession. |
| `professionName` | keep | Human-readable profession. |
| `professionToken` | keep | Stable-ish enum token. |
| `currentJobType` | keep nullable | Missing means no current job, not necessarily idle. |
| `stressCategory` | keep | DFHack scale: `0` most stressed, `6` least stressed. |
| `soulPresent` | keep | Required before reading skills/personality. |
| `histFigureId` | keep | v0.2 bridge to historical identity. |
| `creatureId`, `casteId` | keep | Useful for pronouns/biological framing. |
| `position` | keep | Validated x/y/z location. |

### `get-dwarf-snapshot.lua`

Command:

```powershell
.\dfhack-run.exe fortress-souls/get-dwarf-snapshot <unitId>
```

Output schema:

```text
fortress-souls-dwarf-snapshot.v0.1
```

Validated sections:

| Section | v0.1 decision | Prompt use |
|---|---|---|
| `identity` | keep | Always include. |
| `flags` | keep debug/light prompt | Mostly adapter/debug safety. |
| `location` | keep | Include coordinates and valid path destination if available. |
| `work.currentJob` | keep nullable | Strong prompt grounding. |
| `stress` | keep | Include category and raw values with caution. |
| `health.counters` | keep | Include only nonzero counters in prompt. |
| `health.body.wounds` | keep shallow | None present in sample, but shape works. |
| `inventory` | keep | Item descriptions are useful but low-priority prompt context. |
| `roles.noblePositions` | keep | Expedition leader detected correctly. |
| `roles.military` | keep nullable | Future militia support. |
| `relationships` | keep placeholder | Empty in young fortress; do not rely on v0.1. |
| `skills` | keep full raw; prompt top skills | Strong signal. |
| `personality.traits` | keep full raw; prompt extremes only | Very strong signal but too noisy unfiltered. |
| `personality.values` | keep `type/token/strength` only | Do **not** read nonexistent `.value`. |
| `personality.needs` | keep | Prompt strongest needs and unmet needs. |
| `personality.mannerisms` | keep | Sparse but high flavour. |
| `personality.preferences` | keep raw unresolved | Resolve later to readable material/creature/item names. |
| `promptCandidates` | keep | Primary model-facing seam. |

## Important field mapping decisions

### Traits

`unit_personality.traits` is a static array indexed by `personality_facet_type`.

Decision:

```lua
token = enum_name(df.personality_facet_type, index)
```

Do **not** subtract one from the index.

### Values

`personality_valuest` contains:

```text
type
strength
```

Decision:

```lua
belief.type
belief.strength
```

Do **not** read:

```lua
belief.value
```

It does not exist and caused validation errors during probing.

### Needs

`personality_needst` contains:

```text
id
deity_id
focus_level
need_level
```

Interpretation for v0.1:

| Field | Meaning |
|---|---|
| `focusLevel` | Current satisfaction/pressure. Goes toward `400` when satisfied. Below `0` means unmet pressure. |
| `needLevel` | Raw severity/decay pressure, not current urgency by itself. |
| `isUnmet` | Derived from `focusLevel < 0`. |
| `isDeeplyUnmet` | Derived from `focusLevel < -999`. |

### Location

`unit.pos` extraction is validated.

Path destination may be:

```text
-30000, -30000, -30000
```

Decision: represent this as `isValid = false`; do not treat it as a real tile.

### Burrows

`unit.burrow_id` was probed and does not exist in this structure shape.

Decision: exclude burrow extraction from v0.1 snapshot. Revisit using `df.burrow` / assignment structures in v0.3 perception work.

### Game-generated prose

DF game-generated description/personality prose is desirable, but not part of the v0.1 canonical snapshot.

Decision: v0.1 uses raw named fields plus `promptCandidates`. Do not rely on UI text extraction.

## Prompt candidate seam

`promptCandidates` is the model-facing seam. It should be used before raw full sections.

Recommended v0.1 prompt input:

```json
{
  "identity": {},
  "currentJob": {},
  "location": {},
  "topSkills": [],
  "extremeTraits": [],
  "strongValues": [],
  "strongNeeds": [],
  "mannerisms": [],
  "inventoryItems": [],
  "notableCounters": [],
  "wounds": [],
  "noblePositions": []
}
```

The full snapshot remains available for debugging and future mapping, but the model should not receive everything by default.

## Known limitations

- Relationships are not meaningfully resolved in this sample.
- Preferences are raw var fields, not resolved to readable likes.
- Building/room/zone resolution is not implemented.
- Wounds are shallow and untested on injured dwarves.
- Historical events, announcements, crimes, mandates, and incidents are out of v0.1 scope.
- `dfhack-run` Lua stack traces can appear on stdout. The backend must treat stdout as untrusted until JSON parsing succeeds.
- Scripts must never emit raw userdata. Everything must be mapped into JSON-safe primitives.

## Recommendation

Mark B-019 as validated and proceed to B-020 / B-021 integration work:

1. Check these scripts into `dfhack/scripts/fortress-souls/`.
2. Check sample JSON into `dfhack/samples/`.
3. Implement JSON-file adapter against these samples first.
4. Implement live DFHack process adapter with:
   - script command allowlist,
   - TCP preflight,
   - timeout,
   - stdout JSON parse,
   - stderr/stdout capture,
   - structured error mapping.
