# Local Development Runbook

Status: Draft  
Applies to: FortressSouls v0.1  
Related ADR: `docs/decisions/adr-0004-observability.md`

## Purpose

Keep local startup repeatable for the implemented v0.1 product loop without
turning the modular monolith into a cluster of accidental services.

Fake mode is the default supported development path. JSON-file mode and live
DFHack mode remain optional for targeted validation work.

## Prerequisites

Required:

- .NET SDK matching the project target,
- Node.js and npm for the frontend,
- PowerShell on Windows.

Fresh workspaces need one frontend restore before canonical dev, format, test,
or check commands can succeed:

```powershell
cd .\src\frontend
npm install
cd ..\..
```

`src/frontend/package-lock.json` is committed, but local packages are not
vendored. `scripts/dev.*` now installs the frontend dependencies automatically
when `src/frontend/node_modules` is missing. Run the manual restore only when
you want to execute `format`, `test`, or `check` before the first `dev` run.

If local script execution is blocked, run the commands with
`powershell -ExecutionPolicy Bypass -File ...` for the current session.

Optional:

- Docker Desktop, if using the Aspire Dashboard container,
- Aspire CLI, if using `npx -y @microsoft/aspire-cli dashboard run`,
- DFHack, if validating the live adapter,
- provider credentials, if validating the real provider.

## Default local endpoints

- Frontend dev base from the tracked config defaults: `http://127.0.0.1:5173`
- Backend HTTP base from the tracked config defaults: `http://127.0.0.1:5230`
- Health: `GET /api/health`
- Provider status: `GET /api/provider/status`
- Dwarf adapter status: `GET /api/dwarves/adapter-status`
- Dwarf list: `GET /api/dwarves`
- Dwarf snapshot: `GET /api/dwarves/{dwarfId}/snapshot`
- Create chat session: `POST /api/chat/sessions`
- Send chat message: `POST /api/chat/sessions/{sessionId}/messages`
- Prompt preview: `GET /api/chat/sessions/{sessionId}/prompt-preview`
  in development only

The backend and frontend ports above come from
`fortress-souls.config.example.jsonc`. If you change the ports in your local
root config, the derived URLs change with them.

## Root configuration files

Supported local startup now uses three root files:

- `fortress-souls.config.example.jsonc`: tracked commented defaults for
  non-secret local runtime settings.
- `fortress-souls.config.jsonc`: ignored local single-pane override for
  non-secret settings such as adapter mode, ports, OTLP endpoint, JSON-file
  paths, and DFHack pathing.
- `.env.example` and `.env`: tracked example plus ignored local secrets.

`scripts/dev.*` creates `fortress-souls.config.jsonc` from the tracked example
when the local file is missing, then loads that local file and merges allowed
secret keys from `.env`.

Use the root config for:

- backend and frontend dev ports,
- provider type, endpoint, model, and token/temperature/timeout defaults,
- adapter type,
- JSON-file sample paths,
- DFHack `runPath`, optional `workingDirectory`, host, and port,
- local OTLP endpoint.

If the default local ports are already occupied, edit `backend.port` and
`frontend.port` in `fortress-souls.config.jsonc` and rerun `scripts/dev.*`.

Use `.env` for:

- `FortressSouls__Llm__ApiKey`,
- any future OTLP auth headers or similar secret overrides.

## Mode recipes

### Fake plus Fake

No local files are required. The tracked example config already defaults to:

- `dwarfFortress.adapterType = Fake`
- `llm.providerType = Fake`

### Fake plus OpenAiCompatible

In `fortress-souls.config.jsonc`:

```jsonc
{
  "llm": {
    "providerType": "OpenAiCompatible",
  },
}
```

In `.env`:

```powershell
FortressSouls__Llm__ApiKey=
```

### JsonFile plus Fake

In `fortress-souls.config.jsonc`:

```jsonc
{
  "dwarfFortress": {
    "adapterType": "JsonFile",
    "jsonFile": {
      "dwarfListPath": "samples/snapshots/fake-dwarves-list.v0.1.json",
      "dwarfSnapshotPath": "samples/snapshots/fake-dwarf-4101.v0.1.json",
    },
  },
  "llm": {
    "providerType": "Fake",
  },
}
```

`dwarfSnapshotPath` points to one specific snapshot file. The adapter checks
that the browser-selected dwarf ID matches both `requestedUnitId` and
`identity.id` inside that file.

Do not pair a multi-dwarf list with one fixed snapshot file unless the list
contains only that same dwarf. For predictable browser behavior, either keep
the JSON-file list to the single dwarf represented by the snapshot file or use
`Fake` or `DfHackProcess` mode when you want to switch between multiple dwarves.

### DfHackProcess plus Fake

In `fortress-souls.config.jsonc`:

```jsonc
{
  "dwarfFortress": {
    "adapterType": "DfHackProcess",
    "dfHack": {
      "runPath": "C:/Program Files (x86)/Steam/steamapps/common/DFHack/hack/dfhack-run.exe",
      "workingDirectory": "",
      "host": "127.0.0.1",
      "port": 5000,
    },
  },
  "llm": {
    "providerType": "Fake",
  },
}
```

Leave `workingDirectory` empty to derive it from `runPath`.

This optional mode assumes the validated repo scripts under
`dfhack/scripts/fortress-souls/` have already been manually copied into the
DFHack runtime scripts path. Use the manual preparation flow in
`docs/runbooks/dfhack-b019-manual-validation.md`; `scripts/import-dfhack-scripts.ps1`
is a maintainer sync-back helper, not the setup step.

### DfHackProcess plus OpenAiCompatible

Combine the DFHack config above with:

- `llm.providerType = OpenAiCompatible` in root config,
- `FortressSouls__Llm__ApiKey` in `.env`.

Set `observability.otlpEndpoint` in the root config when you want Aspire
Dashboard traces and metrics. Set it to an empty string to stay on console
fallback only. Telemetry export must never be required for the app to start.

## Start Aspire Dashboard

### Option A: Aspire CLI

```powershell
npx -y @microsoft/aspire-cli dashboard run --allow-anonymous
```

Then open:

```text
http://localhost:18888
```

The dashboard receives OTLP/gRPC on:

```text
http://localhost:4317
```

and OTLP/HTTP on:

```text
http://localhost:4318
```

### Option B: Docker

```powershell
docker run --rm -it `
  -p 18888:18888 `
  -p 4317:18889 `
  -p 4318:18890 `
  -e ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true `
  --name aspire-dashboard `
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

Then open:

```text
http://localhost:18888
```

## Canonical scripts

Use the repository wrappers from the root:

```powershell
.\scripts\dev.ps1
.\scripts\format.ps1
.\scripts\test.ps1
.\scripts\check.ps1
```

On POSIX shells, use:

```bash
./scripts/dev.sh
./scripts/format.sh
./scripts/test.sh
./scripts/check.sh
```

Run `npm install` once in `src/frontend` before the first `dev`, `format`,
`test`, or `check` command in a fresh workspace.

`dev` starts the backend and frontend together. It projects the root config
plus `.env` into the existing environment-variable seams, prints a safe summary
of the active adapter/provider/observability mode, and installs frontend
dependencies automatically when needed.

Focused fake-mode browser smoke test:

```powershell
cd .\src\frontend
npm run test:e2e:install
npm run test:e2e
```

The smoke harness starts backend and frontend in fake mode on controlled ports
with readiness polling:

- backend `http://127.0.0.1:5230`
- frontend `http://127.0.0.1:5173`

## Start backend directly

Use direct backend startup only for troubleshooting. The supported local path
is still `scripts/dev.*`, because that is what projects the root config into
the backend's existing environment-variable contract.

Default fake-mode troubleshooting example from the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --no-launch-profile --project .\src\backend\FortressSouls.Api\FortressSouls.Api.csproj --urls http://127.0.0.1:5230
```

If you bypass `scripts/dev.*` for a non-default mode, export the equivalent
environment values yourself first.

Useful local checks:

```text
GET http://127.0.0.1:5230/api/health
GET http://127.0.0.1:5230/api/provider/status
GET http://127.0.0.1:5230/api/dwarves/adapter-status
```

Prompt preview remains development-only:

```text
GET http://localhost:5230/api/chat/sessions/{sessionId}/prompt-preview
```

Responses should include `X-Correlation-ID`. Use that ID for safe diagnostics.

## Start frontend directly

From the repository root:

```powershell
cd .\src\frontend
npm install
$env:FORTRESS_SOULS_FRONTEND_PORT = "5173"
$env:FORTRESS_SOULS_API_PROXY_TARGET = "http://127.0.0.1:5230"
npm run dev -- --host 127.0.0.1 --port 5173 --strictPort
```

When the backend port differs from the tracked defaults, update
`FORTRESS_SOULS_API_PROXY_TARGET` to match.

The frontend is no longer a placeholder shell. It renders:

- runtime status for the backend, adapter, and provider,
- dwarf list loading, empty, degraded, success, and error states,
- selected dwarf snapshot loading, empty, success, error, and degraded states for stale or invalid selection and snapshot identity mismatch,
- chat loading, empty, success, and error states.

Relevant error states surface `X-Correlation-ID` when the backend provides one.

## Dashboard

Local telemetry inspection is optional. Use the standalone Aspire Dashboard if
you want to view traces and metrics while developing:

```powershell
npx -y @microsoft/aspire-cli dashboard run --allow-anonymous
```

Then open:

```text
http://localhost:18888
```

The app should still start and stay usable when the dashboard is not running.

## What to verify in the dashboard

After calling `/api/health`, verify:

- a trace exists for the HTTP request,
- structured logs include `correlationId`,
- service name appears as `FortressSouls.Api` or equivalent,
- no secrets appear in logs,
- no full prompts or model responses appear in logs.

After calling the dwarf endpoints, verify spans:

```text
fortresssouls.dwarves.list
fortresssouls.dwarves.snapshot
```

After a chat turn, verify spans:

```text
fortresssouls.chat.turn
fortresssouls.prompt.assemble
fortresssouls.llm.chat
```

## Troubleshooting

### Fresh workspace frontend commands fail

Restore packages once before the first `dev`, `format`, `test`, or `check`
command:

```powershell
cd .\src\frontend
npm install
```

### Dashboard is empty

Check:

```powershell
echo $env:OTEL_EXPORTER_OTLP_ENDPOINT
echo $env:OTEL_EXPORTER_OTLP_PROTOCOL
```

Expected:

```text
http://localhost:4317
grpc
```

Also check that the API was started with the canonical launch profile or after
these environment variables were set.

### Backend fails when dashboard is not running

This is a bug.

Telemetry export must be optional in local dev. The app must run in fake mode
without the dashboard.

### UI shows a degraded or error state

Keep the stable user-facing error category and the displayed
`X-Correlation-ID`, then inspect safe logs or traces by that correlation ID.
Do not paste secrets, prompt text, model responses, raw DFHack output, or
private filesystem paths into reports.

### Logs contain prompt text or model response content

This is a bug unless the developer explicitly used the development-only
prompt-preview endpoint.

Default telemetry must not include full prompts or full model responses.

### Logs contain API key or Authorization header

Stop immediately. Treat this as a safety defect.

## Later optional path: Aspire AppHost

B-006 uses repository scripts and does not add AppHost or ServiceDefaults. A
later accepted decision may add:

```text
src/backend/FortressSouls.AppHost
src/backend/FortressSouls.ServiceDefaults
```

This is acceptable only if the script-based workflow proves insufficient.

It must not:

- introduce microservices,
- require cloud resources,
- make the dashboard mandatory,
- hide the basic `dotnet run` path.
