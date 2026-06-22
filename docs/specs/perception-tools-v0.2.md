# Perception Tool Contract v0.2

Status: Draft  
Product spec: `docs/specs/fortress-souls-v0.2.spec.md`

## Contract Rules

Tool names, argument schemas, result schemas, bounds, and failure categories
are application-owned and versioned. Provider annotations and framework DTOs
are adapter details.

Every tool:

- is read-only and cancellation-aware,
- has typed arguments and a typed result,
- validates model arguments before external work,
- validates adapter output before model use,
- emits a schema version and optional bounded warnings,
- obeys per-call and whole-turn output budgets,
- returns no raw DFHack values, commands, paths, or exception text.

Free-text fields are normalized and length-bounded. IDs are opaque strings or
validated domain values, never executable input.

## `look_around`

Purpose: inspect a small revealed area centered on the selected session dwarf.

Model arguments:

```text
radius: integer, optional, application-bounded
```

The observer dwarf ID, center position, z-level, and command name are derived
by the application. v0.2 does not permit arbitrary coordinates, arbitrary unit
IDs, or z-offset selection from the model.

The product result is intentionally smaller than the R-003 research envelope:

```text
schemaVersion
gameTime?
bounds { radius, width, height }
cells[]
legend
warnings[]
```

`bounds` carries only local dimensions around the selected dwarf. Absolute map
coordinates, z-level, and observer position do not survive filtering.
`legend` is a bounded allowlisted summary of the retained terrain or feature
classes present in the result.

Each cell is bounded to:

```text
dx
dy
visibility
terrainClass?
walkable?
featureClass?
unitCount?
```

`dx` and `dy` are relative to the selected dwarf. All optional fields are
visible-cell only. `walkable` is the retained passable or walkable flag.
`featureClass` is a coarse visible feature summary, bounded to `building` for
now. `unitCount` is count-only. No item-count field is promoted in v0.2.

Hidden-cell filtering is strict: a hidden cell may expose only `dx`, `dy`, and
`visibility = hidden`. No terrain, materials, liquids, plants, units, items,
buildings, zones, constructions, flows, names, or raw IDs survive that filter
before the result reaches the model.

R-003 research fields intentionally not promoted now: absolute coordinates,
z-offsets, grid text, symbol precedence, material resolution, plant detail,
liquid detail, raw building or zone detail, construction detail, raw unit or
item lists, loose-item counts, contained-item detail, threat flags, flow
detail, raw IDs, and names.

Sparse live evidence still required before the retained fields are considered
verified for product use:

- at least one hidden cell proving the strict redaction path.

Water, magma, fire, smoke, mist, zones, constructions, mineral resolution, and
item cases are not verification gates for this DTO because those fields are
not promoted.

This is a revealed local-map query, not verified dwarf line-of-sight.

## `inspect_stocks`

Purpose: inspect exact, curated fortress stock counts.

Model arguments:

```text
category: allowlisted category or "all"
```

Categories are defined by the application contract. Unknown categories are
invalid arguments, not search strings.

The product result is intentionally smaller than the R-003 research envelope:

```text
schemaVersion
gameTime?
requestedCategory
categories[]
warnings[]
```

Each category entry is bounded to:

```text
category
exactCount
```

If `requestedCategory = "all"`, `categories[]` returns the allowlisted
categories in stable order. Otherwise it returns exactly one category.

`exactCount` is the exact usable quantity for that curated category.
Approximate values are absent from the product DTO. Research bookkeeping
fields, ownership or containment context, excluded-item bookkeeping,
uncategorized totals, and other `gui/dfstatus` parity detail are intentionally
not promoted.

The contract remains exact-only until both a supported bookkeeping source and a
manual stock-UI comparison are retained as evidence. Until then the product
must omit approximate values rather than inventing DF-style rounding.

## Future Live Command Proposals

If `look_around` and `inspect_stocks` graduate to the live adapter, the
proposed new production command names for those two R-003-derived tools are:

- `fortress-souls/get-dwarf-surroundings`
- `fortress-souls/get-stock-summary`

These are proposed new allowlist entries for those two tools only. They do not
redefine the existing v0.1 command allowlist accepted in ADR-0003 or the full
eventual v0.2 live allowlist.

The R-003 research commands remain evidence-only and are not promoted as
production commands:

- `fortress-souls/research-spatial-vision`
- `fortress-souls/research-stock-summary`

## `list_dwarves`

Purpose: return a bounded list of eligible fortress dwarves that may be
inspected by `inspect_dwarf` during the same turn.

Model arguments: none.

The tool reuses the application-owned eligible-dwarf query and returns curated
identity summaries only. The result is deterministically ordered and bounded.
It does not mean nearby, visible, socially known, or currently available unless
a later contract explicitly adds those semantics.

The runtime records the returned dwarf IDs as the current turn's inspectable
set. This authorization state is ephemeral and is never supplied by the model.

## `inspect_dwarf`

Purpose: inspect a curated detail view for one eligible dwarf.

Model arguments:

```text
dwarfId: validated ID previously returned by list_dwarves in this turn
```

Calls without a matching current-turn list result are rejected before adapter
execution. The tool reuses validated application snapshot behavior but maps it
to a smaller model-facing result. It must not serialize the full snapshot by
default.

The result may contain curated identity, current work, stress summary, needs,
values, traits, roles, and relationships already approved for model context.
It excludes raw DFHack data and fields not approved by the current snapshot and
prompt contracts.

## Result Freshness

Each invocation is a point-in-time query. When a trustworthy game-time marker
is available it is included in the validated result. Results from separate
calls are not promised to form a transactional fortress snapshot.

The model instructions require qualified language when results are absent,
truncated, carry warnings, or may have changed.

## Stable Failures

Tools map expected failures to:

```text
unavailable
invalid_arguments
not_found
timed_out
invalid_data
result_too_large
cancelled
```

Cancellation propagates to the turn rather than becoming model-visible prose.
The agent runtime owns the separate `budget_exhausted` category.

## Evidence Status

Existing evidence:

- eligible dwarf list and snapshot contracts from v0.1,
- R-003 live spatial and stock samples,
- R-003 bounds, validation, and read-only source scan.

Still required before live product integration:

- product DTO schemas and fixtures smaller than research output,
- hidden-cell redaction tests,
- sparse spatial evidence for retained fields: hidden-cell redaction evidence,
- approved allowlist entries for these two R-003-derived live tools, using only
  `fortress-souls/get-dwarf-surroundings` and
  `fortress-souls/get-stock-summary`,
- a retained manual stock UI comparison before any exact-to-visible parity
  claim, and a separately verified bookkeeping source before any approximate
  stock value is added,
- live smoke instructions and retained output for promoted commands.
