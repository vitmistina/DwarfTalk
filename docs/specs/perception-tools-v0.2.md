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

The product result is smaller than the R-003 research envelope and contains:

```text
schemaVersion
gameTime?
bounds
cells[]
legend
warnings[]
```

Cells may contain bounded terrain class, liquid class, walkability, visible
construction/building/zone summaries, nearby unit summaries, loose item
summaries, and visible flow classes. Hidden cells expose only position plus a
hidden/unknown marker. Raw descriptions, full item lists, and unrestricted IDs
are excluded.

This is a revealed local-map query, not verified dwarf line-of-sight.

## `inspect_stocks`

Purpose: inspect exact, curated fortress stock counts.

Model arguments:

```text
category: allowlisted category or "all"
```

Categories are defined by the application contract. Unknown categories are
invalid arguments, not search strings. Results contain exact usable quantities,
bounded exclusion totals, optional verified approximate values, game time when
available, and warnings.

Until bookkeeping precision and UI comparison are verified, approximate values
are omitted. Exact values must not be presented as game-style approximations.

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
- sparse spatial evidence listed by R-003 where it affects promoted fields,
- a closed production command allowlist,
- stock UI comparison or an explicit exact-count-only limitation,
- live smoke instructions and retained output for promoted commands.

