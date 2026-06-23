namespace FortressSouls.Llm;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FortressSouls.Application;
using FortressSouls.Observability;
using Microsoft.Extensions.AI;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

public sealed class MicrosoftExtensionsAiDwarfAgent : IDwarfAgent
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly IChatClient _chatClient;
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly LlmProviderOptions _providerOptions;

    public MicrosoftExtensionsAiDwarfAgent(
        IChatClient chatClient,
        IAgentToolRegistry toolRegistry,
        LlmProviderOptions providerOptions)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _providerOptions = providerOptions?.Validate() ?? throw new ArgumentNullException(nameof(providerOptions));
    }

    public async Task<AgentTurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var validatedRequest = request.Validate();
        var session = validatedRequest.Session;
        var policy = validatedRequest.ExecutionPolicy?.Validate() ?? throw InvalidRequest();
        var normalizedMessage = NormalizeMessage(validatedRequest.UserMessage);
        var providerType = ResolveProviderType();
        var model = ResolveModel();
        var enabledToolDefinitions = ResolveEnabledToolDefinitions(validatedRequest.EnabledToolNames);
        var enabledToolsByName = enabledToolDefinitions.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        using var turnTimeoutSource = new CancellationTokenSource(policy.TurnTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, turnTimeoutSource.Token);

        using var activity = FortressSoulsTelemetry.ActivitySource.StartActivity(FortressSoulsTelemetry.AgentTurnActivityName, ActivityKind.Internal);
        activity?.SetTag(FortressSoulsTelemetry.ChatSessionIdTagName, session.SessionId);
        activity?.SetTag(FortressSoulsTelemetry.DwarfIdTagName, session.DwarfId.ToString());
        activity?.SetTag(FortressSoulsTelemetry.SnapshotSchemaVersionTagName, session.Snapshot.SchemaVersion);
        activity?.SetTag(FortressSoulsTelemetry.ProviderTypeTagName, providerType);
        activity?.SetTag(FortressSoulsTelemetry.LlmModelTagName, model);

        var messages = CreateInitialMessages(session, normalizedMessage, validatedRequest.InitialPromptText);

        var options = new ChatOptions
        {
            ModelId = model,
            Temperature = 0,
            MaxOutputTokens = 256,
            AllowMultipleToolCalls = false,
            Tools = enabledToolDefinitions
                .OrderBy(tool => tool.Name, StringComparer.Ordinal)
                .Select(CreateProviderTool)
                .ToArray()
        };

        if (!string.IsNullOrWhiteSpace(validatedRequest.InitialPromptText))
        {
            options.Instructions = validatedRequest.InitialPromptText;
        }

        var receipts = new List<AgentToolReceipt>();
        var totalOutputBytes = 0;
        var toolCalls = 0;
        session.TurnState.BeginTurn();

        try
        {
            for (var roundIndex = 1; roundIndex <= policy.MaximumRounds; roundIndex++)
            {
                var response = await _chatClient.GetResponseAsync(messages, options, linkedSource.Token);
                var responseMessages = response.Messages?.ToArray() ?? [];
                if (responseMessages.Length == 0)
                {
                    throw InvalidData();
                }

                foreach (var responseMessage in responseMessages)
                {
                    messages.Add(responseMessage);
                }

                var functionCalls = responseMessages
                    .SelectMany(message => message.Contents.OfType<FunctionCallContent>())
                    .ToArray();

                if (functionCalls.Length == 0)
                {
                    var assistantMessage = response.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(assistantMessage))
                    {
                        throw InvalidData();
                    }

                    activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.SuccessOutcome);
                    return new AgentTurnResult(assistantMessage, providerType, model, receipts);
                }

                var pendingToolCalls = functionCalls
                    .Select(functionCall => CreatePendingToolCall(functionCall, session, enabledToolsByName))
                    .ToArray();

                if (toolCalls + pendingToolCalls.Length > policy.MaximumToolCalls)
                {
                    AppendBudgetExhaustedReceipts(receipts, pendingToolCalls);
                    AppendBudgetExhaustedToolResults(messages, pendingToolCalls);

                    var finalResult = await TryCompleteAfterBudgetExhaustionAsync(messages, options, receipts, linkedSource.Token);
                    if (finalResult is not null)
                    {
                        activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.SuccessOutcome);
                        return finalResult;
                    }

                    throw BudgetExhausted();
                }

                for (var pendingToolCallIndex = 0; pendingToolCallIndex < pendingToolCalls.Length; pendingToolCallIndex++)
                {
                    var pendingToolCall = pendingToolCalls[pendingToolCallIndex];
                    var functionCall = pendingToolCall.FunctionCall;
                    var toolDefinition = pendingToolCall.Invocation.Tool;
                    toolCalls++;

                    using var toolActivity = FortressSoulsTelemetry.ActivitySource.StartActivity(FortressSoulsTelemetry.AgentToolCallActivityName, ActivityKind.Internal);
                    toolActivity?.SetTag(FortressSoulsTelemetry.ToolNameTagName, functionCall.Name);
                    toolActivity?.SetTag(FortressSoulsTelemetry.ToolCallIndexTagName, toolCalls);
                    toolActivity?.SetTag(FortressSoulsTelemetry.ToolRoundIndexTagName, roundIndex);

                    using var toolTimeoutSource = new CancellationTokenSource(policy.ToolTimeout);
                    using var toolLinkedSource = CancellationTokenSource.CreateLinkedTokenSource(linkedSource.Token, toolTimeoutSource.Token);

                    object? result;
                    try
                    {
                        var toolResult = await _toolRegistry.ExecuteAsync(
                            pendingToolCall.Invocation,
                            toolLinkedSource.Token);
                        result = toolResult.Content;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        toolActivity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.CancelledOutcome);
                        throw;
                    }
                    catch (OperationCanceledException) when ((toolTimeoutSource.IsCancellationRequested || turnTimeoutSource.IsCancellationRequested) && !cancellationToken.IsCancellationRequested)
                    {
                        var exception = TimedOut();
                        toolActivity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.ErrorOutcome);
                        toolActivity?.SetTag(FortressSoulsTelemetry.ErrorCategoryTagName, MapErrorCategory(exception.ErrorCode));
                        throw exception;
                    }
                    catch (AgentTurnException exception)
                    {
                        toolActivity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.ErrorOutcome);
                        toolActivity?.SetTag(FortressSoulsTelemetry.ErrorCategoryTagName, MapErrorCategory(exception.ErrorCode));
                        throw;
                    }
                    catch (Exception exception)
                    {
                        var mapped = Unavailable(exception);
                        toolActivity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.ErrorOutcome);
                        toolActivity?.SetTag(FortressSoulsTelemetry.ErrorCategoryTagName, MapErrorCategory(mapped.ErrorCode));
                        throw mapped;
                    }

                    var outputBytes = MeasureOutputBytes(result);
                    if (outputBytes > policy.MaximumToolResultBytes)
                    {
                        var exception = ResultTooLarge();
                        toolActivity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.ErrorOutcome);
                        toolActivity?.SetTag(FortressSoulsTelemetry.ErrorCategoryTagName, MapErrorCategory(exception.ErrorCode));
                        throw exception;
                    }

                    if (totalOutputBytes + outputBytes > policy.MaximumTotalToolResultBytes)
                    {
                        var exception = BudgetExhausted();
                        toolActivity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.ErrorOutcome);
                        toolActivity?.SetTag(FortressSoulsTelemetry.ErrorCategoryTagName, MapErrorCategory(exception.ErrorCode));
                        var unresolvedPendingToolCalls = pendingToolCalls[pendingToolCallIndex..];
                        AppendBudgetExhaustedReceipts(receipts, unresolvedPendingToolCalls);
                        AppendBudgetExhaustedToolResults(messages, unresolvedPendingToolCalls);

                        var finalResult = await TryCompleteAfterBudgetExhaustionAsync(messages, options, receipts, linkedSource.Token);
                        if (finalResult is not null)
                        {
                            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.SuccessOutcome);
                            return finalResult;
                        }

                        throw exception;
                    }

                    totalOutputBytes += outputBytes;
                    receipts.Add(new AgentToolReceipt(toolDefinition.Name, AgentToolOutcomes.Success));

                    toolActivity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.SuccessOutcome);
                    toolActivity?.SetTag(FortressSoulsTelemetry.ToolOutputBytesTagName, outputBytes);

                    messages.Add(CreateToolResultMessage(functionCall, result));
                }
            }

            var budgetExhaustedResult = await TryCompleteAfterBudgetExhaustionAsync(messages, options, receipts, linkedSource.Token);
            if (budgetExhaustedResult is not null)
            {
                activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.SuccessOutcome);
                return budgetExhaustedResult;
            }

            throw BudgetExhausted();
        }
        catch (OperationCanceledException) when (turnTimeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var exception = TimedOut();
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.ErrorOutcome);
            activity?.SetTag(FortressSoulsTelemetry.ErrorCategoryTagName, MapErrorCategory(exception.ErrorCode));
            throw exception;
        }
        catch (LlmProviderException exception)
        {
            var mapped = MapProviderException(exception);
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.ErrorOutcome);
            activity?.SetTag(FortressSoulsTelemetry.ErrorCategoryTagName, MapProviderErrorCategory(exception.ErrorCode));
            throw mapped;
        }
        catch (OperationCanceledException)
        {
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.CancelledOutcome);
            throw;
        }
        catch (AgentTurnException exception)
        {
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.ErrorOutcome);
            activity?.SetTag(FortressSoulsTelemetry.ErrorCategoryTagName, MapErrorCategory(exception.ErrorCode));
            throw;
        }
        finally
        {
            session.TurnState.EndTurn();
        }
    }

    private static AITool CreateProviderTool(AgentToolDefinition tool) =>
        tool.Name switch
        {
            FakePerceptionToolService.LookAroundToolName => AIFunctionFactory.Create(
                (int? radius, CancellationToken cancellationToken) => Task.FromResult<object?>(null),
                tool.Name,
                tool.Description,
                SerializerOptions),
            FakePerceptionToolService.InspectStocksToolName => AIFunctionFactory.Create(
                (string category, CancellationToken cancellationToken) => Task.FromResult<object?>(null),
                tool.Name,
                tool.Description,
                SerializerOptions),
            FakePerceptionToolService.ListDwarvesToolName => AIFunctionFactory.Create(
                (CancellationToken cancellationToken) => Task.FromResult<object?>(null),
                tool.Name,
                tool.Description,
                SerializerOptions),
            FakePerceptionToolService.InspectDwarfToolName => AIFunctionFactory.Create(
                (string dwarfId, CancellationToken cancellationToken) => Task.FromResult<object?>(null),
                tool.Name,
                tool.Description,
                SerializerOptions),
            ProbeObservationToolService.StableToolName => AIFunctionFactory.Create(
                (string subject, int repeatCount, bool emitLargePayload, int delayMs, CancellationToken cancellationToken) =>
                    Task.FromResult<object?>(null),
                tool.Name,
                tool.Description,
                SerializerOptions),
            _ => throw new ArgumentException($"The adapter does not support tool '{tool.Name}'.", nameof(tool))
        };

    private static JsonElement GetArguments(FunctionCallContent functionCall)
    {
        var arguments = functionCall.Arguments is null
            ? new Dictionary<string, object?>(StringComparer.Ordinal)
            : new Dictionary<string, object?>(functionCall.Arguments, StringComparer.Ordinal);

        return JsonSerializer.SerializeToElement(arguments, SerializerOptions);
    }

    private static int MeasureOutputBytes(object? result)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(result, SerializerOptions);
        return bytes.Length;
    }

    private IReadOnlyList<AgentToolDefinition> ResolveEnabledToolDefinitions(IReadOnlyList<string>? enabledToolNames)
    {
        if (enabledToolNames is null || enabledToolNames.Count == 0)
        {
            return _toolRegistry.ListDefinitions();
        }

        var resolved = new List<AgentToolDefinition>(enabledToolNames.Count);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var enabledToolName in enabledToolNames)
        {
            if (string.IsNullOrWhiteSpace(enabledToolName)
                || !seenNames.Add(enabledToolName)
                || !_toolRegistry.TryGetDefinition(enabledToolName, out var toolDefinition)
                || toolDefinition is null)
            {
                throw InvalidRequest();
            }

            resolved.Add(toolDefinition);
        }

        return resolved;
    }

    private PendingToolCall CreatePendingToolCall(
        FunctionCallContent functionCall,
        AgentSessionContext session,
        IReadOnlyDictionary<string, AgentToolDefinition> enabledToolsByName)
    {
        if (!enabledToolsByName.TryGetValue(functionCall.Name, out var toolDefinition) || toolDefinition is null)
        {
            throw InvalidData();
        }

        var invocation = new AgentToolInvocation(toolDefinition, session, GetArguments(functionCall));
        _toolRegistry.ValidateInvocation(invocation);

        return new PendingToolCall(functionCall, invocation);
    }

    private static void AppendBudgetExhaustedReceipts(List<AgentToolReceipt> receipts, IReadOnlyList<PendingToolCall> pendingToolCalls)
    {
        foreach (var pendingToolCall in pendingToolCalls)
        {
            receipts.Add(new AgentToolReceipt(pendingToolCall.Invocation.Tool.Name, AgentToolOutcomes.BudgetExhausted));
        }
    }

    private static void AppendBudgetExhaustedToolResults(List<ChatMessage> messages, IReadOnlyList<PendingToolCall> pendingToolCalls)
    {
        foreach (var pendingToolCall in pendingToolCalls)
        {
            messages.Add(CreateBudgetExhaustedToolResultMessage(pendingToolCall.FunctionCall));
        }
    }

    private static ChatMessage CreateBudgetExhaustedToolResultMessage(FunctionCallContent functionCall) =>
        CreateToolResultMessage(functionCall, JsonSerializer.SerializeToElement(new ToolFailureObservation(AgentToolOutcomes.BudgetExhausted), SerializerOptions));

    private static ChatMessage CreateToolResultMessage(FunctionCallContent functionCall, object? result)
    {
        if (string.IsNullOrWhiteSpace(functionCall.CallId))
        {
            throw InvalidData();
        }

        return new ChatMessage(
            AiChatRole.Tool,
            [new FunctionResultContent(functionCall.CallId, result)]);
    }

    private async Task<AgentTurnResult?> TryCompleteAfterBudgetExhaustionAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        IReadOnlyList<AgentToolReceipt> receipts,
        CancellationToken cancellationToken)
    {
        ChatResponse finalResponse;
        try
        {
            finalResponse = await _chatClient.GetResponseAsync(messages, CreateFinalResponseOptions(options), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LlmProviderException exception)
        {
            throw MapProviderException(exception);
        }
        catch
        {
            return null;
        }

        var responseMessages = finalResponse.Messages?.ToArray() ?? [];
        if (responseMessages.Length == 0)
        {
            return null;
        }

        if (responseMessages.SelectMany(message => message.Contents.OfType<FunctionCallContent>()).Any())
        {
            return null;
        }

        var assistantMessage = finalResponse.Text?.Trim();
        if (string.IsNullOrWhiteSpace(assistantMessage))
        {
            return null;
        }

        return new AgentTurnResult(
            assistantMessage,
            ResolveProviderType(),
            ResolveModel(),
            receipts);
    }

    private static ChatOptions CreateFinalResponseOptions(ChatOptions options) =>
        new()
        {
            ModelId = options.ModelId,
            Instructions = options.Instructions,
            Temperature = options.Temperature,
            MaxOutputTokens = options.MaxOutputTokens,
            AllowMultipleToolCalls = false,
            Tools = []
        };

    private static List<ChatMessage> CreateInitialMessages(AgentSessionContext session, string normalizedMessage, string? initialPromptText)
    {
        if (string.IsNullOrWhiteSpace(initialPromptText))
        {
            return
            [
                new ChatMessage(AiChatRole.System, "Use a registered tool only when current structured data would help. Reply briefly after tool use."),
                new ChatMessage(AiChatRole.User, normalizedMessage)
            ];
        }

        return
        [
            new ChatMessage(AiChatRole.User, normalizedMessage)
        ];
    }

    private static string NormalizeMessage(string message)
    {
        if (message is null)
        {
            throw InvalidRequest();
        }

        var normalized = message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw InvalidRequest();
        }

        return normalized;
    }

    private string ResolveProviderType() =>
        _providerOptions.ProviderType == LlmProviderType.Fake
            ? FakeToolLoopChatClient.ProviderTypeName
            : _providerOptions.ProviderType.ToString();

    private string ResolveModel() =>
        _providerOptions.ProviderType == LlmProviderType.Fake
            ? FakeToolLoopChatClient.ModelName
            : _providerOptions.Model;

    private static string MapErrorCategory(AgentTurnErrorCode errorCode) =>
        errorCode switch
        {
            AgentTurnErrorCode.InvalidRequest => "invalid_request",
            AgentTurnErrorCode.Unavailable => AgentToolOutcomes.Unavailable,
            AgentTurnErrorCode.InvalidArguments => AgentToolOutcomes.InvalidArguments,
            AgentTurnErrorCode.NotFound => AgentToolOutcomes.NotFound,
            AgentTurnErrorCode.TimedOut => AgentToolOutcomes.TimedOut,
            AgentTurnErrorCode.InvalidData => AgentToolOutcomes.InvalidData,
            AgentTurnErrorCode.ResultTooLarge => AgentToolOutcomes.ResultTooLarge,
            AgentTurnErrorCode.BudgetExhausted => AgentToolOutcomes.BudgetExhausted,
            _ => "agent_error"
        };

    private static string MapProviderErrorCategory(LlmProviderErrorCode errorCode) =>
        errorCode switch
        {
            LlmProviderErrorCode.InvalidConfiguration => AgentToolOutcomes.Unavailable,
            LlmProviderErrorCode.InvalidRequest => "invalid_request",
            LlmProviderErrorCode.Unavailable => AgentToolOutcomes.Unavailable,
            LlmProviderErrorCode.InvalidResponse => AgentToolOutcomes.InvalidData,
            LlmProviderErrorCode.Timeout => AgentToolOutcomes.TimedOut,
            LlmProviderErrorCode.ResponseTooLarge => AgentToolOutcomes.InvalidData,
            _ => "provider_error"
        };

    private static AgentTurnException MapProviderException(LlmProviderException exception) =>
        exception.ErrorCode switch
        {
            LlmProviderErrorCode.InvalidConfiguration => Unavailable(exception),
            LlmProviderErrorCode.Unavailable => Unavailable(exception),
            LlmProviderErrorCode.InvalidResponse => InvalidData(exception),
            LlmProviderErrorCode.ResponseTooLarge => InvalidData(exception),
            LlmProviderErrorCode.Timeout => TimedOut(exception),
            _ => InvalidRequest(exception)
        };

    private static AgentTurnException InvalidRequest(Exception? innerException = null) =>
        new(AgentTurnErrorCode.InvalidRequest, "The agent turn request is invalid.", innerException);

    private static AgentTurnException Unavailable(Exception? innerException = null) =>
        new(AgentTurnErrorCode.Unavailable, "The agent turn is unavailable.", innerException);

    private static AgentTurnException InvalidArguments(Exception? innerException = null) =>
        new(AgentTurnErrorCode.InvalidArguments, "The agent tool arguments are invalid.", innerException);

    private static AgentTurnException TimedOut(Exception? innerException = null) =>
        new(AgentTurnErrorCode.TimedOut, "The agent turn timed out.", innerException);

    private static AgentTurnException InvalidData(Exception? innerException = null) =>
        new(AgentTurnErrorCode.InvalidData, "The agent turn received invalid data.", innerException);

    private static AgentTurnException ResultTooLarge(Exception? innerException = null) =>
        new(AgentTurnErrorCode.ResultTooLarge, "The agent tool result is too large.", innerException);

    private static AgentTurnException BudgetExhausted(Exception? innerException = null) =>
        new(AgentTurnErrorCode.BudgetExhausted, "The agent turn exceeded its execution budget.", innerException);

    private sealed record PendingToolCall(
        FunctionCallContent FunctionCall,
        AgentToolInvocation Invocation);

    private sealed record ToolFailureObservation(string Outcome);
}
