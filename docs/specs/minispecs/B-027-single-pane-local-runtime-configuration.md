# B-027: Add Single-Pane Local Runtime Configuration

Status: Done
Parent backlog: `docs/backlog/v0.1-backlog.md#b-027`
Product specification: `docs/specs/fortress-souls-v0.1.spec.md`
Primary module: Developer tooling and local runtime configuration
Risk class: Medium
Recommended implementation model: Mechanical implementation tier (GPT-5.4 high)
Recommended reasoning level: Medium
Recommended review model: Bounded review tier (GPT-5.4 high)
Human checkpoint: No

## Observable Outcome

A developer can run Fortress Souls from one root non-secret config plus one root
secret file, switch supported fake/live combinations without editing scattered
project files, and start the app through one canonical `dev` command.

## Why This Slice Exists

The v0.1 runtime already supported fake and live combinations, but the setup
surface was fragmented across launch profiles, environment-variable snippets,
and runbooks. Early feedback sessions need one obvious place to configure and
run the app without losing the existing read-only and redaction boundaries.

## Context To Load

Required context:

- `AGENTS.md`, this mini-spec, and B-027
- `docs/architecture/0001-architecture-overview.md`
- `docs/specs/fortress-souls-v0.1.spec.md`
- `docs/decisions/adr-0003-dfhack-adapter.md`
- `docs/decisions/adr-0004-observability.md`
- `docs/decisions/adr-0005-llm-provider-strategy.md`
- `docs/runbooks/local-dev.md`
- `docs/runbooks/provider-configuration.md`
- existing `scripts/dev.*`, `scripts/test.*`, root config examples, and current backend/frontend config seams

Conditional context:

- `docs/runbooks/dfhack-b019-manual-validation.md` when validating live DFHack mode.

## Existing State To Inspect

Before this item, local startup depended on launch-profile settings plus manual
environment-variable snippets in documentation. `.env.example` carried several
non-secret provider defaults even though they were not credentials. Recheck the
startup scripts, `launchSettings.json`, provider and adapter options, Vite dev
proxy behavior, and existing safe status endpoints before editing.

## In Scope

- Add one tracked root config example for non-secret local runtime settings and one ignored root local config file name.
- Keep root `.env` for secrets only and trim `.env.example` accordingly.
- Load the root config plus `.env` through one shared helper used by both `scripts/dev.ps1` and `scripts/dev.sh`.
- Centralize backend port, frontend port, provider defaults, adapter defaults, DFHack pathing, and local OTLP endpoint in the root config.
- Derive duplicate values such as frontend proxy target and DFHack working directory where practical.
- Stop the supported `dev` path from depending on mutable launch-profile settings.
- Add focused automated tests for the config helper and update docs/backlog artifacts.

## Out Of Scope

- No browser-side settings page, no secret editor, no YAML support, no profile library, and no production deployment config redesign.
- No changes to backend domain behavior, DFHack command allowlists, provider protocol, or prompt assembly.
- No live-provider or live-DFHack automated tests.

## Boundaries And Invariants

- The application runtime contract remains environment-variable based; the root config is a friendlier authoring surface, not a second backend config API.
- Keep secrets out of tracked files and out of startup summaries.
- Keep low-level safety bounds, loopback validation rules, and service identity internal rather than promoting them into the user-edited root config.
- Fake mode remains the default supported development path.

## Implementation Slices

### Slice 1: Shared root config resolver

- Intended behavior: one helper reads `fortress-souls.config.jsonc` when present, falls back to `fortress-souls.config.example.jsonc`, merges allowed `.env` secrets, and projects the result into the existing env keys.
- Likely files or modules touched: `scripts/dev-config.mjs`, focused node tests, root config example files.
- Test-first evidence: focused node tests cover JSONC comment handling, derived values, `.env` merging, and example fallback.
- Completion evidence: helper tests pass and the real repo example config loads successfully.

### Slice 2: Canonical startup wiring

- Intended behavior: `scripts/dev.ps1` and `scripts/dev.sh` use the shared helper, auto-install frontend dependencies when needed, print the active config summary, and start backend/frontend from root-config-derived ports.
- Likely files or modules touched: `scripts/dev.ps1`, `scripts/dev.sh`, `launchSettings.json`.
- Test-first evidence: the helper tests protect the projection logic; script validation uses a real startup run plus health check.
- Completion evidence: one `dev` command starts the app with the expected mode and derived URLs.

### Slice 3: Documentation and planning alignment

- Intended behavior: README, runbooks, backlog, and mini-spec reflect the new root-config startup flow and the `.env` secrets split.
- Likely files or modules touched: README, runbooks, backlog, this mini-spec.
- Test-first evidence: documentation exception; validate with executable startup checks and structural review of the changed paths and commands.
- Completion evidence: docs describe the same commands and file ownership that the scripts actually implement.

## Acceptance Criteria

- [ ] One tracked root config example documents the supported non-secret local runtime settings.
- [ ] One ignored root local config file name is the supported single-pane override for non-secret local settings.
- [ ] `.env.example` becomes secrets-first and `.env` remains the local secret file.
- [ ] `scripts/dev.ps1` and `scripts/dev.sh` project the root config plus `.env` into the existing backend/frontend environment-variable seams.
- [ ] The supported `dev` path no longer depends on mutable launch-profile OTLP settings.
- [ ] Focused automated tests cover the helper's JSONC parsing, fallback, and derived values.
- [ ] Documentation explains fake/live mixed runs through root config plus `.env`.

## Test Strategy

Use the cheapest reliable level:

- node tests for pure config parsing and mapping logic,
- real script startup plus health check for the startup path,
- existing backend/frontend suites unchanged unless helper integration breaks them.

Do not add live provider or live DFHack automation to validate this item.

## Observability And Failure Behaviour

Startup prints only a safe summary: config file path, adapter type, provider
type, derived backend/frontend URLs, and whether OTLP or console fallback is
active. It never prints API keys, OTLP headers, prompts, or provider content.

## Validation

Focused checks:

```powershell
node --test .\scripts\dev-config.test.mjs
node .\scripts\dev-config.mjs summary .
```

Behavior check:

```powershell
.\scripts\dev.ps1
Invoke-RestMethod -Uri "http://127.0.0.1:5230/api/health"
```

Relevant repository checks:

```powershell
.\scripts\test.ps1
```

## Stop Conditions

- Stop before adding YAML or a browser config UI; those are separate scope decisions.
- Stop if root config would require changing accepted read-only safety boundaries.
- Stop if supporting the new config would require weakening existing validation or redaction behavior.

## Completion Report

Report: 1. outcome and design decisions; 2. changed files; 3. validation commands and results; 4. known limitations, unverified assumptions, and remaining manual checks.
