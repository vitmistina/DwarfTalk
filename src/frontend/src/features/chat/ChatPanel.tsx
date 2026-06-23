import { useCallback, useEffect, useRef, useState } from "react";
import {
  ChatApiError,
  chatMessageCharacterLimit,
  createChatSession,
  fetchChatPromptPreview,
  sendChatMessage,
  type ChatToolReceipt,
  type ChatPromptPreviewResult,
  type CreateChatSessionResult,
  type SendChatMessageResult,
} from "../../api/chat";

interface ChatTurn {
  id: string;
  role: "player" | "assistant";
  text: string;
  diagnostics?: SendChatMessageResult["diagnostics"];
  toolReceipts?: SendChatMessageResult["toolReceipts"];
}

interface ChatSessionStateIdle {
  kind: "idle";
}

interface ChatSessionStateCreating {
  kind: "creating";
}

interface ChatSessionStateError {
  kind: "error";
  message: string;
  correlationId?: string;
}

interface ChatSessionStateReady {
  kind: "ready";
  session: CreateChatSessionResult;
}

type ChatSessionState = ChatSessionStateIdle | ChatSessionStateCreating | ChatSessionStateError | ChatSessionStateReady;

interface PromptPreviewStateIdle {
  kind: "idle";
}

interface PromptPreviewStateLoading {
  kind: "loading";
}

interface PromptPreviewStateError {
  kind: "error";
  message: string;
  correlationId?: string;
}

interface PromptPreviewStateReady {
  kind: "ready";
  result: ChatPromptPreviewResult;
}

type PromptPreviewState = PromptPreviewStateIdle | PromptPreviewStateLoading | PromptPreviewStateError | PromptPreviewStateReady;

export interface ChatPanelProps {
  selectedDwarfId: string | null;
  selectedDwarfName: string | null;
  showDevelopmentPreview?: boolean;
  isDevelopmentEnvironment?: boolean;
  createSession?: (dwarfId: string, signal?: AbortSignal) => Promise<CreateChatSessionResult>;
  sendMessage?: (sessionId: string, message: string, signal?: AbortSignal) => Promise<SendChatMessageResult>;
  loadPromptPreview?: (sessionId: string, signal?: AbortSignal) => Promise<ChatPromptPreviewResult>;
}

export function ChatPanel({
  selectedDwarfId,
  selectedDwarfName,
  showDevelopmentPreview = import.meta.env.DEV,
  isDevelopmentEnvironment = true,
  createSession: createSessionApi = createChatSession,
  sendMessage: sendMessageApi = sendChatMessage,
  loadPromptPreview: loadPromptPreviewApi = fetchChatPromptPreview,
}: ChatPanelProps) {
  const allowDevelopmentPreview = import.meta.env.DEV && showDevelopmentPreview && isDevelopmentEnvironment;
  const [sessionState, setSessionState] = useState<ChatSessionState>({ kind: "idle" });
  const [turns, setTurns] = useState<ChatTurn[]>([]);
  const [draft, setDraft] = useState("");
  const [inputError, setInputError] = useState<string | null>(null);
  const [sendError, setSendError] = useState<{ message: string; correlationId?: string } | null>(null);
  const [pendingMessage, setPendingMessage] = useState<string | null>(null);
  const [previewState, setPreviewState] = useState<PromptPreviewState>({ kind: "idle" });
  const createRequestRef = useRef(0);
  const sendRequestRef = useRef(0);
  const previewRequestRef = useRef(0);
  const previewControllerRef = useRef<AbortController | null>(null);

  const startSession = useCallback((dwarfId: string) => {
    const requestId = createRequestRef.current + 1;
    createRequestRef.current = requestId;
    sendRequestRef.current += 1;
    previewRequestRef.current += 1;
    previewControllerRef.current?.abort();
    previewControllerRef.current = null;
    setSessionState({ kind: "creating" });
    setTurns([]);
    setPendingMessage(null);
    setSendError(null);
    setInputError(null);
    setPreviewState({ kind: "idle" });

    createSessionApi(dwarfId)
      .then((result) => {
        if (requestId !== createRequestRef.current) {
          return;
        }

        if (result.dwarfId !== dwarfId) {
          setSessionState({
            kind: "error",
            message: "The chat session did not match the selected dwarf. Reset and try again.",
            correlationId: result.correlationId,
          });
          return;
        }

        setSessionState({ kind: "ready", session: result });
      })
      .catch((error: unknown) => {
        if (requestId !== createRequestRef.current) {
          return;
        }

        const apiError = error instanceof ChatApiError ? error : undefined;
        setSessionState({
          kind: "error",
          message: apiError?.message ?? "Unable to start chat right now.",
          correlationId: apiError?.correlationId,
        });
      });
  }, [createSessionApi]);

  useEffect(() => {
    if (!selectedDwarfId) {
      return;
    }

    const timeoutId = window.setTimeout(() => {
      startSession(selectedDwarfId);
    }, 0);

    return () => {
      window.clearTimeout(timeoutId);
      createRequestRef.current += 1;
      sendRequestRef.current += 1;
      previewRequestRef.current += 1;
      previewControllerRef.current?.abort();
      previewControllerRef.current = null;
    };
  }, [selectedDwarfId, startSession]);

  const handleResetSession = () => {
    if (!selectedDwarfId) {
      return;
    }

    setDraft("");
    startSession(selectedDwarfId);
  };

  const handleSendMessage = () => {
    if (pendingMessage !== null || sessionState.kind !== "ready") {
      return;
    }

    const normalizedDraft = draft.replace("\r\n", "\n").replaceAll("\r", "\n").trim();
    if (normalizedDraft.length === 0) {
      setInputError("Enter a message before sending.");
      return;
    }

    if (normalizedDraft.length > chatMessageCharacterLimit) {
      setInputError(`Message must be ${chatMessageCharacterLimit} characters or fewer.`);
      return;
    }

    setInputError(null);
    setSendError(null);
    setPendingMessage(normalizedDraft);
    const requestId = sendRequestRef.current + 1;
    sendRequestRef.current = requestId;
    const sessionId = sessionState.session.sessionId;

    sendMessageApi(sessionId, normalizedDraft)
      .then((result) => {
        if (requestId !== sendRequestRef.current || selectedDwarfId !== sessionState.session.dwarfId) {
          return;
        }

        if (result.sessionId !== sessionId || result.dwarfId !== sessionState.session.dwarfId) {
          setPendingMessage(null);
          setSessionState({
            kind: "error",
            message: "The chat session is no longer valid. Reset the chat session and try again.",
            correlationId: result.correlationId,
          });
          setSendError({
            message: "The chat response did not match the current session. Reset and try again.",
            correlationId: result.correlationId,
          });
          return;
        }

        setTurns((currentTurns) => [
          ...currentTurns,
          { id: `${result.sessionId}-${currentTurns.length + 1}-player`, role: "player", text: normalizedDraft },
          {
            id: `${result.sessionId}-${currentTurns.length + 2}-assistant`,
            role: "assistant",
            text: result.assistantMessage.text,
            diagnostics: result.diagnostics,
            toolReceipts: result.toolReceipts,
          },
        ]);
        setDraft("");
        setPendingMessage(null);
      })
      .catch((error: unknown) => {
        if (requestId !== sendRequestRef.current) {
          return;
        }

        const apiError = error instanceof ChatApiError ? error : undefined;
        setPendingMessage(null);
        setSendError({
          message: apiError?.message ?? "Unable to send the chat message right now.",
          correlationId: apiError?.correlationId,
        });

        if (
          apiError?.errorCode === "chat_session_not_found" ||
          apiError?.errorCode === "invalid_session_id" ||
          apiError?.errorCode === "chat_identity_mismatch"
        ) {
          setSessionState({
            kind: "error",
            message: "The chat session is no longer valid. Reset the chat session and try again.",
            correlationId: apiError.correlationId,
          });
        }
      });
  };

  const handlePromptPreview = () => {
    if (sessionState.kind !== "ready") {
      return;
    }

    previewControllerRef.current?.abort();
    const controller = new AbortController();
    previewControllerRef.current = controller;
    const requestId = previewRequestRef.current + 1;
    previewRequestRef.current = requestId;
    const activeSessionId = sessionState.session.sessionId;
    const activeDwarfId = sessionState.session.dwarfId;
    setPreviewState({ kind: "loading" });
    loadPromptPreviewApi(activeSessionId, controller.signal)
      .then((result) => {
        if (controller.signal.aborted || requestId !== previewRequestRef.current) {
          return;
        }

        if (result.sessionId !== activeSessionId || result.dwarfId !== activeDwarfId) {
          setPreviewState({
            kind: "error",
            message: "Prompt preview does not match the current session.",
            correlationId: result.correlationId,
          });
          return;
        }

        setPreviewState({ kind: "ready", result });
      })
      .catch((error: unknown) => {
        if (controller.signal.aborted || requestId !== previewRequestRef.current) {
          return;
        }

        const apiError = error instanceof ChatApiError ? error : undefined;
        setPreviewState({
          kind: "error",
          message: apiError?.message ?? "Unable to load prompt preview right now.",
          correlationId: apiError?.correlationId,
        });
      });
  };

  return (
    <article className="panel dwarf-panel" aria-labelledby="chat-heading" aria-busy={pendingMessage !== null}>
      <div className="panel__header">
        <span className={`status-chip ${statusClassName(sessionState, pendingMessage !== null)}`}>
          {statusLabel(sessionState, pendingMessage !== null)}
        </span>
        <h2 id="chat-heading">Chat</h2>
      </div>
      {selectedDwarfId ? (
        <p className="panel__copy">
          Chat target: <strong>{selectedDwarfName ?? selectedDwarfId}</strong>
        </p>
      ) : (
        <p className="panel__copy">Select a dwarf to start chat.</p>
      )}

      {sessionState.kind === "error" ? (
        <p className="panel__copy panel__copy--error" role="status">
          {sessionState.message}
          {sessionState.correlationId ? (
            <>
              {" "}
              Correlation ID: <code>{sessionState.correlationId}</code>
            </>
          ) : null}
        </p>
      ) : null}

      <ol className="chat-turn-list" aria-label="Conversation">
        {turns.map((turn) => (
          <li key={turn.id} className="chat-turn">
            <p className="chat-turn__role">{turn.role === "player" ? "You" : "Dwarf"}</p>
            <p className="chat-turn__text">{turn.text}</p>
            {turn.toolReceipts && turn.toolReceipts.length > 0 ? (
              <div className="chat-turn__receipts">
                <p className="chat-turn__receipt-label">Perception</p>
                <ul className="chat-turn__receipt-list" aria-label="Perception receipts">
                  {turn.toolReceipts.map((receipt, index) => (
                    <li key={`${turn.id}-receipt-${index}`}>
                      <span className={`receipt-chip ${receiptClassName(receipt.outcome)}`}>
                        <span className="receipt-chip__tool">{getReceiptToolLabel(receipt.tool)}</span>
                        <span className="receipt-chip__outcome">{getReceiptOutcomeLabel(receipt.outcome)}</span>
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
            {turn.diagnostics ? (
              <p className="chat-turn__meta">
                Provider: <code>{turn.diagnostics.provider}</code> · Model: <code>{turn.diagnostics.model}</code> ·
                {" Duration: "}
                {turn.diagnostics.durationMs}ms · Prompt: <code>{turn.diagnostics.promptId}</code>
              </p>
            ) : null}
          </li>
        ))}
        {pendingMessage ? (
          <li className="chat-turn chat-turn--pending" aria-live="polite">
            <p className="chat-turn__role">You</p>
            <p className="chat-turn__text">{pendingMessage}</p>
            <p className="chat-turn__meta">Sending…</p>
          </li>
        ) : null}
      </ol>

      {turns.length === 0 && pendingMessage === null ? (
        <p className="panel__copy">No conversation yet.</p>
      ) : null}

      {sendError ? (
        <p className="panel__copy panel__copy--error" role="alert">
          {sendError.message}
          {sendError.correlationId ? (
            <>
              {" "}
              Correlation ID: <code>{sendError.correlationId}</code>
            </>
          ) : null}
        </p>
      ) : null}

      <form
        className="chat-input-form"
        onSubmit={(event) => {
          event.preventDefault();
          handleSendMessage();
        }}
      >
        <label htmlFor="chat-message-input">Message</label>
        <textarea
          id="chat-message-input"
          rows={3}
          value={draft}
          onChange={(event) => {
            setDraft(event.target.value);
            setInputError(null);
          }}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey) {
              event.preventDefault();
              handleSendMessage();
            }
          }}
          maxLength={chatMessageCharacterLimit}
          disabled={sessionState.kind !== "ready" || pendingMessage !== null}
          aria-describedby="chat-input-help"
        />
        <p id="chat-input-help" className="panel__copy">
          Press Enter to send. Shift+Enter adds a new line. {draft.length}/{chatMessageCharacterLimit}
        </p>
        {inputError ? (
          <p className="panel__copy panel__copy--error" role="alert">
            {inputError}
          </p>
        ) : null}
        <div className="chat-input-actions">
          <button type="submit" disabled={sessionState.kind !== "ready" || pendingMessage !== null}>
            {pendingMessage !== null ? "Sending..." : "Send"}
          </button>
          <button
            type="button"
            onClick={handleResetSession}
            disabled={!selectedDwarfId || sessionState.kind === "creating" || pendingMessage !== null}
          >
            Reset chat session
          </button>
        </div>
      </form>

      {allowDevelopmentPreview && sessionState.kind === "ready" ? (
        <div className="chat-preview">
          <button type="button" onClick={handlePromptPreview} disabled={previewState.kind === "loading"}>
            {previewState.kind === "loading" ? "Loading prompt preview..." : "Show prompt preview"}
          </button>
          {previewState.kind === "error" ? (
            <p className="panel__copy panel__copy--error" role="alert">
              {previewState.message}
              {previewState.correlationId ? (
                <>
                  {" "}
                  Correlation ID: <code>{previewState.correlationId}</code>
                </>
              ) : null}
            </p>
          ) : null}
          {previewState.kind === "ready" ? (
            <pre className="chat-preview__text">{previewState.result.promptText}</pre>
          ) : null}
        </div>
      ) : null}
    </article>
  );
}

function statusClassName(sessionState: ChatSessionState, pending: boolean): string {
  if (!pending && sessionState.kind === "ready") {
    return "status-chip--ready";
  }
  if (sessionState.kind === "error") {
    return "status-chip--error";
  }
  if (sessionState.kind === "creating" || pending) {
    return "status-chip--loading";
  }
  return "status-chip--pending";
}

function statusLabel(sessionState: ChatSessionState, pending: boolean): string {
  if (pending) {
    return "Waiting";
  }
  switch (sessionState.kind) {
    case "ready":
      return "Ready";
    case "creating":
      return "Starting";
    case "error":
      return "Error";
    default:
      return "No session";
  }
}

function getReceiptToolLabel(tool: string): string {
  switch (tool) {
    case "look_around":
      return "look_around";
    case "inspect_stocks":
      return "inspect_stocks";
    case "list_dwarves":
      return "list_dwarves";
    case "inspect_dwarf":
      return "inspect_dwarf";
    default:
      return "tool";
  }
}

function getReceiptOutcomeLabel(outcome: string): string {
  switch (outcome) {
    case "success":
      return "Success";
    case "unavailable":
      return "Unavailable";
    case "invalid_arguments":
      return "Invalid arguments";
    case "not_found":
      return "Not found";
    case "timed_out":
      return "Timed out";
    case "invalid_data":
      return "Invalid data";
    case "result_too_large":
      return "Too large";
    case "budget_exhausted":
      return "Budget exhausted";
    default:
      return "Unknown outcome";
  }
}

function receiptClassName(outcome: ChatToolReceipt["outcome"]): string {
  switch (outcome) {
    case "success":
      return "receipt-chip--success";
    case "budget_exhausted":
      return "receipt-chip--warning";
    default:
      return "receipt-chip--error";
  }
}
