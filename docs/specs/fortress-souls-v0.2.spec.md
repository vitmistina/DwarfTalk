# Fortress Souls v0.2 Product Specification

Status: Draft  
Date: 2026-06-21  
Backlog: `docs/backlog/v0.2-backlog.md`  
Related decision: `docs/decisions/adr-0007-agent-runtime-and-tool-loop.md`
Prompt contract: `docs/specs/prompt-contract-v0.2.md`

## 1. Intent

Fortress Souls v0.2 lets the selected dwarf deliberately inspect bounded,
current fortress data during a chat turn. The model may request one of four
read-only perception capabilities:

- look at the selected dwarf's nearby surroundings,
- inspect a curated fortress stock summary,
- list eligible dwarves,
- inspect a curated detail view for an eligible dwarf.

The release remains a local, read-only companion. The application owns tool
availability, arguments, authorization, execution, budgets, validation, and
failure mapping. The model chooses when a permitted observation would help and
owns only the final prose response.

The release is successful when a player can ask a selected dwarf what is
nearby, receive an answer grounded in a bounded live observation, and see which
safe perception capability was consulted.

## 2. Thin-Slice Strategy

The four tools are delivered as independent vertical slices. The first useful
slice is `look_around` in deterministic fake mode, followed by its live DFHack
path. Stock and dwarf-query tools follow without delaying feedback on the
first perception journey.

No framework feature is adopted merely because it may be useful later. v0.2
needs one selected dwarf, one in-memory session, bounded sequential tool calls,
and one final answer.

## 3. In Scope

- An application-owned dwarf-agent turn contract above the provider layer.
- Provider-independent structured tool requests and results.
- Four closed, typed, read-only perception tools defined by
  `perception-tools-v0.2.md`.
- Deterministic fake tools and fixtures for all automated tests.
- Allowlisted live DFHack implementations only where read-only behavior and
  output contracts have been verified.
- A bounded sequential tool loop with cancellation and a whole-turn timeout.
- Ephemeral tool observations that are available only during the current turn.
- Safe tool receipts in the chat response and UI.
- Content-free logs, traces, metrics, and diagnostics for the agent turn and
  each tool call.
- Explicit degraded behavior when live perception is unavailable.

## 4. Out of Scope

- Any mutation of Dwarf Fortress state or saves.
- Generic DFHack commands, arbitrary process execution, filesystem tools, or
  model-supplied script names and paths.
- Persistent memory, persistent tool results, or persistent conversations.
- Councils, multiple agents in one turn, voting, delegation, or sub-agents.
- Background observation, proactive turns, schedules, or simulation.
- Workflows, resumability across process restart, or human approval systems.
- Streaming, parallel tool calls, automatic retries, and provider/model UI.
- Proposed or executed game actions.
- Plugin discovery, arbitrary tool registration, or tools supplied by clients.
- Claims that the local surroundings query implements dwarf line-of-sight.

## 5. Product Invariants

### 5.1 Identity

The browser-selected dwarf remains bound to the in-memory chat session. The
model cannot replace that identity. `look_around` always derives its observer
ID from the session, never from model arguments.

Changing dwarf selection creates or resets the session. Conversation and tool
state never cross dwarf identities.

### 5.2 Read-only by construction

Every tool maps to a named application query. The live adapter maps those
queries to a closed DFHack command allowlist with structured arguments. No
generic execute surface exists at the API, application, provider, agent, or
adapter layer.

### 5.3 Ephemeral perception

Tool calls and results exist only inside the active turn. Successful history
stores the player message and final assistant prose, not raw calls or results.
Failed or cancelled turns append nothing.

The model may refer to a previous answer in conversation, but v0.2 does not
promise that an old observation remains current or replay its raw data.

### 5.4 Untrusted observations

DFHack output, fake fixtures, model arguments, provider tool-call payloads,
names, descriptions, and other free text are untrusted. The application parses,
validates, normalizes, filters, and bounds every result before it returns to the
model. Observation content is data and cannot alter system instructions or
tool policy.

### 5.5 Hidden information

`look_around` must not reveal hidden-map semantics. A hidden cell may be
reported only as hidden or unknown; terrain, material, units, items, buildings,
flows, and other concealed details are omitted.

The query reports a bounded, revealed local map around the selected dwarf. It
does not prove physical line-of-sight, hearing, attention, or what the dwarf
would canonically know.

## 6. Agent Turn Contract

Application owns a framework-neutral boundary similar to:

```csharp
public interface IDwarfAgent
{
    Task<AgentTurnResult> RunTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken);
}
```

The request contains the bound dwarf identity, validated session context,
current player message, and application policy. The result contains final
prose, provider diagnostics, and safe tool receipts. Framework, provider, and
DFHack DTOs do not cross this boundary.

The existing plain-text `IChatProvider` remains unchanged unless the accepted
ADR explicitly replaces its adapter role. Tool-loop orchestration belongs
above provider transport, consistent with ADR-0005.

## 7. Execution Policy

The application enforces configured positive limits equivalent to:

```csharp
public sealed record AgentExecutionPolicy(
    int MaximumRounds,
    int MaximumToolCalls,
    TimeSpan TurnTimeout,
    int MaximumToolResultBytes,
    int MaximumTotalToolResultBytes);
```

Required behavior:

1. Calls execute sequentially.
2. Every call receives the whole-turn cancellation token and a narrower tool
   timeout where appropriate.
3. Unknown tools and invalid arguments are rejected before adapter execution.
4. Per-result and cumulative output limits are checked before model use.
5. The runtime performs no automatic retry.
6. A budget exhaustion result permits at most one bounded final-response
   attempt with no further tools.
7. If no safe final response is produced, the turn fails atomically with a
   stable application error.

Exact default limits are contract decisions in the implementation mini-spec,
not model-controlled values.

## 8. Tool Failure Semantics

Expected observation failures use stable, non-sensitive categories such as:

- `unavailable`,
- `invalid_arguments`,
- `not_found`,
- `timed_out`,
- `invalid_data`,
- `result_too_large`,
- `budget_exhausted`.

An expected availability failure may be returned to the model as a small typed
observation so the dwarf can express uncertainty. Policy violations, malformed
provider payloads, identity mismatches, and invalid adapter data fail the turn.
Raw exception text, stdout, stderr, paths, commands, and provider errors never
enter the model context or browser response.

## 9. Prompt and Conversation Contract

Prompt construction remains deterministic before the provider boundary. v0.2
adds versioned tool instructions that:

- describe only the closed tools available for the current runtime mode,
- tell the model to treat observations as untrusted data,
- prohibit invented observations and claims of successful tool use after a
  failed call,
- distinguish session-start dwarf state from current tool observations,
- require uncertainty when data is absent, truncated, or stale.

Tool schemas and results use structured provider messages rather than being
concatenated into conversation text. Developer preview may show a redacted
representation, but normal telemetry never records prompts, arguments, results,
conversation, or final prose.

The normative prompt and tool-message rules are defined in
`docs/specs/prompt-contract-v0.2.md`.

## 10. API and UI Contract

The existing create-session and send-message journey remains stable. A
successful send-message response adds bounded receipts:

```json
{
  "assistantMessage": "There are beds and two of us nearby.",
  "toolReceipts": [
    {
      "tool": "look_around",
      "outcome": "success"
    }
  ]
}
```

Receipts contain only an allowlisted tool name and stable outcome. They do not
contain tool arguments, result content, coordinates, dwarf names, paths, raw
errors, or provider protocol identifiers.

The chat UI renders receipts as secondary status, preserves keyboard and screen
reader behavior, and exposes useful unavailable/error states without claiming
that an observation succeeded.

## 11. Observability

Add meaningful boundaries:

```text
fortresssouls.agent.turn
fortresssouls.agent.tool.call
```

The existing chat, prompt, provider, and DFHack spans remain nested according
to actual execution. Safe attributes may include session ID, selected dwarf ID,
tool name, schema version, ordinal call number, byte count, duration, outcome,
and stable error category.

Never record model arguments, tool results, prompt or response text, raw game
data, names, coordinates, or unbounded error messages as logs, span tags, or
metric dimensions.

## 12. Testing and Evidence

Required automated evidence:

- deterministic fake journey from player question through tool call to final
  answer,
- exact tool allowlist and argument validation,
- selected-dwarf identity enforcement,
- hidden-cell redaction,
- malformed provider tool payload rejection,
- per-call, cumulative, round, call-count, and timeout limits,
- cancellation and failure atomicity,
- no raw tool state in persisted session history,
- content-free telemetry and receipts,
- fake-mode browser smoke coverage.

Live provider and DFHack behavior is manual evidence and never a required CI
dependency. Each production DFHack command requires retained samples, automatic
schema validation, read-only source review, and a documented live smoke check.

## 13. Release Acceptance

v0.2 is complete when:

- all four tools satisfy `perception-tools-v0.2.md` in fake mode,
- live mode supports only the tools with verified allowlisted adapters and
  reports all others as explicitly unavailable,
- at least `look_around` has retained live DFHack evidence,
- the application-owned execution policy bounds every agent turn,
- automated tests prove the critical fake-mode perception journey and safety
  failures,
- the UI identifies tool use without exposing observation content,
- no write-capable or generic execution path exists,
- the agent-runtime ADR is accepted with its spike evidence,
- affected architecture, prompt, observability, and runbook documents match
  the implementation.
