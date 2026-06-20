# ADR-0004: Observability and Local Developer Telemetry

Status: Accepted  
Date: 2026-06-19  
Spike: R-004 Local observability path

## Context

FortressSouls v0.1 needs observability from the first backend slice.

The app must make it easy to answer:

- Did the app connect to the dwarf data source?
- How long did dwarf listing take?
- How long did snapshot extraction take?
- What LLM provider and model were used?
- How long did the LLM request take?
- Did prompt assembly fail?
- Was a failure in DFHack, the backend, the provider, or the browser?

Logs alone are not enough. v0.1 needs structured logs, traces, and basic metrics.

The project is a modular monolith, not a distributed system. Local observability must not smuggle in unnecessary service decomposition.

## Options considered

### 1. Aspire AppHost and ServiceDefaults

Pros:

- Strong .NET local developer experience.
- Aspire Dashboard integration is first-class.
- ServiceDefaults can configure common OpenTelemetry instrumentation.
- AppHost can later orchestrate backend, frontend, dashboard, and small containers.

Cons:

- Adds Aspire-specific project structure early.
- May feel like distributed architecture cosplay if introduced before the app has enough moving parts.
- More scaffolding for Codex to maintain.

### 2. Standalone Aspire Dashboard with OTLP export

Pros:

- Works with any OpenTelemetry-enabled application.
- Does not require a full Aspire AppHost.
- Very good local UI for traces, structured logs, and metrics.
- Keeps the application instrumentation vendor-neutral.
- Simple enough for v0.1.

Cons:

- Standalone mode does not provide full Aspire resource orchestration.
- Requires a separate dashboard process or container.
- Telemetry is local and short-lived.

### 3. OpenTelemetry Collector through Docker Compose

Pros:

- Vendor-neutral collector path.
- Useful later for fan-out, filtering, batching, and routing.
- Closer to production-style telemetry pipelines.

Cons:

- Extra moving part for v0.1.
- Less immediately useful than a visual dashboard.
- More configuration to debug before the product loop exists.

### 4. Console exporters only

Pros:

- Minimal setup.
- Useful as fallback.
- No dashboard or container needed.

Cons:

- Poor local diagnostic experience.
- Harder to inspect traces and request waterfalls.
- Easy to slide back into “logs are observability”, which they are not.

## Decision

Use OpenTelemetry in the backend as the stable observability contract.

For local development, use:

1. **Standalone Aspire Dashboard** as the preferred local telemetry viewer.
2. **OTLP export** from the backend to the dashboard.
3. **Console exporter** as a fallback for environments where the dashboard is not running.
4. **No OpenTelemetry Collector in v0.1**, unless a later need appears.
5. **No production observability backend** in v0.1.

Do not require a full Aspire AppHost for B-005.

Allow B-006 to add Aspire AppHost or ServiceDefaults later if it simplifies one-command local startup. AppHost is orchestration convenience, not an architectural declaration that the system is microservices.

Explicitly reject a microservice split for observability in v0.1. Telemetry infrastructure may exist as a local developer aid, but Fortress Souls still ships as one backend application plus its local UI.

## Implementation direction for B-005

Create a dedicated observability module/project if the backend skeleton supports it:

```text
src/backend/FortressSouls.Observability
```

It should contain:

- shared `ActivitySource` definitions,
- shared `Meter` definitions,
- metric instruments,
- correlation ID middleware,
- redaction helpers,
- extension methods for service registration.

Minimum custom spans:

```text
fortresssouls.dwarves.list
fortresssouls.dwarves.snapshot
fortresssouls.prompt.assemble
fortresssouls.llm.chat
fortresssouls.chat.turn
```

Minimum metrics:

```text
fortresssouls.dwarves.list.duration
fortresssouls.dwarves.snapshot.duration
fortresssouls.prompt.tokens.estimated
fortresssouls.llm.request.duration
fortresssouls.llm.request.count
fortresssouls.llm.error.count
```

Minimum structured log fields:

```text
correlationId
sessionId
dwarfId
snapshotSchemaVersion
providerType
model
operation
durationMs
errorCode
```

## Recommended configuration

Use OTLP export when an endpoint is configured:

```text
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
OTEL_SERVICE_NAME=FortressSouls.Api
```

When OTLP is not configured or the dashboard is unavailable, the API should still run. Telemetry export must not become a product startup dependency.

## Local dashboard

Preferred local dashboard command:

```powershell
npx -y @microsoft/aspire-cli dashboard run --allow-anonymous
```

Alternative Docker command:

```powershell
docker run --rm -it `
  -p 18888:18888 `
  -p 4317:18889 `
  -p 4318:18890 `
  -e ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true `
  --name aspire-dashboard `
  mcr.microsoft.com/dotnet/aspire-dashboard:latest
```

Open:

```text
http://localhost:18888
```

## Security and redaction

Never log or tag:

- API keys,
- Authorization headers,
- full provider request headers,
- full prompts by default,
- full LLM responses by default,
- raw DFHack output by default,
- local filesystem paths containing private usernames unless explicitly in developer diagnostics.

Prompt preview may exist in development mode, but it must be explicit and must never include provider credentials.

## Consequences

Positive:

- Keeps observability vendor-neutral.
- Gives useful local traces, logs, and metrics early.
- Does not block v0.1 on a perfect orchestration story.
- Preserves room for Aspire AppHost later.
- Avoids an unnecessary OpenTelemetry Collector in the first slice.

Negative:

- Local dashboard is an extra process.
- Standalone dashboard resource views are limited compared with full Aspire AppHost.
- Some telemetry polish will come later as modules are implemented.
- Developers must understand the distinction between instrumentation and visualization.

## Non-decisions

This ADR does not decide:

- production observability backend,
- Application Insights,
- Grafana/Prometheus/Jaeger,
- browser telemetry,
- OpenTelemetry Collector deployment,
- long-term retention of telemetry,
- prompt/response capture strategy beyond v0.1 redaction rules.

## Unresolved questions

The following remain open and should be resolved only when later backlog items need them:

- whether B-006 should add Aspire AppHost and ServiceDefaults or keep standalone startup scripts,
- whether browser-side telemetry is useful in v0.1 beyond basic developer diagnostics,
- whether any additional bounded metrics are needed after the first end-to-end chat slice exists.
