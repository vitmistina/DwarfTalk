import { useCallback, useEffect, useState } from "react";
import { fetchRuntimeStatus, type RuntimeStatusResult } from "../../api/status";

type RuntimeStatusLoader = (
  signal?: AbortSignal,
) => Promise<RuntimeStatusResult>;

export interface RuntimeStatusPanelProps {
  loadRuntimeStatus?: RuntimeStatusLoader;
}

type RuntimeStatusPanelState =
  | { kind: "loading" }
  | { kind: "ready"; result: RuntimeStatusResult }
  | { kind: "error" };

function formatReadiness(isConfigured: boolean, isReady: boolean): string {
  if (!isConfigured) {
    return "Not configured";
  }

  return isReady ? "Ready" : "Not ready";
}

function formatLastOutcome(outcome: string): string {
  switch (outcome) {
    case "not_started":
      return "Not started";
    case "success":
      return "Success";
    case "error":
      return "Error";
    case "timeout":
      return "Timed out";
    case "cancelled":
      return "Cancelled";
    case "disabled":
      return "Disabled";
    default:
      return "Unknown";
  }
}

const providerErrorMessages: Record<string, string> = {
  missing_api_key: "Provider credentials are missing.",
  invalid_configuration: "Provider configuration is invalid.",
  timeout: "Provider request timed out.",
  cancelled: "Provider request was cancelled.",
  transport_error: "Provider could not be reached.",
  non_success_status: "Provider returned an unexpected status.",
  invalid_response: "Provider returned an invalid response.",
  provider_error: "Provider failed to process the request.",
};

const adapterErrorMessages: Record<string, string> = {
  adapter_disabled: "Adapter is disabled.",
  invalid_configuration: "Adapter configuration is invalid.",
  unavailable: "Adapter is unavailable.",
  executable_unavailable: "DFHack executable is unavailable.",
  timeout: "Adapter request timed out.",
  cancelled: "Adapter request was cancelled.",
  request_cancelled: "Adapter request was cancelled.",
  crashed: "Adapter process exited unexpectedly.",
  output_too_large: "Adapter returned too much data.",
  invalid_schema: "Adapter returned data in an unsupported format.",
  mapping_failure: "Adapter data could not be processed.",
  invalid_json: "Adapter returned invalid data.",
  failed: "Adapter request failed.",
  dfhack_error: "DFHack returned an error.",
};

function formatProviderErrorCategory(errorCategory: string): string {
  return (
    providerErrorMessages[errorCategory] ??
    "Provider is reporting an unexpected error."
  );
}

function formatAdapterErrorCategory(errorCategory: string): string {
  return (
    adapterErrorMessages[errorCategory] ??
    "Adapter is reporting an unexpected error."
  );
}

function buildSetupGuidance(result: RuntimeStatusResult): string[] {
  const guidance: string[] = [];

  if (!result.provider.isConfigured) {
    guidance.push(
      "Provider: Set the provider type to Fake for local recovery.",
    );
    guidance.push(
      "Provider: Choose OpenAiCompatible and configure a model plus credentials for real-provider mode.",
    );
  }

  if (!result.adapter.isConfigured) {
    guidance.push(
      "Adapter: Choose the adapter type explicitly: Fake, JsonFile, or DfHackProcess.",
    );
    guidance.push(
      "Adapter: JsonFile mode needs matching single-dwarf list and snapshot files for the same dwarf.",
    );
  }

  return guidance;
}

function deriveChipVariant(
  result: RuntimeStatusResult,
): "ready" | "degraded" | "not-configured" {
  const { provider, adapter } = result;

  if (!provider.isConfigured || !adapter.isConfigured) {
    return "not-configured";
  }

  if (!provider.isReady || !adapter.isReady) {
    return "degraded";
  }

  const hasError =
    provider.lastOutcome === "error" ||
    provider.lastOutcome === "timeout" ||
    adapter.lastOutcome === "error" ||
    adapter.lastOutcome === "timeout";

  return hasError ? "degraded" : "ready";
}

export function RuntimeStatusPanel({
  loadRuntimeStatus = fetchRuntimeStatus,
}: RuntimeStatusPanelProps) {
  const [state, setState] = useState<RuntimeStatusPanelState>({
    kind: "loading",
  });
  const [retryKey, setRetryKey] = useState(0);

  const handleRetry = useCallback(() => {
    setState({ kind: "loading" });
    setRetryKey((k) => k + 1);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;

    loadRuntimeStatus(controller.signal)
      .then((result) => {
        if (active) {
          setState({ kind: "ready", result });
        }
      })
      .catch(() => {
        if (active) {
          setState({ kind: "error" });
        }
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, [loadRuntimeStatus, retryKey]);

  if (state.kind === "loading") {
    return (
      <section
        className="panel panel--runtime-status"
        aria-labelledby="runtime-status-heading"
        aria-busy="true"
      >
        <div className="panel__header">
          <span className="status-chip status-chip--loading">Loading</span>
          <h2 id="runtime-status-heading">Runtime status</h2>
        </div>
        <p className="panel__copy">Checking runtime status...</p>
      </section>
    );
  }

  if (state.kind === "error") {
    return (
      <section
        className="panel panel--runtime-status"
        aria-labelledby="runtime-status-heading"
      >
        <div className="panel__header">
          <span className="status-chip status-chip--error">Unavailable</span>
          <h2 id="runtime-status-heading">Runtime status</h2>
        </div>
        <p className="panel__copy panel__copy--error" role="alert">
          Runtime status is unavailable right now.
        </p>
        <button type="button" onClick={handleRetry}>
          Retry
        </button>
      </section>
    );
  }

  const { result } = state;
  const variant = deriveChipVariant(result);
  const { provider, adapter } = result;

  const chipClass =
    variant === "ready"
      ? "status-chip--ready"
      : variant === "degraded"
        ? "status-chip--error"
        : "status-chip--pending";

  const chipLabel =
    variant === "ready"
      ? "Ready"
      : variant === "degraded"
        ? "Degraded"
        : "Not configured";

  const isNotConfigured = variant === "not-configured";
  const setupGuidance = isNotConfigured ? buildSetupGuidance(result) : [];

  return (
    <section
      className="panel panel--runtime-status"
      aria-labelledby="runtime-status-heading"
    >
      <div className="panel__header">
        <span className={`status-chip ${chipClass}`}>{chipLabel}</span>
        <h2 id="runtime-status-heading">Runtime status</h2>
      </div>

      <dl className="runtime-status-grid">
        <div>
          <dt>Provider</dt>
          <dd>{provider.providerType}</dd>
        </div>
        <div>
          <dt>Model</dt>
          <dd>{provider.model}</dd>
        </div>
        <div>
          <dt>Provider readiness</dt>
          <dd>{formatReadiness(provider.isConfigured, provider.isReady)}</dd>
        </div>
        <div>
          <dt>Provider last outcome</dt>
          <dd>{formatLastOutcome(provider.lastOutcome)}</dd>
        </div>
        {provider.lastErrorCategory ? (
          <div>
            <dt>Provider error</dt>
            <dd>{formatProviderErrorCategory(provider.lastErrorCategory)}</dd>
          </div>
        ) : null}
        <div>
          <dt>Adapter</dt>
          <dd>{adapter.adapterType}</dd>
        </div>
        <div>
          <dt>Adapter readiness</dt>
          <dd>{formatReadiness(adapter.isConfigured, adapter.isReady)}</dd>
        </div>
        <div>
          <dt>Adapter last outcome</dt>
          <dd>{formatLastOutcome(adapter.lastOutcome)}</dd>
        </div>
        {adapter.lastErrorCategory ? (
          <div>
            <dt>Adapter error</dt>
            <dd>{formatAdapterErrorCategory(adapter.lastErrorCategory)}</dd>
          </div>
        ) : null}
      </dl>

      {setupGuidance.length > 0 ? (
        <section
          className="panel__copy"
          aria-labelledby="runtime-status-setup-guidance-heading"
        >
          <h3 id="runtime-status-setup-guidance-heading">Setup guidance</h3>
          <ul>
            {setupGuidance.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </section>
      ) : null}
    </section>
  );
}
