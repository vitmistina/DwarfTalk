# ADR-0001: v0.1 Implementation Stack

Status: Accepted  
Date: 2026-06-20

## Context

Fortress Souls v0.1 needs a small, stable implementation stack so backlog items do not repeatedly reopen the same technology choices.

The product slice is intentionally narrow:

- read-only dwarf data extraction,
- deterministic prompt assembly,
- one local web UI,
- one configured LLM provider behind an application-owned interface,
- observability from the first backend slice.

The stack needs to favor:

- fast local iteration,
- strong reviewability,
- simple modular-monolith boundaries,
- deterministic tests with fake adapters,
- straightforward local observability,
- no pressure toward microservices or premature infrastructure.

## Decision

Use this stack for Fortress Souls v0.1:

### Backend

- .NET 10 LTS
- ASP.NET Core
- C#
- xUnit for backend testing

### Frontend

- TypeScript
- React
- Vite
- a lightweight query layer, with TanStack Query as the default choice when the frontend data flow needs one

### Cross-cutting

- one monorepo for docs, backend, frontend, DFHack scripts, tests, and developer tooling
- OpenTelemetry for traces and metrics
- structured logging in the backend
- fake adapters and fixtures as first-class development primitives

## Why this stack

This stack best matches the accepted v0.1 constraints:

- .NET and ASP.NET Core fit a local modular monolith well.
- C# provides strong typing for versioned contracts, validation, and adapter seams.
- React and TypeScript are sufficient for the small local chat UI without forcing a larger frontend framework.
- Vite keeps frontend startup and build tooling light.
- OpenTelemetry preserves vendor-neutral observability and cleanly supports the local dashboard path chosen separately in ADR-0004.

## Explicit rejections for v0.1

Do not introduce these as the default v0.1 stack:

- microservices or separately deployed internal services,
- a database for chat persistence,
- heavy frontend frameworks beyond the local UI need,
- multiple real LLM providers at once,
- model tool-calling runtimes,
- a generic DFHack execution surface.

These are rejected because they add surface area and operational complexity outside the approved v0.1 slice.

## Consequences

Positive:

- backlog items can build against a stable technical baseline,
- local development remains simple,
- deterministic contracts and tests are easier to maintain,
- the repository can add fake mode before live DFHack or real-provider work.

Negative:

- early stack choices narrow experimentation,
- .NET 10 availability must be kept aligned with contributor environments and CI,
- TanStack Query may prove unnecessary for some simple UI flows and should be introduced only when the frontend actually benefits from it.

## Unresolved questions

The following remain intentionally open for later backlog items:

- whether the frontend needs TanStack Query immediately or can start with simpler local state,
- whether local one-command startup should later add Aspire AppHost and ServiceDefaults,
- which exact real LLM endpoint and model should back v0.1 once provider work begins.

Those decisions must stay within the boundaries already established here and in later ADRs.
