# Fortress Souls

Fortress Souls v0.1 is a local, read-only companion for Dwarf Fortress. It
lists eligible dwarves, lets the player select one in the browser, fetches a
validated snapshot for that dwarf, assembles deterministic prompt context, and
supports an in-memory chat through one configured LLM provider.

## Read-only guarantee

- The browser owns dwarf identity selection from `GET /api/dwarves`.
- Snapshot and chat flows use the selected validated dwarf ID.
- The unit highlighted in the Dwarf Fortress UI is not an input.
- v0.1 does not expose game-mutation endpoints, arbitrary DFHack execution, or
  model tool calling.

## v0.1 boundaries

In scope:

- fake-mode local development and testing,
- dwarf list, selected dwarf snapshot, runtime status, and in-memory chat,
- safe provider and dwarf-adapter status surfaces,
- development-only prompt preview.

Out of scope:

- persistent chat or memory,
- streaming,
- multiple live providers or a model picker,
- background simulation,
- game mutation.

## Prerequisites

Required:

- .NET SDK matching `src/backend/global.json`,
- Node.js and npm for `src/frontend`,
- PowerShell on Windows.

Optional:

- DFHack for live adapter work,
- real-provider credentials for provider testing,
- Aspire Dashboard for local telemetry viewing.

## Supported local path

1. Optional: create a local root config when you want to override the tracked defaults.

   ```powershell
   Copy-Item .\fortress-souls.config.example.jsonc .\fortress-souls.config.jsonc
   ```

   `fortress-souls.config.example.jsonc` is the tracked source of commented
   non-secret defaults. The ignored `fortress-souls.config.jsonc` is the local
   single-pane override for adapter mode, ports, OTLP endpoint, DFHack path,
   and other non-secret runtime choices.

2. Optional: create `.env` from `.env.example` when you need secrets.

   ```powershell
   Copy-Item .\.env.example .\.env
   ```

   Keep `.env` for credentials such as `FortressSouls__Llm__ApiKey`.

3. Start the supported stack:

   ```powershell
   .\scripts\dev.ps1
   ```

   `dev` creates `fortress-souls.config.jsonc` from the tracked example when it
   is missing, then loads that local file plus `.env`. It also installs
   frontend dependencies automatically when `src/frontend/node_modules` is
   missing.

   If the default local ports are already in use, edit
   `fortress-souls.config.jsonc` and change `backend.port` and/or
   `frontend.port`, then rerun `dev`.

   Fake mode remains the default supported development path.

4. Run the full repository checks:

   ```powershell
   .\scripts\check.ps1
   ```

   `check` still expects frontend dependencies to exist. Running `dev` once in
   a fresh workspace satisfies that requirement.

When the backend returns a safe failure, relevant UI error states surface
`X-Correlation-ID`. Use that ID to inspect safe logs and traces; do not paste
secrets, prompt text, or model responses into reports.

## Optional modes

- Real provider mode is documented in `docs/runbooks/provider-configuration.md`.
- Optional live DFHack manual validation is documented in `docs/runbooks/dfhack-b019-manual-validation.md`.
- Fake mode remains the default supported path for day-to-day development.
- Mixed local runs are supported through root config plus `.env`, for example:
  `Fake + OpenAiCompatible`, `JsonFile + Fake`, and `DfHackProcess + OpenAiCompatible`.

## Canonical commands

PowerShell:

```powershell
.\scripts\dev.ps1
.\scripts\format.ps1
.\scripts\test.ps1
.\scripts\check.ps1
```

POSIX:

```bash
./scripts/dev.sh
./scripts/format.sh
./scripts/test.sh
./scripts/check.sh
```

## More documentation

- [Local development runbook](docs/runbooks/local-dev.md)
- [Provider configuration runbook](docs/runbooks/provider-configuration.md)
- [DFHack manual validation runbook](docs/runbooks/dfhack-b019-manual-validation.md)
- [Architecture overview](docs/architecture/0001-architecture-overview.md)
- [v0.1 product spec](docs/specs/fortress-souls-v0.1.spec.md)
- [Documentation index](docs/README.md)
