namespace FortressSouls.Application;

using System.Text.Json;
using FortressSouls.Domain;

public sealed record AgentSessionContext(
    string SessionId,
    DwarfId DwarfId,
    DwarfSnapshot Snapshot,
    IReadOnlyList<ChatHistoryMessage> Conversation)
{
    public AgentTurnState TurnState { get; init; } = new();

    public AgentSessionContext Validate()
    {
        if (string.IsNullOrWhiteSpace(SessionId)
            || Snapshot is null
            || Conversation is null
            || TurnState is null
            || Snapshot.RequestedDwarfId != DwarfId
            || Snapshot.Identity.Id != DwarfId
            || Conversation.Any(message => message is null || message.Text is null || !Enum.IsDefined(message.Role)))
        {
            throw InvalidRequest();
        }

        return this;
    }

    private static AgentTurnException InvalidRequest() =>
        new(AgentTurnErrorCode.InvalidRequest, "The agent turn request is invalid.");
}

public sealed class AgentTurnState
{
    private readonly HashSet<string> _inspectableDwarfIds = new(StringComparer.Ordinal);

    public void BeginTurn()
    {
        _inspectableDwarfIds.Clear();
        IsActive = true;
    }

    public void EndTurn()
    {
        _inspectableDwarfIds.Clear();
        IsActive = false;
    }

    public bool IsActive { get; private set; }

    public void SetInspectableDwarves(IEnumerable<DwarfId> dwarfIds)
    {
        ArgumentNullException.ThrowIfNull(dwarfIds);

        if (!IsActive)
        {
            throw InvalidRequest();
        }

        _inspectableDwarfIds.Clear();
        foreach (var dwarfId in dwarfIds)
        {
            _inspectableDwarfIds.Add(dwarfId.ToString());
        }
    }

    public bool IsInspectable(DwarfId dwarfId) =>
        IsActive && _inspectableDwarfIds.Contains(dwarfId.ToString());

    private static AgentTurnException InvalidRequest() =>
        new(AgentTurnErrorCode.InvalidRequest, "The agent turn request is invalid.");
}

public sealed record AgentTurnRequest(
    AgentSessionContext Session,
    string UserMessage,
    AgentExecutionPolicy ExecutionPolicy,
    string? InitialPromptText = null)
{
    public IReadOnlyList<string>? EnabledToolNames { get; init; }

    public AgentTurnRequest Validate()
    {
        _ = Session?.Validate() ?? throw InvalidRequest();
        _ = ExecutionPolicy?.Validate() ?? throw InvalidRequest();

        if (UserMessage is null
            || (InitialPromptText is not null && string.IsNullOrWhiteSpace(InitialPromptText))
            || (EnabledToolNames is not null && EnabledToolNames.Any(name => string.IsNullOrWhiteSpace(name))))
        {
            throw InvalidRequest();
        }

        return this;
    }

    private static AgentTurnException InvalidRequest() =>
        new(AgentTurnErrorCode.InvalidRequest, "The agent turn request is invalid.");
}

public sealed record AgentExecutionPolicy(
    int MaximumRounds,
    int MaximumToolCalls,
    int MaximumToolResultBytes,
    int MaximumTotalToolResultBytes,
    TimeSpan TurnTimeout,
    TimeSpan ToolTimeout)
{
    public AgentExecutionPolicy Validate()
    {
        if (MaximumRounds <= 0
            || MaximumToolCalls <= 0
            || MaximumToolResultBytes <= 0
            || MaximumTotalToolResultBytes <= 0
            || MaximumTotalToolResultBytes < MaximumToolResultBytes
            || TurnTimeout <= TimeSpan.Zero
            || ToolTimeout <= TimeSpan.Zero)
        {
            throw new AgentTurnException(AgentTurnErrorCode.InvalidRequest, "The agent turn request is invalid.");
        }

        return this;
    }
}

public static class AgentToolOutcomes
{
    public const string Success = "success";
    public const string Unavailable = "unavailable";
    public const string InvalidArguments = "invalid_arguments";
    public const string NotFound = "not_found";
    public const string TimedOut = "timed_out";
    public const string InvalidData = "invalid_data";
    public const string ResultTooLarge = "result_too_large";
    public const string BudgetExhausted = "budget_exhausted";
}

public sealed record AgentToolReceipt(
    string Tool,
    string Outcome);

public sealed record AgentTurnResult(
    string AssistantMessage,
    string ProviderType,
    string Model,
    IReadOnlyList<AgentToolReceipt> ToolReceipts);

public interface IDwarfAgent
{
    Task<AgentTurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken);
}

public sealed record AgentToolDefinition(
    string Name,
    string Description);

public sealed record AgentToolInvocation(
    AgentToolDefinition Tool,
    AgentSessionContext Session,
    JsonElement Arguments);

public sealed record AgentToolResult(
    AgentToolDefinition Tool,
    JsonElement Content)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static AgentToolResult Create(AgentToolDefinition tool, object? content)
    {
        ArgumentNullException.ThrowIfNull(tool);

        return new AgentToolResult(
            tool,
            JsonSerializer.SerializeToElement(content, content?.GetType() ?? typeof(object), SerializerOptions));
    }
}

public sealed class AgentToolRegistration
{
    private static readonly Action<AgentToolInvocation> NoOpValidateInvocation = _ => { };

    private readonly Action<AgentToolInvocation> _validateInvocation;
    private readonly Func<AgentToolInvocation, CancellationToken, Task<AgentToolResult>> _executeAsync;

    public AgentToolRegistration(
        AgentToolDefinition definition,
        Func<AgentToolInvocation, CancellationToken, Task<AgentToolResult>> executeAsync,
        Action<AgentToolInvocation>? validateInvocation = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _validateInvocation = validateInvocation ?? NoOpValidateInvocation;
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));

        if (string.IsNullOrWhiteSpace(Definition.Name))
        {
            throw new ArgumentException("Agent tool names are required.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(Definition.Description))
        {
            throw new ArgumentException("Agent tool descriptions are required.", nameof(definition));
        }
    }

    public AgentToolDefinition Definition { get; }

    public void ValidateInvocation(AgentToolInvocation invocation) =>
        _validateInvocation(invocation);

    public Task<AgentToolResult> ExecuteAsync(AgentToolInvocation invocation, CancellationToken cancellationToken) =>
        _executeAsync(invocation, cancellationToken);
}

public interface IAgentToolRegistry
{
    IReadOnlyList<AgentToolDefinition> ListDefinitions();

    bool TryGetDefinition(string name, out AgentToolDefinition? definition);

    void ValidateInvocation(AgentToolInvocation invocation);

    Task<AgentToolResult> ExecuteAsync(AgentToolInvocation invocation, CancellationToken cancellationToken);
}

public sealed class ClosedAgentToolRegistry : IAgentToolRegistry
{
    private readonly IReadOnlyList<AgentToolDefinition> _orderedDefinitions;
    private readonly IReadOnlyDictionary<string, AgentToolRegistration> _toolsByName;

    public ClosedAgentToolRegistry(IEnumerable<AgentToolRegistration> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var orderedTools = tools
            .Select(tool => tool ?? throw new ArgumentException("Agent tools cannot contain null entries.", nameof(tools)))
            .OrderBy(tool => tool.Definition.Name, StringComparer.Ordinal)
            .ToArray();

        var duplicates = orderedTools
            .GroupBy(tool => tool.Definition.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicates is not null)
        {
            throw new ArgumentException($"Agent tool names must be unique ('{duplicates.Key}').", nameof(tools));
        }

        _orderedDefinitions = orderedTools.Select(tool => tool.Definition).ToArray();
        _toolsByName = orderedTools.ToDictionary(tool => tool.Definition.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<AgentToolDefinition> ListDefinitions() => _orderedDefinitions;

    public bool TryGetDefinition(string name, out AgentToolDefinition? definition)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            definition = null;
            return false;
        }

        if (_toolsByName.TryGetValue(name, out var tool))
        {
            definition = tool.Definition;
            return true;
        }

        definition = null;
        return false;
    }

    public Task<AgentToolResult> ExecuteAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
    {
        ValidateInvocation(invocation);

        return _toolsByName[invocation.Tool.Name].ExecuteAsync(invocation, cancellationToken);
    }

    public void ValidateInvocation(AgentToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(invocation.Tool);
        _ = invocation.Session?.Validate() ?? throw InvalidRequest();

        if (!_toolsByName.TryGetValue(invocation.Tool.Name, out var tool))
        {
            throw new AgentTurnException(AgentTurnErrorCode.InvalidData, "The requested agent tool is invalid.");
        }

        tool.ValidateInvocation(invocation);
    }

    private static AgentTurnException InvalidRequest() =>
        new(AgentTurnErrorCode.InvalidRequest, "The agent turn request is invalid.");
}

public enum AgentTurnErrorCode
{
    InvalidRequest,
    Unavailable,
    InvalidArguments,
    NotFound,
    TimedOut,
    InvalidData,
    ResultTooLarge,
    BudgetExhausted
}

public sealed class AgentTurnException : Exception
{
    public AgentTurnException(AgentTurnErrorCode errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public AgentTurnErrorCode ErrorCode { get; }
}