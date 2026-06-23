import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ChatApiError } from "../../api/chat";
import { ChatPanel } from "./ChatPanel";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });

  return { promise, resolve, reject };
}

describe("ChatPanel", () => {
  it("creates a session, sends a message, and renders ordered turns", async () => {
    const createSession = vi.fn(async () => ({ sessionId: "chat-00000001", dwarfId: "4101" }));
    const sendMessage = vi.fn(async () => ({
      sessionId: "chat-00000001",
      dwarfId: "4101",
      assistantMessage: {
        role: "assistant" as const,
        text: "Keep the pick sharp and the tunnel drier than my beard.",
      },
      diagnostics: {
        provider: "Fake",
        model: "fake-dwarf",
        durationMs: 25,
        promptId: "prompt-abc",
      },
      toolReceipts: [{ tool: "look_around", outcome: "success" }],
    }));

    render(
      <ChatPanel
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview={false}
        createSession={createSession}
        sendMessage={sendMessage}
      />,
    );

    const messageInput = await screen.findByLabelText("Message");
    fireEvent.change(messageInput, { target: { value: "How is the mine?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByText("How is the mine?")).toBeInTheDocument();
    expect(await screen.findByText(/Keep the pick sharp/i)).toBeInTheDocument();
    expect(screen.getByText("Perception")).toBeInTheDocument();
    expect(screen.getByText("look_around")).toBeInTheDocument();
    expect(screen.getByText("Success")).toBeInTheDocument();
    expect(screen.getByText(/Provider:/)).toBeInTheDocument();
    expect(messageInput).toHaveValue("");
    expect(createSession).toHaveBeenCalledWith("4101");
    expect(sendMessage).toHaveBeenCalledWith("chat-00000001", "How is the mine?");
  });

  it("blocks duplicate submit while pending", async () => {
    const turn = deferred<{
      sessionId: string;
      dwarfId: string;
      assistantMessage: { role: "assistant"; text: string };
      diagnostics: { provider: string; model: string; durationMs: number; promptId: string };
      toolReceipts: { tool: string; outcome: string }[];
    }>();
    const sendMessage = vi.fn(() => turn.promise);

    render(
      <ChatPanel
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview={false}
        createSession={async () => ({ sessionId: "chat-00000001", dwarfId: "4101" })}
        sendMessage={sendMessage}
      />,
    );

    const messageInput = await screen.findByLabelText("Message");
    fireEvent.change(messageInput, { target: { value: "Status?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));
    fireEvent.click(screen.getByRole("button", { name: "Sending..." }));

    expect(sendMessage).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("button", { name: "Sending..." })).toBeDisabled();

    turn.resolve({
      sessionId: "chat-00000001",
      dwarfId: "4101",
      assistantMessage: { role: "assistant", text: "Still digging." },
      diagnostics: {
        provider: "Fake",
        model: "fake-dwarf",
        durationMs: 10,
        promptId: "prompt-def",
      },
      toolReceipts: [],
    });

    expect(await screen.findByText("Still digging.")).toBeInTheDocument();
  });

  it("preserves draft text when send fails", async () => {
    render(
      <ChatPanel
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview={false}
        createSession={async () => ({ sessionId: "chat-00000001", dwarfId: "4101" })}
        sendMessage={async () => {
          throw new ChatApiError(
            "The chat provider is unavailable right now.",
            503,
            "chat_provider_unavailable",
            "trace-chat-error",
          );
        }}
      />,
    );

    const messageInput = await screen.findByLabelText("Message");
    fireEvent.change(messageInput, { target: { value: "Do you need help?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("The chat provider is unavailable right now.");
    expect(screen.getByText("trace-chat-error")).toBeInTheDocument();
    expect(messageInput).toHaveValue("Do you need help?");
    expect(screen.queryByText("Perception")).not.toBeInTheDocument();
    expect(screen.queryByText("Success")).not.toBeInTheDocument();
  });

  it("prevents mixed history when dwarf identity changes mid-turn", async () => {
    const turn = deferred<{
      sessionId: string;
      dwarfId: string;
      assistantMessage: { role: "assistant"; text: string };
      diagnostics: { provider: string; model: string; durationMs: number; promptId: string };
      toolReceipts: { tool: string; outcome: string }[];
    }>();
    const createSession = vi
      .fn()
      .mockResolvedValueOnce({ sessionId: "chat-00000001", dwarfId: "4101" })
      .mockResolvedValueOnce({ sessionId: "chat-00000002", dwarfId: "4102" });

    const { rerender } = render(
      <ChatPanel
        key="4101"
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview={false}
        createSession={createSession}
        sendMessage={() => turn.promise}
      />,
    );

    const messageInput = await screen.findByLabelText("Message");
    fireEvent.change(messageInput, { target: { value: "First dwarf question" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    rerender(
      <ChatPanel
        key="4102"
        selectedDwarfId="4102"
        selectedDwarfName="Domas Inkgranite"
        showDevelopmentPreview={false}
        createSession={createSession}
        sendMessage={() => turn.promise}
      />,
    );

    turn.resolve({
      sessionId: "chat-00000001",
      dwarfId: "4101",
      assistantMessage: { role: "assistant", text: "Old dwarf reply" },
      diagnostics: {
        provider: "Fake",
        model: "fake-dwarf",
        durationMs: 16,
        promptId: "prompt-old",
      },
      toolReceipts: [],
    });

    await waitFor(() => {
      expect(createSession).toHaveBeenNthCalledWith(1, "4101");
      expect(createSession).toHaveBeenNthCalledWith(2, "4102");
    });

    expect(screen.queryByText("First dwarf question")).not.toBeInTheDocument();
    expect(screen.queryByText("Old dwarf reply")).not.toBeInTheDocument();
    expect(screen.getByText("Chat target:")).toHaveTextContent("Domas Inkgranite");
    expect(screen.getByText("No conversation yet.")).toBeInTheDocument();
  });

  it("submits with keyboard Enter", async () => {
    const sendMessage = vi.fn(async () => ({
      sessionId: "chat-00000001",
      dwarfId: "4101",
      assistantMessage: { role: "assistant" as const, text: "Keyboard accepted." },
      diagnostics: { provider: "Fake", model: "fake-dwarf", durationMs: 9, promptId: "prompt-key" },
      toolReceipts: [],
    }));

    render(
      <ChatPanel
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview={false}
        createSession={async () => ({ sessionId: "chat-00000001", dwarfId: "4101" })}
        sendMessage={sendMessage}
      />,
    );

    const messageInput = await screen.findByLabelText("Message");
    fireEvent.change(messageInput, { target: { value: "Keyboard test" } });
    fireEvent.keyDown(messageInput, { key: "Enter", code: "Enter" });

    expect(await screen.findByText("Keyboard accepted.")).toBeInTheDocument();
    expect(sendMessage).toHaveBeenCalledWith("chat-00000001", "Keyboard test");
  });

  it("treats chat_identity_mismatch as an invalid session that must be reset", async () => {
    render(
      <ChatPanel
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview={false}
        createSession={async () => ({ sessionId: "chat-00000001", dwarfId: "4101" })}
        sendMessage={async () => {
          throw new ChatApiError(
            "The chat session is no longer valid. Reset the chat session and try again.",
            409,
            "chat_identity_mismatch",
            "trace-chat-identity",
          );
        }}
      />,
    );

    const messageInput = await screen.findByLabelText("Message");
    fireEvent.change(messageInput, { target: { value: "Mismatch test" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(
      await screen.findAllByText(/The chat session is no longer valid\. Reset the chat session and try again\./i),
    ).toHaveLength(2);
    expect(screen.getAllByText("trace-chat-identity")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Send" })).toBeDisabled();
  });

  it("ignores stale prompt preview responses after session reset", async () => {
    const stalePreview = deferred<{
      sessionId: string;
      dwarfId: string;
      promptText: string;
      correlationId?: string;
    }>();
    const createSession = vi
      .fn()
      .mockResolvedValueOnce({ sessionId: "chat-00000001", dwarfId: "4101" })
      .mockResolvedValueOnce({ sessionId: "chat-00000002", dwarfId: "4101" });
    const loadPromptPreview = vi.fn(() => stalePreview.promise);

    render(
      <ChatPanel
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview
        createSession={createSession}
        loadPromptPreview={loadPromptPreview}
      />,
    );

    fireEvent.click(await screen.findByRole("button", { name: "Show prompt preview" }));
    expect(loadPromptPreview).toHaveBeenCalledWith("chat-00000001", expect.any(AbortSignal));

    fireEvent.click(screen.getByRole("button", { name: "Reset chat session" }));
    await waitFor(() => {
      expect(createSession).toHaveBeenNthCalledWith(2, "4101");
    });

    stalePreview.resolve({
      sessionId: "chat-00000001",
      dwarfId: "4101",
      promptText: "stale prompt text",
    });

    await waitFor(() => {
      expect(screen.queryByText("stale prompt text")).not.toBeInTheDocument();
    });
    expect(screen.queryByText(/Prompt preview does not match the current session\./i)).not.toBeInTheDocument();
  });

  it("hides prompt preview in production even when prop is enabled", async () => {
    render(
      <ChatPanel
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview
        isDevelopmentEnvironment={false}
        createSession={async () => ({ sessionId: "chat-00000001", dwarfId: "4101" })}
      />,
    );

    await screen.findByLabelText("Message");
    expect(screen.queryByRole("button", { name: "Show prompt preview" })).not.toBeInTheDocument();
  });

  it("gates prompt preview and renders plain text only", async () => {
    const loadPromptPreview = vi.fn(async () => ({
      sessionId: "chat-00000001",
      dwarfId: "4101",
      promptText: "<b>unsafe</b>\nline two",
    }));

    const { container, rerender } = render(
      <ChatPanel
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview={false}
        createSession={async () => ({ sessionId: "chat-00000001", dwarfId: "4101" })}
        loadPromptPreview={loadPromptPreview}
      />,
    );

    await screen.findByLabelText("Message");
    expect(screen.queryByRole("button", { name: "Show prompt preview" })).not.toBeInTheDocument();

    rerender(
      <ChatPanel
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview
        createSession={async () => ({ sessionId: "chat-00000001", dwarfId: "4101" })}
        loadPromptPreview={loadPromptPreview}
      />,
    );

    fireEvent.click(await screen.findByRole("button", { name: "Show prompt preview" }));
    expect(
      await screen.findByText((content) => content.includes("<b>unsafe</b>") && content.includes("line two")),
    ).toBeInTheDocument();
    expect(container.querySelector("b")).toBeNull();
  });

  it("renders safe receipt outcomes without exposing raw arguments or results", async () => {
    render(
      <ChatPanel
        selectedDwarfId="4101"
        selectedDwarfName="Iden Torrentshade"
        showDevelopmentPreview={false}
        createSession={async () => ({ sessionId: "chat-00000001", dwarfId: "4101" })}
        sendMessage={async () => ({
          sessionId: "chat-00000001",
          dwarfId: "4101",
          assistantMessage: { role: "assistant" as const, text: "I could not inspect further." },
          diagnostics: { provider: "Fake", model: "fake-dwarf", durationMs: 14, promptId: "prompt-budget" },
          toolReceipts: [{ tool: "inspect_stocks", outcome: "budget_exhausted" }],
        })}
      />,
    );

    const messageInput = await screen.findByLabelText("Message");
    fireEvent.change(messageInput, { target: { value: "How much wood do we have in stock?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByText("inspect_stocks")).toBeInTheDocument();
    expect(screen.getByText("Budget exhausted")).toBeInTheDocument();
    expect(screen.queryByText("\"categories\"")).not.toBeInTheDocument();
    expect(screen.queryByText("radius")).not.toBeInTheDocument();
  });
});
