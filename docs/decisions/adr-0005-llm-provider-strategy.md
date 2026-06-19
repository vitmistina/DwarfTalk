# ADR-0005: LLM Provider Strategy for v0.1

Status: Accepted  
Date: 2026-06-19

## Context

DwarfTalk v0.1 needs one real LLM provider for plain text dwarf chat.

The application must preserve the deterministic/probabilistic seam:

| Concern | Owner |
| --- | --- |
| Dwarf state extraction | Deterministic application code |
| Prompt assembly | Deterministic application code |
| Configuration and secrets | Deterministic application code |
| LLM response generation | Probabilistic model |
| Displaying response | Deterministic application code |
| Mutating Dwarf Fortress | Not allowed in v0.1 |

v0.1 explicitly excludes:

- memory,
- tools,
- streaming,
- model selection UI,
- game mutation,
- agentic multi-step reasoning.

## Decision

Implement the first real provider as:

```text
OpenAiCompatibleChatProvider
```

Use OpenRouter as the first configured endpoint:

```text
https://openrouter.ai/api/v1
```

Use one default model for v0.1:

```text
deepseek/deepseek-v3.2
```

Keep `FakeChatProvider` as the default for tests and offline development.

Do not implement in v0.1:

- model picker,
- streaming,
- tool calling,
- memory,
- agent runtime,
- provider marketplace,
- direct OpenAI Responses API integration.

## Consequences

Positive:

- Simple implementation path.
- Cheap model experimentation.
- Keeps provider details out of domain and application modules.
- Supports future local or alternate OpenAI-compatible endpoints.
- Preserves `IChatProvider` as a small seam.
- Makes B-016 implementation straightforward.

Negative:

- OpenRouter-specific operational quirks may appear.
- Model slugs and prices may change.
- OpenAI-compatible does not mean perfectly identical across providers.
- Some provider-specific features will remain inaccessible in v0.1.

## Future evolution

Future agentic behaviour must be introduced above the provider layer, through an application-level `AgentRuntime`.

`IChatProvider` remains responsible only for model invocation.

Future `AgentRuntime` may own:

- read-only tools,
- memory retrieval,
- memory writing,
- `.look(...)` behaviour,
- turn planning,
- policy checks,
- human approval flows.

Do not grow `IChatProvider` into an agent orchestration interface.

## Safety

The provider must never log:

- API keys,
- Authorization headers,
- full provider request headers,
- full prompts by default,
- full LLM responses by default.

The model must not receive direct access to:

- DFHack commands,
- filesystem commands,
- shell execution,
- write-capable game APIs.

There must be no endpoint equivalent to:

```text
POST /api/dfhack/execute
```

That is not a debug endpoint. That is a tiny cursed crown.
