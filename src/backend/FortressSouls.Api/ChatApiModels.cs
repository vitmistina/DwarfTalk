namespace FortressSouls.Api;

public sealed record CreateChatSessionRequest(string DwarfId);

public sealed record CreateChatSessionResponse(string SessionId, string DwarfId);

public sealed record SendChatMessageRequest(string Message);

public sealed record ChatAssistantMessageResponse(string Role, string Text);

public sealed record ChatDiagnosticsResponse(
    string Provider,
    string Model,
    int DurationMs,
    string PromptId);

public sealed record ChatToolReceiptResponse(string Tool, string Outcome);

public sealed record SendChatMessageResponse(
    string SessionId,
    string DwarfId,
    ChatAssistantMessageResponse AssistantMessage,
    ChatDiagnosticsResponse Diagnostics,
    IReadOnlyList<ChatToolReceiptResponse> ToolReceipts);

public sealed record PromptPreviewResponse(
    string SessionId,
    string DwarfId,
    string PromptText);

public sealed record ProviderStatusResponse(
    string ProviderType,
    string Model,
    bool IsConfigured,
    bool IsReady,
    string LastOutcome,
    string? LastErrorCategory,
    int? LastDurationMs,
    string? LastUpdatedAtUtc);
