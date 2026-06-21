# ADR-0007: Agent Runtime and Tool Loop for v0.2

Status: Proposed  
Date: 2026-06-21  
Related: ADR-0002, ADR-0003, ADR-0005  
Decision gate: `R2-001` in `docs/backlog/v0.2-backlog.md`

## Context

v0.2 introduces bounded, read-only perception tools inside a selected dwarf's
chat turn. The runtime must exchange structured tool calls with one configured
model, validate and execute application-owned queries, return structured
observations, and obtain one final prose response.

The existing application already owns:

- selected-dwarf identity and in-memory sessions,
- deterministic prompt assembly and history limits,
- provider configuration and failure mapping,
- cancellation, timeouts, and response bounds,
- telemetry and redaction,
- the fixed DFHack command allowlist.

ADR-0005 deliberately keeps `IChatProvider` plain-text and says future agentic
behavior belongs above it. v0.2 needs to extend that architecture without
allowing framework types or provider tool-call DTOs to become product
contracts.

At drafting time, external official documentation lookup was unavailable with
HTTP 403. Claims about current package versions, support status, migration
guidance, and exact APIs must be verified and recorded by R2-001 before this ADR
is accepted.

## Decision Drivers

- Deliver one useful perception loop with little new machinery.
- Preserve application ownership of identity, policy, budgets, and errors.
- Support the configured OpenAI-compatible endpoint in actual tool-call tests.
- Keep automated tests deterministic and independent of live providers.
- Contain third-party types inside the LLM/agent adapter.
- Avoid implementing provider protocol parsing when a supported abstraction is
  sufficient.
- Avoid adopting workflow, memory, multi-agent, or persistence features that
  v0.2 does not need.

## Options

### Microsoft.Extensions.AI function invocation

Use `IChatClient` and the smallest supported function-invocation layer to run a
single-agent tool loop. Fortress Souls supplies typed functions that call
application-owned perception queries.

Advantages:

- smallest Microsoft abstraction intended for chat and function calling,
- avoids hand-maintaining most provider tool-call protocol behavior,
- composes with an application-owned runtime boundary,
- leaves room to adopt a fuller agent framework later.

Risks:

- exact loop-limit, middleware, and failure hooks require verification,
- the current custom provider adapter may need an `IChatClient` bridge or
  replacement,
- OpenAI-compatible endpoints differ in tool-call support.

### Microsoft Agent Framework

Implement the internal dwarf agent with the supported .NET agent abstraction,
while keeping its agent and session types behind an application-owned port.

Advantages:

- provides an explicit agent abstraction and supported growth path,
- may later support workflows, approval, richer context, and multi-agent work,
- may reduce a later migration if those capabilities become concrete scope.

Risks:

- duplicates session, history, orchestration, and policy already owned by the
  application,
- introduces a larger API and dependency surface for one bounded loop,
- encourages adoption of out-of-scope memory and workflow concepts,
- package maturity and current support claims still require evidence.

### Semantic Kernel

Use Semantic Kernel agents or function-calling orchestration.

Advantages:

- established .NET ecosystem and broad connector surface,
- mature function and prompt abstractions.

Risks:

- Microsoft guidance may direct new agent development to Agent Framework,
- creates migration risk if its agent features are in maintenance transition,
- offers substantially more surface than v0.2 requires.

### Custom tool loop and provider protocol

Extend the current OpenAI-compatible HTTP adapter to parse tool requests and
implement the loop entirely in Fortress Souls.

Advantages:

- complete control over policy and failure behavior,
- no additional production dependency,
- smallest conceptual surface if provider behavior is extremely narrow.

Risks:

- Fortress Souls owns protocol variants, malformed payload handling, message
  sequencing, and compatibility maintenance,
- easy to mix provider DTOs with application policy,
- duplicates supported library behavior without adding product value.

## Proposed Decision

Subject to R2-001, use the smallest supported
`Microsoft.Extensions.AI` function-invocation layer for the v0.2 tool loop.

Do not adopt Microsoft Agent Framework for v0.2 unless the spike demonstrates
that the smaller layer cannot enforce the required budgets, cancellation,
failure mapping, fake testing, and OpenAI-compatible transport behavior without
substantial custom orchestration.

Do not adopt Semantic Kernel for new v0.2 agent work if current official
guidance confirms Agent Framework as its successor for agent capabilities.

Do not implement a custom provider-protocol loop unless both Microsoft options
fail a documented requirement. A custom fallback must remain behind the same
application-owned boundary and requires focused protocol contract tests.

## Application Boundary

Application owns a framework-neutral contract:

```csharp
public interface IDwarfAgent
{
    Task<AgentTurnResult> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken);
}
```

The adapter implementing this contract may use Microsoft abstractions. Domain,
API, Prompting, DwarfFortress, and frontend code must not reference Microsoft
agent, chat-client, function, session, or provider DTOs.

Fortress Souls, not the library, owns:

- selected-dwarf identity and tool authorization,
- the closed tool registry,
- typed argument and result contracts,
- maximum rounds, calls, bytes, and total duration,
- timeout and cancellation policy,
- read-only enforcement and DFHack allowlisting,
- ephemeral observation lifetime,
- history atomicity,
- stable application errors and safe receipts,
- telemetry names, dimensions, and redaction.

The framework may perform provider message sequencing and function dispatch
only within those constraints.

## R2-001 Acceptance Evidence

The spike must record package versions, official support status, source links,
and executable results for:

1. One structured tool call and final response through the configured
   OpenAI-compatible endpoint.
2. A deterministic fake client requiring no network.
3. Unknown tool rejection before application execution.
4. Malformed and out-of-range argument handling.
5. Maximum-round and maximum-call enforcement.
6. Per-result and cumulative output-size enforcement.
7. Caller cancellation, tool timeout, and whole-turn timeout.
8. No implicit retry after provider or tool failure.
9. Content-free telemetry and stable error mapping.
10. No Microsoft framework types outside the adapter composition boundary.

The spike compares implementation size and behavioral gaps for the smallest
Microsoft.Extensions.AI path and Agent Framework. It does not build councils,
memory, workflows, or production tools.

## Consequences if Accepted

Positive:

- v0.2 uses a maintained protocol abstraction without adopting a full agent
  platform prematurely.
- Product policy remains testable without a provider or framework.
- A later Agent Framework adapter can replace the implementation without
  changing API or domain contracts.

Negative:

- the current provider implementation will need adaptation,
- the application still owns a small policy wrapper around library invocation,
- tool behavior must be tested against the real configured endpoint because
  OpenAI compatibility is incomplete in practice.

## Revisit Triggers

Reconsider Agent Framework only when accepted scope requires at least one of:

- resumable workflows,
- explicit human approval checkpoints,
- multi-agent coordination,
- framework-managed context providers that remove demonstrated complexity,
- session capabilities that the application cannot provide safely and simply.

Any such change requires a superseding ADR. It must not silently move product
policy, persistent memory, or DFHack access into the framework.

