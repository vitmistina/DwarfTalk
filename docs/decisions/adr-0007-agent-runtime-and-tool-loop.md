# ADR-0007: Agent Runtime and Tool Loop for v0.2

Status: Proposed  
Date: 2026-06-21  
Related: ADR-0002, ADR-0003, ADR-0005  
Decision gate: `R2-001` in `docs/backlog/v0.2-backlog.md`

This ADR remains proposed until the missing live OpenAI-compatible tool-call
proof in `R2-001` is captured and reviewed.

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

R2-001 verified the current official Microsoft support and package state on
2026-06-21:

- `Microsoft.Extensions.AI` official Learn pages exist for the library,
  `IChatClient`, and `UseFunctionInvocation`, and explicitly document automatic
  function tool invocation on the `IChatClient` pipeline.
- Official NuGet feeds report current stable packages
  `Microsoft.Extensions.AI` `10.7.0`,
  `Microsoft.Extensions.AI.Abstractions` `10.7.0`, and
  `Microsoft.Extensions.AI.OpenAI` `10.7.0`.
- Microsoft Agent Framework official Learn pages exist for overview, agent
  types, hosting, and migration. They describe Agent Framework as the direct
  successor to Semantic Kernel and AutoGen, built around agents, sessions,
  middleware, memory, and workflows, and state that any inference service that
  provides `Microsoft.Extensions.AI.IChatClient` can be used to build agents.
- Official NuGet feeds report current stable
  `Microsoft.Agents.AI.Abstractions` `1.10.0`, while
  `Microsoft.Agents.AI.Hosting` remains preview-only at
  `1.10.0-preview.260610.1`.
- Official migration guidance exists at
  `https://learn.microsoft.com/en-us/agent-framework/migration-guide/from-semantic-kernel/`
  and
  `https://learn.microsoft.com/en-us/semantic-kernel/support/migration/agent-framework-rc-migration-guide`.
- Official NuGet feeds report current stable `Microsoft.SemanticKernel`
  `1.77.0`.

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

## Proposed direction

Use the smallest supported `Microsoft.Extensions.AI` path for the v0.2 tool
loop and keep the tool-loop abstraction application-owned.

For the R2-001 spike, Fortress Souls adds an application-owned probe seam above
provider transport and implements it in the LLM adapter with:

- `Microsoft.Extensions.AI` `10.7.0`,
- application-owned contracts and stable errors in `FortressSouls.Application`,
- an internal OpenAI-compatible `IChatClient` bridge inside `FortressSouls.Llm`,
- `AIFunctionFactory` for typed tool contracts inside the adapter boundary.

Do not expand or replace `IChatProvider`. ADR-0005 remains intact: the existing
plain-text provider seam stays responsible only for v0.1 chat transport.

Do not adopt Microsoft Agent Framework for v0.2. The spike showed that Agent
Framework would still depend on the same `IChatClient` transport bridge for the
configured OpenAI-compatible endpoint while adding agent, session, middleware,
memory, and workflow surface that Fortress Souls does not need for the bounded
v0.2 loop. The current hosting package remaining preview-only increases that
risk.

Do not adopt Semantic Kernel for new v0.2 agent work. Official Microsoft
migration guidance now points new agent capability investment toward Agent
Framework, which would create avoidable migration churn for Fortress Souls.

Do not implement a custom provider-protocol loop unless a later accepted item
proves that the Microsoft.Extensions.AI path cannot satisfy a documented
runtime requirement. Any such fallback must remain behind the same
application-owned boundary.

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

R2-001 proves this boundary with a smaller executable seam,
`IToolLoopProbe`, rather than routing the existing chat API through an
unfinished v0.2 runtime.

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

## R2-001 Evidence

Official source links verified on 2026-06-21:

- `https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai`
- `https://learn.microsoft.com/en-us/dotnet/ai/ichatclient`
- `https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.functioninvokingchatclientbuilderextensions.usefunctioninvocation`
- `https://learn.microsoft.com/en-us/agent-framework/overview/`
- `https://learn.microsoft.com/en-us/agent-framework/agents/`
- `https://learn.microsoft.com/en-us/agent-framework/get-started/hosting`
- `https://learn.microsoft.com/en-us/agent-framework/migration-guide/from-semantic-kernel/`
- `https://learn.microsoft.com/en-us/semantic-kernel/support/migration/agent-framework-rc-migration-guide`

Executable repository evidence:

1. One structured tool call and final response through the configured
   OpenAI-compatible protocol shape.
   Result: proven with a deterministic stub of the exact
   `/chat/completions` request and response exchange in
   `OpenAiCompatibleToolLoopChatClientTests`. Live OpenRouter proof is still
   blocked locally because no `.env` file and no loaded
   `FortressSouls__Llm__ApiKey` were present in this environment.
2. A deterministic fake client requiring no network.
   Result: proven in `ToolLoopProbeTests.RunAsync_ExecutesOneToolCall_AndReturnsFinalAssistantMessage`.
3. Unknown tool rejection before application execution.
   Result: proven in `ToolLoopProbeTests.RunAsync_RejectsUnknownToolBeforeApplicationExecution`.
4. Malformed and out-of-range argument handling.
   Result: proven in
   `ToolLoopProbeTests.RunAsync_RejectsMalformedToolArgumentsBeforeApplicationExecution`
   and
   `ToolLoopProbeTests.RunAsync_RejectsOutOfRangeToolArgumentsWithoutRetry`.
5. Maximum-round and maximum-call enforcement.
   Result: proven in `ToolLoopProbeTests.RunAsync_EnforcesRoundLimit` and
   `ToolLoopProbeTests.RunAsync_EnforcesToolCallLimit`.
6. Per-result and cumulative output-size enforcement.
   Result: proven in `ToolLoopProbeTests.RunAsync_EnforcesPerResultBudget` and
   `ToolLoopProbeTests.RunAsync_EnforcesTotalResultBudget`.
7. Caller cancellation, tool timeout, whole-turn timeout, and provider timeout.
   Result: proven in `ToolLoopProbeTests.RunAsync_HonorsCallerCancellation`,
   `ToolLoopProbeTests.RunAsync_EnforcesToolTimeout`, and
   `ToolLoopProbeTests.RunAsync_EnforcesTurnTimeout`, plus
   `OpenAiCompatibleToolLoopChatClientTests.RunAsync_MapsProviderTimeoutToStableProbeTimeoutAndTelemetryCategory`.
8. No implicit retry after provider or tool failure.
   Result: proven for provider failure by
   `OpenAiCompatibleToolLoopChatClientTests.RunAsync_MapsOpenAiCompatibleTransportFailureToStableErrorWithoutRetry`,
   which asserts a single outbound request, and for tool failure by
   `ToolLoopProbeTests.RunAsync_RejectsOutOfRangeToolArgumentsWithoutRetry`,
   which asserts one tool execution and no follow-up model request.
9. Content-free telemetry and stable error mapping.
   Result: proven in `ToolLoopProbeTests.RunAsync_EmitsContentFreeTelemetry`
   and the same transport-failure test above.
10. No Microsoft framework types outside the adapter composition boundary.
    Result: proven in `ArchitectureTests.NonAdapterAssemblies_DoNotReferenceMicrosoftAgentOrChatPackages`.

Implementation-size and behavior comparison:

- Smallest `Microsoft.Extensions.AI` path: one new adapter package,
  application-owned contracts, deterministic fake tests, and no framework DTO
  leakage outside `FortressSouls.Llm`. The automatic function-invocation layer
  is useful, but Fortress Souls still needs an application-owned wrapper for
  whole-turn timeout, cumulative byte budgets, safe receipts, and stable error
  taxonomy.
- Microsoft Agent Framework: still requires the same `IChatClient` model bridge
  for our provider, adds broader agent/session/workflow concepts, and currently
  leaves the hosting package in preview.
- Semantic Kernel: broader surface than the slice requires and official
  migration guidance now exists toward Agent Framework for agent capabilities.

Repository blocker:

- R2-001 is not fully complete in this environment because the required live
  OpenAI-compatible tool-call proof could not run safely. The exact blocker is
  the absence of both `.env` and a loaded provider API key.

## Consequences if Accepted

Positive:

- v0.2 uses a maintained protocol abstraction without adopting a full agent
  platform prematurely.
- Product policy remains testable without a provider or framework.
- A later Agent Framework adapter can replace the implementation without
  changing API or domain contracts.

Negative:

- the current provider implementation needed a parallel `IChatClient` bridge
  for the spike,
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
