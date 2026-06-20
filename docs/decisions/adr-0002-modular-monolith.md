# ADR-0002: Modular Monolith Boundaries for v0.1

Status: Accepted  
Date: 2026-06-20

## Context

Fortress Souls v0.1 is a local application with a narrow vertical slice, but it still crosses several concerns:

- dwarf data extraction,
- deterministic prompt assembly,
- chat/session orchestration,
- LLM invocation,
- observability,
- HTTP delivery and browser UI.

Without an explicit boundary decision, implementation work will drift toward either:

- one undifferentiated application blob, or
- premature service decomposition.

Both outcomes would make the repo harder to review and harder for future backlog items to extend safely.

## Decision

Fortress Souls v0.1 will be implemented as a modular monolith in a monorepo.

The backend remains one deployable application with explicit internal module boundaries.

The dependency direction is:

```text
Domain <- Application <- Adapters/API composition
```

Module responsibilities are:

- Domain: pure game and chat concepts, validation rules, and application-owned contracts that do not depend on infrastructure.
- Application: use-case orchestration and ports for dwarf data access, prompting, provider calls, and session coordination.
- DwarfFortress adapter: DFHack and JSON-file integration behind application-owned contracts.
- Prompting: deterministic prompt construction, ordering, truncation, and related rules.
- Llm adapter: provider request/response mapping and provider-specific configuration handling.
- Observability: shared instrumentation primitives, correlation, redaction helpers, and telemetry registration.
- API: transport validation, HTTP endpoints, dependency composition, and response mapping.

The frontend is separate source within the same monorepo and depends on the published HTTP contract, not backend implementation types.

## Explicit rejections for v0.1

Reject these architectural moves in v0.1:

- microservices,
- a backend-for-frontend as a separate deployment unit,
- a shared miscellaneous utilities layer that becomes a second domain,
- direct frontend dependency on backend implementation assemblies or internal DTOs,
- domain references to ASP.NET Core, DFHack, provider SDKs, telemetry SDKs, filesystem APIs, or process execution.

The backend must not split into separate services for:

- DFHack integration,
- prompt assembly,
- provider calls,
- observability,
- chat orchestration.

## Why this decision

The modular-monolith path gives the project enough structure to protect safety and determinism without introducing distributed-system complexity before it is needed.

It also matches the repository's skills guidance:

- pure game concepts stay in Domain,
- DFHack and file access stay behind adapters,
- deterministic prompt construction stays separate from provider calls,
- use-case coordination stays in Application,
- transport logic stays thin in API.

## Consequences

Positive:

- module ownership stays reviewable,
- the read-only DFHack boundary remains easier to enforce,
- tests can replace adapters with fakes without changing application logic,
- future extraction remains possible if later justified by an ADR.

Negative:

- the repo needs discipline to keep boundaries intentional,
- some project structure will appear before the full feature set exists,
- module boundaries may feel heavier than a single-project prototype.

## Unresolved questions

These details are deferred to later backlog items:

- the exact project split inside `src/backend/`,
- whether a dedicated chat/application module is needed immediately or later,
- the precise frontend query/cache shape once the browser UI exists.

Those details may evolve, but they must not violate the modular-monolith direction or the inward dependency rule in this ADR.
