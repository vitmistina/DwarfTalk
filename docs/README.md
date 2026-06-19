# Fortress Souls documentation index

This folder contains project-level documentation that should be stable across research spikes and implementation work.

## Decisions

- `decisions/adr-0003-dfhack-adapter.md` records the accepted v0.1 DFHack adapter invocation strategy.
- `decisions/adr-0005-llm-provider-strategy.md` records the accepted v0.1 LLM provider strategy.

## LLM provider strategy

v0.1 uses `FakeChatProvider` by default.

The first real provider target is OpenRouter through `OpenAiCompatibleChatProvider`.

Default configured model:

```text
deepseek/deepseek-v3.2
```

v0.1 intentionally supports only one configured model.

Not included in v0.1:

- model picker,
- streaming,
- tool calling,
- memory,
- agent runtime,
- provider marketplace,
- game mutation.

See:

- `research/llm-provider-options.md`
- `runbooks/provider-configuration.md`
- `decisions/adr-0005-llm-provider-strategy.md`

## Research

- `research/dfhack-command-invocation.md` records R-001 manual verification of safe DFHack command invocation.
- `research/dfhack-field-map.md` records R-002/B-019 field mapping decisions for the validated dwarf list and snapshot scripts.

## Runbooks

- `runbooks/dfhack-b019-manual-validation.md` describes the manual validation flow for the B-019 DFHack scripts.

## Repository placement conventions

- Production DFHack scripts live in `dfhack/scripts/fortress-souls/`.
- Adapter/sample JSON artifacts live in `dfhack/samples/`.
- Research-only probes and spike-specific notes remain under `research/`.
