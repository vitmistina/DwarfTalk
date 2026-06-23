export interface CreateChatSessionResult {
  sessionId: string;
  dwarfId: string;
  correlationId?: string;
}

export interface SendChatMessageDiagnostics {
  provider: string;
  model: string;
  durationMs: number;
  promptId: string;
}

export interface ChatToolReceipt {
  tool: string;
  outcome: string;
}

export interface SendChatMessageResult {
  sessionId: string;
  dwarfId: string;
  assistantMessage: {
    role: "assistant";
    text: string;
  };
  diagnostics: SendChatMessageDiagnostics;
  toolReceipts: ChatToolReceipt[];
  correlationId?: string;
}

export interface ChatPromptPreviewResult {
  sessionId: string;
  dwarfId: string;
  promptText: string;
  correlationId?: string;
}

interface ApiErrorBody {
  errorCode?: string;
}

const correlationIdPattern = /^[A-Za-z0-9_.-]{1,64}$/;
export const chatMessageCharacterLimit = 1_200;

export class ChatApiError extends Error {
  readonly statusCode?: number;
  readonly errorCode?: string;
  readonly correlationId?: string;

  constructor(message: string, statusCode?: number, errorCode?: string, correlationId?: string) {
    super(message);
    this.name = "ChatApiError";
    this.statusCode = statusCode;
    this.errorCode = errorCode;
    this.correlationId = correlationId;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function requireStringField(record: Record<string, unknown>, fieldName: string): string {
  const value = record[fieldName];
  if (typeof value !== "string" || value.trim().length === 0) {
    throw new Error("Backend chat API returned an invalid response.");
  }
  return value;
}

function requireNumberField(record: Record<string, unknown>, fieldName: string): number {
  const value = record[fieldName];
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error("Backend chat API returned an invalid response.");
  }
  return value;
}

function sanitizeCorrelationId(value: string | null): string | undefined {
  if (value && correlationIdPattern.test(value)) {
    return value;
  }
  return undefined;
}

async function parseApiErrorBody(response: Response): Promise<ApiErrorBody | undefined> {
  const body = await response.json().catch(() => undefined);
  if (!isRecord(body)) {
    return undefined;
  }

  const errorCode = body.errorCode;
  if (typeof errorCode !== "string" || errorCode.trim().length === 0) {
    return undefined;
  }

  return { errorCode };
}

function mapChatErrorMessage(errorCode: string | undefined, statusCode: number): string {
  switch (errorCode) {
    case "invalid_dwarf_id":
    case "dwarf_not_found":
      return "The selected dwarf is stale or invalid. Select a dwarf again.";
    case "chat_session_not_found":
    case "invalid_session_id":
    case "chat_identity_mismatch":
      return "The chat session is no longer valid. Reset the chat session and try again.";
    case "chat_turn_in_progress":
      return "A chat turn is already in progress.";
    case "invalid_message":
      return "Enter a message before sending.";
    case "message_too_long":
      return "The message is too long. Shorten it and try again.";
    case "request_cancelled":
      return "The chat request timed out or was cancelled. Try again.";
    case "chat_provider_unavailable":
      return "The chat provider is unavailable right now.";
    case "chat_provider_invalid_response":
    case "chat_provider_invalid_configuration":
    case "chat_provider_error":
      return "The chat provider failed to process the request.";
    case "prompt_preview_unavailable":
      return "Prompt preview is unavailable until at least one successful response.";
    default:
      if (statusCode >= 500) {
        return "Chat is unavailable right now.";
      }
      return "Unable to complete the chat request right now.";
  }
}

function parseCreateSessionResponse(value: unknown): CreateChatSessionResult {
  if (!isRecord(value)) {
    throw new Error("Backend chat API returned an invalid response.");
  }

  return {
    sessionId: requireStringField(value, "sessionId"),
    dwarfId: requireStringField(value, "dwarfId"),
  };
}

function parseSendMessageResponse(value: unknown): SendChatMessageResult {
  if (!isRecord(value) || !isRecord(value.assistantMessage) || !isRecord(value.diagnostics) || !Array.isArray(value.toolReceipts)) {
    throw new Error("Backend chat API returned an invalid response.");
  }

  return {
    sessionId: requireStringField(value, "sessionId"),
    dwarfId: requireStringField(value, "dwarfId"),
    assistantMessage: {
      role: "assistant",
      text: requireStringField(value.assistantMessage, "text"),
    },
    diagnostics: {
      provider: requireStringField(value.diagnostics, "provider"),
      model: requireStringField(value.diagnostics, "model"),
      durationMs: requireNumberField(value.diagnostics, "durationMs"),
      promptId: requireStringField(value.diagnostics, "promptId"),
    },
    toolReceipts: value.toolReceipts.map((receipt) => parseToolReceipt(receipt)),
  };
}

function parseToolReceipt(value: unknown): ChatToolReceipt {
  if (!isRecord(value)) {
    throw new Error("Backend chat API returned an invalid response.");
  }

  return {
    tool: requireStringField(value, "tool"),
    outcome: requireStringField(value, "outcome"),
  };
}

function parsePromptPreviewResponse(value: unknown): ChatPromptPreviewResult {
  if (!isRecord(value)) {
    throw new Error("Backend chat API returned an invalid response.");
  }

  return {
    sessionId: requireStringField(value, "sessionId"),
    dwarfId: requireStringField(value, "dwarfId"),
    promptText: requireStringField(value, "promptText"),
  };
}

async function throwChatApiError(response: Response): Promise<never> {
  const correlationId = sanitizeCorrelationId(response.headers.get("X-Correlation-ID"));
  const apiError = await parseApiErrorBody(response);
  throw new ChatApiError(
    mapChatErrorMessage(apiError?.errorCode, response.status),
    response.status,
    apiError?.errorCode,
    correlationId,
  );
}

export async function createChatSession(dwarfId: string, signal?: AbortSignal): Promise<CreateChatSessionResult> {
  const response = await fetch("/api/chat/sessions", {
    method: "POST",
    signal,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ dwarfId }),
  });

  const correlationId = sanitizeCorrelationId(response.headers.get("X-Correlation-ID"));

  if (!response.ok) {
    await throwChatApiError(response);
  }

  const body = await response.json().catch(() => {
    throw new Error("Backend chat API returned an invalid response.");
  });

  return {
    ...parseCreateSessionResponse(body),
    correlationId,
  };
}

export async function sendChatMessage(
  sessionId: string,
  message: string,
  signal?: AbortSignal,
): Promise<SendChatMessageResult> {
  const response = await fetch(`/api/chat/sessions/${encodeURIComponent(sessionId)}/messages`, {
    method: "POST",
    signal,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ message }),
  });

  const correlationId = sanitizeCorrelationId(response.headers.get("X-Correlation-ID"));

  if (!response.ok) {
    await throwChatApiError(response);
  }

  const body = await response.json().catch(() => {
    throw new Error("Backend chat API returned an invalid response.");
  });

  return {
    ...parseSendMessageResponse(body),
    correlationId,
  };
}

export async function fetchChatPromptPreview(
  sessionId: string,
  signal?: AbortSignal,
): Promise<ChatPromptPreviewResult> {
  const response = await fetch(`/api/chat/sessions/${encodeURIComponent(sessionId)}/prompt-preview`, {
    method: "GET",
    signal,
    headers: {
      Accept: "application/json",
    },
  });

  const correlationId = sanitizeCorrelationId(response.headers.get("X-Correlation-ID"));

  if (!response.ok) {
    await throwChatApiError(response);
  }

  const body = await response.json().catch(() => {
    throw new Error("Backend chat API returned an invalid response.");
  });

  return {
    ...parsePromptPreviewResponse(body),
    correlationId,
  };
}
