namespace FortressSouls.Prompting;

using FortressSouls.Domain;

public enum PromptMessageRole
{
    Player,
    Assistant
}

public sealed record PromptConversationMessage(
    PromptMessageRole Role,
    string Text);

public sealed record PromptInputs(
    DwarfSnapshot Snapshot,
    IReadOnlyList<PromptConversationMessage> Conversation,
    string PlayerMessage,
    string? StaticInterpretationGuide = null);

public sealed record PromptToolDefinition(
    string Name,
    string ArgumentsSchemaVersion,
    string ResultSchemaVersion);

public sealed record AgentPromptInputs(
    DwarfSnapshot Snapshot,
    IReadOnlyList<PromptConversationMessage> Conversation,
    string PlayerMessage,
    IReadOnlyList<PromptToolDefinition> EnabledTools,
    string? StaticInterpretationGuide = null);

public sealed record PromptAssemblyOptions(
    int MaxPromptCharacters = 10_000,
    int MaxConversationMessages = 12,
    int MaxConversationMessageCharacters = 700,
    int MaxPlayerMessageCharacters = 1_200,
    int MaxStaticGuideCharacters = 2_000)
{
    public static PromptAssemblyOptions Default { get; } = new();
}

public enum PromptAssemblyFailureCategory
{
    None,
    ValidationError,
    PromptTooLarge
}

public sealed record PromptTruncationInfo(
    bool ConversationMessagesDroppedForCount,
    bool ConversationMessagesDroppedForBudget,
    bool ConversationMessageTextTruncated,
    bool PlayerMessageTruncated,
    bool StaticGuideTruncated)
{
    public bool Any =>
        ConversationMessagesDroppedForCount
        || ConversationMessagesDroppedForBudget
        || ConversationMessageTextTruncated
        || PlayerMessageTruncated
        || StaticGuideTruncated;
}

public sealed record PromptAssemblyDiagnostics(
    string TemplateVersion,
    int EstimatedCharacterCount,
    int EstimatedTokenCount,
    int ConversationMessagesIncluded,
    PromptTruncationInfo Truncation,
    PromptAssemblyFailureCategory FailureCategory);

public sealed record PromptAssemblyResult(
    string? PromptText,
    PromptAssemblyDiagnostics Diagnostics)
{
    public bool Succeeded => FailureCategory == PromptAssemblyFailureCategory.None;

    public PromptAssemblyFailureCategory FailureCategory => Diagnostics.FailureCategory;
}
