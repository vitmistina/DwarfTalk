namespace FortressSouls.Application;

using FortressSouls.Prompting;

public sealed record ChatSessionOptions
{
    public int MaxSessions { get; init; } = 128;

    public int MaxHistoryMessages { get; init; } = 24;

    public int MaxPlayerMessageCharacters { get; init; } = 1_200;

    public int MaxAssistantMessageCharacters { get; init; } = 1_200;

    public PromptAssemblyOptions PromptAssembly { get; init; } = PromptAssemblyOptions.Default;

    public static ChatSessionOptions Default { get; } = new();
}

public sealed record ChatSessionCreateResult(
    string SessionId,
    string DwarfId);

public sealed record ChatTurnDiagnostics(
    string Provider,
    string Model,
    int DurationMs,
    string PromptId);

public sealed record ChatSendMessageResult(
    string SessionId,
    string DwarfId,
    string AssistantMessage,
    ChatTurnDiagnostics Diagnostics,
    IReadOnlyList<AgentToolReceipt> ToolReceipts);

public sealed record ChatPromptPreviewResult(
    string SessionId,
    string DwarfId,
    string PromptText);

public sealed class ChatValidationException(string errorCode, string message)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}

public sealed class ChatSessionNotFoundException(string sessionId)
    : Exception($"Chat session '{sessionId}' was not found.")
{
    public string SessionId { get; } = sessionId;
}

public sealed class ChatTurnInProgressException(string sessionId)
    : Exception($"Chat session '{sessionId}' already has an active turn.")
{
    public string SessionId { get; } = sessionId;
}

public enum ChatRole
{
    Player,
    Assistant
}

public sealed record ChatHistoryMessage(ChatRole Role, string Text);
