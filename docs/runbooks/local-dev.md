# Local Development Runbook

Status: Draft  
Applies to: FortressSouls v0.1  
Related ADR: `docs/decisions/adr-0004-observability.md`

## Purpose

Make local startup repeatable without turning the modular monolith into a petting zoo of tiny services.

v0.1 local development has three useful modes:

1. backend only,
2. backend plus frontend,
3. backend plus frontend plus local telemetry dashboard.

## Prerequisites

Required:

- .NET SDK matching the project target,
- Node.js for the frontend,
- PowerShell on Windows.

If local script execution is blocked, run the commands with
`powershell -ExecutionPolicy Bypass -File ...` for the current session.

Optional:

- Docker Desktop, if using the Aspire Dashboard container,
- Aspire CLI, if using `npx -y @microsoft/aspire-cli dashboard run`.

## Environment defaults

For local fake mode:

```powershell
$env:FortressSouls__DwarfFortress__AdapterType = "Fake"
$env:FortressSouls__Llm__ProviderType = "Fake"
```

The dwarf adapter is selected explicitly through
`FortressSouls__DwarfFortress__AdapterType`. When it is omitted, the app falls
back to the legacy `FortressSouls__DfHack__Enabled` switch for older local
configurations.

For local JSON-file mode with one fixed snapshot:

```powershell
$env:FortressSouls__DwarfFortress__AdapterType = "JsonFile"
$env:FortressSouls__DwarfFortress__JsonFile__DwarfListPath = "C:\path\to\matching-single-dwarf-list.json"
$env:FortressSouls__DwarfFortress__JsonFile__DwarfSnapshotPath = "C:\path\to\matching-dwarf-snapshot.json"
$env:FortressSouls__Llm__ProviderType = "Fake"
```

`DwarfSnapshotPath` points to one specific snapshot file. The adapter checks that
the browser-selected dwarf ID matches both `requestedUnitId` and `identity.id`
inside that file.

Do not pair a multi-dwarf list with one fixed snapshot file unless the list
contains only that same dwarf. For predictable browser behavior, either keep
the JSON-file list to the single dwarf represented by the snapshot file or use
`Fake`/`DfHackProcess` mode when you want to switch between multiple dwarves.

For local DFHack mode:

```powershell
$env:FortressSouls__DwarfFortress__AdapterType = "DfHackProcess"
$env:FortressSouls__DfHack__RunPath = "C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack\dfhack-run.exe"
$env:FortressSouls__DfHack__WorkingDirectory = "C:\Program Files (x86)\Steam\steamapps\common\DFHack\hack"
$env:FortressSouls__DfHack__Host = "127.0.0.1"
$env:FortressSouls__DfHack__Port = "5000"
$env:FortressSouls__Llm__ProviderType = "Fake"
```

For local telemetry to Aspire Dashboard when starting the API outside the
canonical launch profile:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
$env:OTEL_EXPORTER_OTLP_PROTOCOL = "grpc"
$env:OTEL_SERVICE_NAME = "FortressSouls.Api"
```

The canonical `http` and `https` launch profiles already set these values, so
`scripts/dev.*` and `dotnet run --launch-profile http` do not need manual OTEL
exports. Telemetry export must never be required for the app to start.

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

`dev` starts the current backend and frontend shells together. The frontend dev
server proxies `/api` to the backend on `http://127.0.0.1:5230`.

Focused fake-mode browser smoke test:

```powershell
cd .\src\frontend
npm run test:e2e:install
npm run test:e2e
```

The smoke harness starts backend/frontend in fake mode on controlled ports with readiness polling:

- backend `http://127.0.0.1:5230`
- frontend `http://127.0.0.1:5173`

## Start backend directly

From the repository root:

```powershell
dotnet run --launch-profile http --project .\src\backend\FortressSouls.Api\FortressSouls.Api.csproj
```

Expected health endpoint:

```text
GET http://localhost:5230/api/health
```

Expected response shape:

```json
{
  "status": "ok",
  "version": "0.1.0",
  "adapter": "Fake",
  "provider": "Fake",
  "observability": "OtlpConfigured"
}
```

If you start the API without the `http` or `https` launch profile and do not set
OTLP environment variables, `observability` falls back to `ConsoleFallback`.

## Start frontend directly

From the repository root:

```powershell
cd .\src\frontend
npm install
npm run dev -- --host 127.0.0.1 --strictPort
```

The frontend should show:

- backend health status,
- placeholder or real dwarf list depending on implementation phase,
- diagnostics panel.

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
- no full prompt appears in logs.

After dwarf endpoints exist, verify spans:

```text
fortresssouls.dwarves.list
fortresssouls.dwarves.snapshot
```

After chat exists, verify spans:

```text
fortresssouls.chat.turn
fortresssouls.prompt.assemble
fortresssouls.llm.chat
```

## Troubleshooting

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

Telemetry export must be optional in local dev. The app must run in fake mode without the dashboard.

### Logs contain prompt text

This is a bug unless the developer explicitly used a prompt-preview endpoint.

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
