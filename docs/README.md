# Fortress Souls documentation index

This folder contains project-level documentation that should be stable across research spikes and implementation work.

## v0.1 selection contract

The backend lists eligible dwarves and returns snapshots by validated dwarf ID.
The player selects a dwarf in the web UI. Fortress Souls does not read or
depend on the unit currently highlighted in the Dwarf Fortress UI.

## Draft v0.2 perception work

v0.2 is a draft release plan for bounded, read-only perception during a
selected dwarf's chat turn. It preserves application ownership of identity,
policy, budgets, validation, and telemetry.

Authoritative draft documents:

- `specs/fortress-souls-v0.2.spec.md`
- `specs/perception-tools-v0.2.md`
- `specs/prompt-contract-v0.2.md`
- `backlog/v0.2-backlog.md`
- `decisions/adr-0007-agent-runtime-and-tool-loop.md`

ADR-0007 remains Proposed until R2-001 verifies current Microsoft package
guidance and executable provider behavior.

## Decisions

- `decisions/adr-0003-dfhack-adapter.md` records the accepted v0.1 DFHack adapter invocation strategy.
- `decisions/adr-0006-coding-model-routing-and-mini-specs.md` records the mini-spec lifecycle and model-routing policy.
- `decisions/adr-0005-llm-provider-strategy.md` records the accepted v0.1 LLM provider strategy.
- `decisions/adr-0007-agent-runtime-and-tool-loop.md` records the proposed v0.2 tool-loop decision and evidence gate.

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
- `research/dfhack-field-map.md` records R-002A/B-019 field mapping decisions for the validated dwarf list and snapshot scripts.
- `research/dfhack-live-state-probes.md` records the deferred R-002 live-state probe findings for health, wounds, location, inventory, roles, and relationships.
- `research/dfhack-spatial-stock-spikes-2026-06-21.md` records R-003 live evidence for bounded spatial and exact stock queries.

## Runbooks

- `runbooks/dfhack-b019-manual-validation.md` describes the manual validation flow for the B-019 DFHack scripts.

## Repository placement conventions

- Production DFHack scripts live in `dfhack/scripts/fortress-souls/`.
- Adapter/sample JSON artifacts live in `dfhack/samples/`.
- Stable research findings live in `docs/research/`.
- Maintainer utilities live in `scripts/`.
- Temporary spike artifacts are deleted once their conclusions and canonical samples have been absorbed.
