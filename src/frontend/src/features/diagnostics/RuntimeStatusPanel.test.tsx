import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type {
  ProviderStatusResponse,
  AdapterStatusResponse,
} from "../../api/status";
import { RuntimeStatusPanel } from "./RuntimeStatusPanel";

function expectValueForLabel(label: string, value: string) {
  const term = screen.getByText(label);
  const description = term.nextElementSibling;

  expect(description).not.toBeNull();
  expect(description).toHaveTextContent(value);
}

async function expectStatusChip(label: string) {
  expect(
    await screen.findByText(label, { selector: ".status-chip" }),
  ).toBeInTheDocument();
}

function makeProvider(
  overrides?: Partial<ProviderStatusResponse>,
): ProviderStatusResponse {
  return {
    providerType: "Fake",
    model: "fake-dwarf",
    isConfigured: true,
    isReady: true,
    lastOutcome: "not_started",
    lastErrorCategory: null,
    lastDurationMs: null,
    lastUpdatedAtUtc: null,
    ...overrides,
  };
}

function makeAdapter(
  overrides?: Partial<AdapterStatusResponse>,
): AdapterStatusResponse {
  return {
    adapterType: "Fake",
    isConfigured: true,
    isReady: true,
    lastOutcome: "not_started",
    lastErrorCategory: null,
    lastDurationMs: null,
    lastUpdatedAtUtc: null,
    ...overrides,
  };
}

describe("RuntimeStatusPanel", () => {
  it("shows loading while status is being fetched", () => {
    render(
      <RuntimeStatusPanel
        loadRuntimeStatus={() => new Promise<never>(() => undefined)}
      />,
    );

    expect(screen.getByText("Checking runtime status...")).toBeInTheDocument();
  });

  it("shows ready state when both provider and adapter are configured and ready", async () => {
    render(
      <RuntimeStatusPanel
        loadRuntimeStatus={async () => ({
          provider: makeProvider(),
          adapter: makeAdapter(),
        })}
      />,
    );

    await expectStatusChip("Ready");
    expect(screen.getAllByText("Fake")).toHaveLength(2);
    expect(screen.getByText("fake-dwarf")).toBeInTheDocument();
    expectValueForLabel("Provider readiness", "Ready");
    expectValueForLabel("Provider last outcome", "Not started");
    expectValueForLabel("Adapter readiness", "Ready");
    expectValueForLabel("Adapter last outcome", "Not started");
  });

  it("shows degraded state when provider has a last error", async () => {
    render(
      <RuntimeStatusPanel
        loadRuntimeStatus={async () => ({
          provider: makeProvider({
            isReady: true,
            lastOutcome: "error",
            lastErrorCategory: "invalid_response",
          }),
          adapter: makeAdapter(),
        })}
      />,
    );

    await expectStatusChip("Degraded");
    expectValueForLabel("Provider readiness", "Ready");
    expectValueForLabel("Provider last outcome", "Error");
    expectValueForLabel(
      "Provider error",
      "Provider returned an invalid response.",
    );
    expect(screen.queryByText("invalid_response")).not.toBeInTheDocument();
  });

  it("shows degraded state when a dependency is configured but not ready", async () => {
    render(
      <RuntimeStatusPanel
        loadRuntimeStatus={async () => ({
          provider: makeProvider({
            isConfigured: true,
            isReady: false,
            lastOutcome: "not_started",
          }),
          adapter: makeAdapter(),
        })}
      />,
    );

    await expectStatusChip("Degraded");
    expectValueForLabel("Provider readiness", "Not ready");
    expectValueForLabel("Provider last outcome", "Not started");
  });

  it("shows not-configured state when provider is not configured", async () => {
    render(
      <RuntimeStatusPanel
        loadRuntimeStatus={async () => ({
          provider: makeProvider({
            isConfigured: false,
            isReady: false,
            lastOutcome: "error",
            lastErrorCategory: "missing_api_key",
          }),
          adapter: makeAdapter(),
        })}
      />,
    );

    await expectStatusChip("Not configured");
    expectValueForLabel("Provider readiness", "Not configured");
    expectValueForLabel("Provider last outcome", "Error");
    expectValueForLabel("Provider error", "Provider credentials are missing.");
    expect(screen.queryByText("missing_api_key")).not.toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: /setup guidance/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/set the provider type to fake for local recovery/i),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        /choose openaicompatible and configure a model plus credentials for real-provider mode/i,
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /setup guide/i }),
    ).not.toBeInTheDocument();
  });

  it("shows not-configured state when adapter is not configured", async () => {
    render(
      <RuntimeStatusPanel
        loadRuntimeStatus={async () => ({
          provider: makeProvider(),
          adapter: makeAdapter({
            isConfigured: false,
            isReady: false,
            lastOutcome: "disabled",
            lastErrorCategory: "adapter_disabled",
          }),
        })}
      />,
    );

    await expectStatusChip("Not configured");
    expectValueForLabel("Adapter readiness", "Not configured");
    expectValueForLabel("Adapter last outcome", "Disabled");
    expectValueForLabel("Adapter error", "Adapter is disabled.");
    expect(screen.queryByText("adapter_disabled")).not.toBeInTheDocument();
    expect(
      screen.getByRole("heading", { name: /setup guidance/i }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        /choose the adapter type explicitly: fake, jsonfile, or dfhackprocess/i,
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        /jsonfile mode needs matching single-dwarf list and snapshot files for the same dwarf/i,
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: /setup guide/i }),
    ).not.toBeInTheDocument();
  });

  it("shows adapter cancellation text for the runtime-status cancellation category", async () => {
    render(
      <RuntimeStatusPanel
        loadRuntimeStatus={async () => ({
          provider: makeProvider(),
          adapter: makeAdapter({
            isReady: true,
            lastOutcome: "error",
            lastErrorCategory: "request_cancelled",
          }),
        })}
      />,
    );

    await expectStatusChip("Degraded");
    expectValueForLabel("Adapter readiness", "Ready");
    expectValueForLabel("Adapter last outcome", "Error");
    expectValueForLabel("Adapter error", "Adapter request was cancelled.");
    expect(screen.queryByText("request_cancelled")).not.toBeInTheDocument();
  });

  it("shows degraded state when adapter has a last error", async () => {
    render(
      <RuntimeStatusPanel
        loadRuntimeStatus={async () => ({
          provider: makeProvider(),
          adapter: makeAdapter({
            isReady: true,
            lastOutcome: "error",
            lastErrorCategory: "dfhack_error",
          }),
        })}
      />,
    );

    await expectStatusChip("Degraded");
    expectValueForLabel("Adapter readiness", "Ready");
    expectValueForLabel("Adapter last outcome", "Error");
    expectValueForLabel("Adapter error", "DFHack returned an error.");
    expect(screen.queryByText("dfhack_error")).not.toBeInTheDocument();
  });

  it("shows error state and allows retry when status fetch fails", async () => {
    const loadRuntimeStatus = vi
      .fn()
      .mockRejectedValueOnce(new Error("network failure"))
      .mockResolvedValue({
        provider: makeProvider(),
        adapter: makeAdapter(),
      });

    render(<RuntimeStatusPanel loadRuntimeStatus={loadRuntimeStatus} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Runtime status is unavailable right now.",
    );

    const retryButton = screen.getByRole("button", { name: /retry/i });
    expect(retryButton).toBeInTheDocument();

    fireEvent.click(retryButton);

    await expectStatusChip("Ready");
    expect(loadRuntimeStatus).toHaveBeenCalledTimes(2);
  });

  it("retry button can receive focus in the error state", async () => {
    const loadRuntimeStatus = vi
      .fn()
      .mockRejectedValueOnce(new Error("network failure"))
      .mockResolvedValue({
        provider: makeProvider(),
        adapter: makeAdapter(),
      });

    render(<RuntimeStatusPanel loadRuntimeStatus={loadRuntimeStatus} />);

    await screen.findByRole("alert");

    const retryButton = screen.getByRole("button", { name: /retry/i });
    retryButton.focus();
    expect(retryButton).toHaveFocus();
  });
});
