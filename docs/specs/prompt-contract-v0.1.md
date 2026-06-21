# Prompt Contract v0.1

Status: Draft  
Parent backlog item: `docs/backlog/v0.1-backlog.md#b-012`  
Product spec reference: `docs/specs/fortress-souls-v0.1.spec.md` section 9

## Version identifiers

- Prompt template version: `fortress-souls-prompt-template.v0.2`
- Static interpretation guide version: `fortress-souls-interpretation-guide.v0.2`

The assembler MUST emit the template version in prompt text and diagnostics.

## System instruction contract

The system instruction MUST:

1. Require first-person speech as the selected dwarf.
2. Frame the player as a real but unexplained interlocutor rather than an automatic god, overseer, or friend.
3. Ground the voice in supplied work, personality, needs, values, and mannerisms without reciting raw trait labels by default.
4. Forbid invented events, hidden knowledge, and generic assistant behavior.

## Approved inputs

Only these validated inputs are allowed:

1. Browser-selected dwarf snapshot (validated `DwarfSnapshot` contract).
2. Current in-memory conversation messages for the active session.
3. Static interpretation guide text.
4. Current player message text.

Not allowed in prompt assembly:

- persistent memory,
- hidden fortress state,
- tool descriptions,
- API keys or secrets,
- raw DFHack output.

## Deterministic normalization and assembly

1. Normalize all inbound text line endings to `\n`.
2. Trim leading and trailing whitespace from each free-text input string.
3. Serialize snapshot and conversation JSON using application-owned serializer options.
4. Assemble sections in this exact order:
   1. `TEMPLATE_VERSION`
   2. `STATIC_GUIDE_VERSION`
   3. `SYSTEM`
   4. `DWARF_STATE_JSON`
   5. `INTERPRETATION_GUIDE`
   6. `CONVERSATION_JSON`
   7. `PLAYER_MESSAGE_JSON`
5. Conversation role values are lower-case (`player`, `assistant`).

## Bounds and budget policy

Deterministic bounds:

- `maxPromptCharacters = 10000`
- `maxConversationMessages = 12`
- `maxConversationMessageCharacters = 700`
- `maxPlayerMessageCharacters = 1200`
- `maxStaticGuideCharacters = 2000`

Deterministic truncation/rejection policy:

1. Truncate each message/guide/player text to its own max characters.
2. Keep only the newest `maxConversationMessages` conversation messages.
3. Assemble prompt.
4. If over `maxPromptCharacters`, drop oldest conversation messages one-by-one and reassemble until within budget.
5. If still over budget after conversation is empty, fail with stable category `PromptTooLarge`.

## Diagnostics contract (content-free)

Assembler diagnostics MUST include:

- `templateVersion`,
- `estimatedCharacterCount`,
- `estimatedTokenCount`,
- `conversationMessagesIncluded`,
- truncation flags,
- stable `failureCategory` (`None`, `ValidationError`, `PromptTooLarge`).

Diagnostics MUST NOT include prompt text or raw conversation/snapshot content.

## Observability requirements

Instrument `fortresssouls.prompt.assemble` with:

- span name: `fortresssouls.prompt.assemble`,
- span tag: `fortresssouls.prompt.template_version`,
- span outcome tag (`fortresssouls.operation.outcome`),
- metric: `fortresssouls.prompt.tokens.estimated`,
- metric tags: template version, truncated flag, outcome.

Telemetry MUST remain content-free.
