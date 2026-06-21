# Architecture Overview

## Intent

Fortress Souls v0.1 is a local, read-only companion for Dwarf Fortress. The
browser selects a dwarf from a backend-provided list, the backend fetches a
validated snapshot for that dwarf ID, deterministic application code assembles
prompt context, and one configured provider generates the dwarf's prose reply.

## Current Repository Shape

- `docs/` holds stable specs, ADRs, runbooks, and research.
- `dfhack/` holds the allowlisted read-only Lua scripts plus retained manual
  validation samples.
- `samples/` holds fake app-facing dwarf list and snapshot examples for offline
  development and tests.
- `scripts/` holds canonical local dev, format, test, and check entry points,
  plus maintainer utilities.
- `src/backend/` holds the modular monolith backend, adapters, observability,
  prompting, and automated tests.
- `src/frontend/` holds the local React/Vite UI and browser tests.
- `.agents/` holds repository guidance, memories, prompts, skills, and agent
  definitions.

## v0.1 Architectural Direction

- One backend deployment unit with explicit internal module boundaries.
- Read-only adapter sequence: `Fake`, `JsonFile`, and optional `DfHackProcess`.
- The browser owns dwarf identity selection; the Dwarf Fortress UI cursor is
  not an application input.
- Deterministic contracts govern dwarf list/snapshot data, prompt assembly,
  session state, and runtime status projections.
- Observability is built in from the first backend slice with correlation IDs,
  structured logs, traces, and metrics.
- Fake mode is the default supported development path; real provider and live
  DFHack modes remain optional and separately documented.

## Proposed v0.2 Direction

v0.2 is currently a draft, not implemented architecture. The proposed change
introduces an application-owned dwarf-agent turn above provider transport:

```text
Chat API
  -> Chat/Application session orchestration
    -> IDwarfAgent
      -> bounded tool-loop adapter
        -> configured model transport
        -> closed perception tool registry
          -> application-owned read-only queries
            -> Fake or allowlisted DwarfFortress adapter
```

The application continues to own selected-dwarf identity, sessions, prompt and
history policy, tool authorization, execution budgets, stable failures,
receipts, and telemetry. A library may sequence provider messages and dispatch
approved functions, but its agent, function, session, and provider DTOs remain
inside the adapter boundary.

Perception observations are ephemeral to one turn. Session history stores only
the player message and final dwarf response. v0.2 adds no persistent memory,
background runtime, multi-agent workflow, or game mutation.

The proposed tools are closed application capabilities: nearby revealed map,
exact stock summary, eligible dwarf list, and guarded dwarf detail. They are
not DFHack commands. Live implementations still use fixed allowlisted commands,
structured arguments, validation, timeout, cancellation, and output limits.

See:

- `docs/specs/fortress-souls-v0.2.spec.md`
- `docs/specs/perception-tools-v0.2.md`
- `docs/specs/prompt-contract-v0.2.md`
- `docs/backlog/v0.2-backlog.md`
- `docs/decisions/adr-0007-agent-runtime-and-tool-loop.md`

## Constraints

- Keep game mutation impossible by construction.
- Do not add generic DFHack execution or open-ended model tool surfaces.
- Any model-callable capability must be a typed, bounded, application-owned,
  read-only tool accepted by the active version specification.
- Keep prompt/response content and secrets out of default telemetry.
- If the architecture direction changes, update this document and the relevant
  ADRs in the same change set.
