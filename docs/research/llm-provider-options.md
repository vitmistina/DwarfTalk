# LLM Provider Options

Status: Accepted for v0.1  
Spike: R-003  
Date: 2026-06-19

## Question

What real LLM provider should v0.1 implement first?

## v0.1 constraints

DwarfTalk v0.1 is deliberately simple.

v0.1 supports:

- plain chat turns,
- one selected dwarf,
- one configured model,
- deterministic prompt assembly,
- model output as prose only.

v0.1 does not support:

- model picker,
- streaming,
- tool calling,
- memory,
- agent runtime,
- persistent conversations,
- model-to-DFHack access,
- game mutation,
- provider marketplace.

Provider-specific DTOs must not leak into domain or application modules.

## Options considered

### 1. Direct OpenAI provider

Pros:

- High-quality models.
- Stable official platform.
- Good documentation.
- Good future path if OpenAI-native features become useful.

Cons:

- Less useful for testing multiple cheap or open models.
- Could tempt the app toward provider-specific APIs too early.
- Does not prove the OpenAI-compatible seam as broadly.

### 2. OpenAI-compatible provider abstraction

Pros:

- Works with OpenRouter and many local/cloud endpoints.
- Keeps v0.1 provider implementation simple.
- Fits the existing `IChatProvider` seam.
- Lets us swap endpoint and model through configuration.
- Helps future local model support.

Cons:

- OpenAI-compatible does not mean perfectly identical.
- Provider differences still need defensive handling.
- Some endpoints may only partially implement the expected API.

### 3. Local OpenAI-compatible endpoint

Examples:

- LM Studio,
- Ollama-compatible bridges,
- local OpenAI-compatible servers.

Pros:

- Useful future path for local/privacy testing.
- Can probably reuse the same `OpenAiCompatibleChatProvider`.
- Avoids cloud dependency during some experiments.

Cons:

- Not the first priority for v0.1.
- Local model quality and speed vary.
- Adds setup friction before the product loop is proven.

## Decision

Use `OpenAiCompatibleChatProvider` as the first real provider implementation.

Use OpenRouter as the first configured endpoint.

Use one default v0.1 model:

```text
deepseek/deepseek-v3.2
```

Keep `FakeChatProvider` as the default for tests and offline development.

## Rationale

OpenRouter gives the project one API key, one OpenAI-compatible endpoint, and access to many cheap model experiments.

For v0.1, the important thing is not finding the perfect model. The important thing is proving that the app can replace `FakeChatProvider` with a real provider without changing:

- chat orchestration,
- prompt assembly,
- dwarf-state extraction,
- domain contracts,
- application use cases.

The provider should remain a small model invocation seam, not an agent runtime in disguise.

## Default v0.1 model policy

Default model:

```text
deepseek/deepseek-v3.2
```

Rationale:

- cheap enough for many dwarf chat turns,
- strong enough for coherent plain roleplay,
- large enough context for dwarf snapshot plus active conversation,
- suitable as a pragmatic first default.

Runner-up if DeepSeek feels too stiff:

```text
mistralai/mistral-small-3.2-24b-instruct
```

Experimental roleplay model, not default:

```text
cognitivecomputations/dolphin-mistral-24b-venice-edition:free
```

Model choice is deliberately configuration, not code.

## Configuration contract

The provider configuration should expose only the following fields for v0.1:

```text
ProviderType
Endpoint
Model
ApiKey
MaxOutputTokens
Temperature
TimeoutSeconds
```

Do not add provider marketplace features in v0.1.

## Future direction

Future agentic behaviour should be added through an application-level `AgentRuntime`, not by expanding `IChatProvider` into a tool, memory, or orchestration interface.

`IChatProvider` remains responsible for model invocation only.

Future `AgentRuntime` may own:

- read-only tool selection,
- memory retrieval,
- memory writing,
- `.look(...)` behaviour,
- turn planning,
- policy checks,
- human approval flows.

The provider is the mouth. The future agent runtime is the nervous system. Do not sew them together too early.

## Non-decisions

This spike does not decide:

- long-term model vendor,
- streaming support,
- tool calling API,
- memory storage,
- local model runtime,
- paid model comparison framework,
- agent runtime design,
- prompt memory strategy.
