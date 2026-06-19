# ADR-0003: DFHack adapter approach for Fortress Souls v0.1

**Status:** Accepted  
**Date:** 2026-06-19

## Context

Fortress Souls v0.1 needs read-only access to a live Dwarf Fortress fortress through DFHack. The app must not expose arbitrary DFHack execution, mutation endpoints, model tools, or write-capable game APIs.

Manual research validated that DFHack scripts installed under the DFHack script path can be invoked as commands through `dfhack-run.exe`, and that stdout can carry compact JSON suitable for a backend process adapter.

## Decision

Use `dfhack-run.exe` plus explicitly allowlisted Lua scripts for the first live v0.1 adapter.

Allowlisted scripts:

```text
fortress-souls/list-dwarves
fortress-souls/get-dwarf-snapshot
```

Adapter order:

1. `FakeDwarfFortressAdapter`
2. `JsonFileDwarfFortressAdapter`
3. `DfHackDwarfFortressAdapter`

The live adapter must:

- preflight DFHack availability,
- execute only exact allowlisted script commands,
- pass only primitive arguments such as unit id,
- impose a timeout,
- capture stdout and stderr,
- parse stdout as JSON,
- reject invalid JSON,
- map script/process failures to structured application errors.

## Consequences

Positive:

- Simple local implementation.
- Easy manual verification.
- JSON contract is testable through samples.
- Script layer remains read-only.
- Backend avoids raw DF memory access directly.

Negative:

- Depends on local DFHack install path/configuration.
- `dfhack-run` errors and Lua stack traces may appear on stdout.
- Backend must be robust against invalid JSON.
- Live adapter is Windows-path sensitive until configured.

## Safety rules

- No endpoint may execute arbitrary DFHack commands.
- No model may call DFHack directly.
- No script in v0.1 may mutate game state.
- Scripts must not emit raw userdata.
- Scripts must return JSON-safe primitives only.

## Validated evidence

B-019 validation produced:

```text
ListCount            : 7
ValidSnapshotCount   : 7
ErrorSnapshotCount   : 0
InvalidSnapshotCount : 0
```

This is sufficient to proceed with JSON-file adapter and live process adapter implementation.
