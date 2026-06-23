namespace FortressSouls.Tests;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FortressSouls.Application;
using FortressSouls.Domain;
using FortressSouls.Llm;
using FortressSouls.Observability;
using Microsoft.Extensions.AI;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

public sealed class ToolLoopProbeTests
{
    [Fact]
    public async Task RunTurnAsync_ExecutesOneToolCall_AndReturnsFinalAssistantMessage()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    new Dictionary<string, object?>
                    {
                        ["subject"] = "ore bins",
                        ["repeatCount"] = 2,
                        ["emitLargePayload"] = false,
                        ["delayMs"] = 0
                    })])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "The ore bins are near the stockpile.")));

        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var result = await agent.RunTurnAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.Equal("The ore bins are near the stockpile.", result.AssistantMessage);
        var receipt = Assert.Single(result.ToolReceipts);
        Assert.Equal(ProbeObservationToolService.StableToolName, receipt.Tool);
        Assert.Equal(AgentToolOutcomes.Success, receipt.Outcome);
        Assert.Equal(1, toolService.InvocationCount);
        Assert.Equal(2, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_RejectsUnknownToolBeforeApplicationExecution()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-1", "probe_unknown", new Dictionary<string, object?>())])));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidData, exception.ErrorCode);
        Assert.Equal(0, toolService.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_RejectsMalformedToolArgumentsBeforeApplicationExecution()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    new Dictionary<string, object?>
                    {
                        ["subject"] = "ore bins",
                        ["repeatCount"] = "many",
                        ["emitLargePayload"] = false,
                        ["delayMs"] = 0
                    })])));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidArguments, exception.ErrorCode);
        Assert.Equal(0, toolService.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RunTurnAsync_RejectsMismatchedSelectedDwarfIdentityBeforeProviderInteraction(
        bool mismatchRequestedDwarfId,
        bool mismatchSnapshotIdentityId)
    {
        var fakeClient = new SequenceChatClient();
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);
        var mismatchedDwarfId = DwarfId.Parse("4102");

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(
                CreateRequest(
                    session: CreateSessionContext(
                        requestedDwarfId: mismatchRequestedDwarfId ? mismatchedDwarfId : null,
                        identityDwarfId: mismatchSnapshotIdentityId ? mismatchedDwarfId : null)),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidRequest, exception.ErrorCode);
        Assert.Equal(0, toolService.InvocationCount);
        Assert.Equal(0, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_RejectsOutOfRangeToolArgumentsBeforeApplicationExecution()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "ore bins", repeatCount: 0))])));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(maximumRounds: 2, maximumToolCalls: 2), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidArguments, exception.ErrorCode);
        Assert.Equal(0, toolService.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_MapsUnexpectedToolFailureToStableUnavailableWithoutRetry()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "ore bins"))])));
        var tool = new ThrowingTool();
        var agent = CreateAgent(fakeClient, tool);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(maximumRounds: 2, maximumToolCalls: 2), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.Unavailable, exception.ErrorCode);
        Assert.Equal(1, tool.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_AllowsOneFinalAssistantResponseAttemptAfterRoundBudgetExhaustion()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "ore bins"))])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I cannot inspect further, but the ore bins are near the stockpile.")));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var result = await agent.RunTurnAsync(
            CreateRequest(maximumRounds: 1),
            CancellationToken.None);

        Assert.Equal("I cannot inspect further, but the ore bins are near the stockpile.", result.AssistantMessage);
        Assert.Equal(1, toolService.InvocationCount);
        Assert.Equal(2, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_DisablesToolsOnFinalAssistantResponseAttemptAfterBudgetExhaustion()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "ore bins"))])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I have nothing further to inspect.")));
        var agent = CreateAgent(fakeClient, new ProbeObservationToolService());

        var result = await agent.RunTurnAsync(
            CreateRequest(maximumRounds: 1),
            CancellationToken.None);

        Assert.Equal("I have nothing further to inspect.", result.AssistantMessage);
        Assert.Collection(
            fakeClient.RequestOptions,
            options => Assert.NotEmpty(options?.Tools ?? []),
            options => Assert.Empty(options?.Tools ?? []));
    }

    [Fact]
    public async Task RunTurnAsync_MapsRoundLimitToBudgetExhausted()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "ore bins"))])));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(maximumRounds: 1), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.BudgetExhausted, exception.ErrorCode);
        Assert.Equal(1, toolService.InvocationCount);
        Assert.Equal(2, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_MapsToolCallLimitToBudgetExhausted()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [
                    new FunctionCallContent("call-1", "probe_observe", ValidArguments(subject: "ore bins")),
                    new FunctionCallContent("call-2", "probe_observe", ValidArguments(subject: "ore bins"))
                ])));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(maximumToolCalls: 1), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.BudgetExhausted, exception.ErrorCode);
        Assert.Equal(0, toolService.InvocationCount);
        Assert.Equal(2, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_RejectsUnknownToolInOverLimitBatchBeforeBudgetRecovery()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [
                    new FunctionCallContent("call-1", "probe_observe", ValidArguments(subject: "ore bins")),
                    new FunctionCallContent("call-2", "probe_unknown", new Dictionary<string, object?>())
                ])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I cannot inspect further.")));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(maximumToolCalls: 1), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidData, exception.ErrorCode);
        Assert.Equal(0, toolService.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_RejectsInvalidArgumentsInOverLimitBatchBeforeBudgetRecovery()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [
                    new FunctionCallContent("call-1", "probe_observe", ValidArguments(subject: "ore bins")),
                    new FunctionCallContent(
                        "call-2",
                        "probe_observe",
                        new Dictionary<string, object?>
                        {
                            ["subject"] = "ore bins",
                            ["repeatCount"] = "many",
                            ["emitLargePayload"] = false,
                            ["delayMs"] = 0
                        })
                ])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I cannot inspect further.")));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(maximumToolCalls: 1), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidArguments, exception.ErrorCode);
        Assert.Equal(0, toolService.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_RejectsSemanticInvalidArgumentsInOverLimitBatchBeforeBudgetRecovery()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [
                    new FunctionCallContent("call-1", "probe_observe", ValidArguments(subject: "ore bins")),
                    new FunctionCallContent(
                        "call-2",
                        "probe_observe",
                        ValidArguments(subject: "   "))
                ])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I cannot inspect further.")));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(maximumToolCalls: 1), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidArguments, exception.ErrorCode);
        Assert.Equal(0, toolService.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_EnforcesPerResultBudget()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "ore bins", emitLargePayload: true))])));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(maximumToolResultBytes: 128, maximumTotalToolResultBytes: 512), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.ResultTooLarge, exception.ErrorCode);
        Assert.Equal(1, toolService.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_EnforcesTotalResultBudget()
    {
        var longSubject = new string('o', 50);
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-1", "probe_observe", ValidArguments(subject: longSubject))])),
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-2", "probe_observe", ValidArguments(subject: longSubject))])));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(maximumRounds: 2, maximumToolCalls: 2, maximumToolResultBytes: 256, maximumTotalToolResultBytes: 260), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.BudgetExhausted, exception.ErrorCode);
        Assert.Equal(2, toolService.InvocationCount);
        Assert.Equal(3, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_AppendsBudgetExhaustedToolResultAndReceiptWhenFinalResponseSucceedsAfterCumulativeBudgetOverflow()
    {
        var longSubject = new string('o', 50);
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-1", "probe_observe", ValidArguments(subject: longSubject))])),
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-2", "probe_observe", ValidArguments(subject: longSubject))])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I could not inspect further, but the bins are nearby.")));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var result = await agent.RunTurnAsync(
            CreateRequest(maximumRounds: 2, maximumToolCalls: 2, maximumToolResultBytes: 256, maximumTotalToolResultBytes: 260),
            CancellationToken.None);

        Assert.Equal("I could not inspect further, but the bins are nearby.", result.AssistantMessage);
        Assert.Collection(
            result.ToolReceipts,
            receipt =>
            {
                Assert.Equal(ProbeObservationToolService.StableToolName, receipt.Tool);
                Assert.Equal(AgentToolOutcomes.Success, receipt.Outcome);
            },
            receipt =>
            {
                Assert.Equal(ProbeObservationToolService.StableToolName, receipt.Tool);
                Assert.Equal(AgentToolOutcomes.BudgetExhausted, receipt.Outcome);
            });

        var retryMessages = fakeClient.RequestMessages[2];
        var retryToolMessage = Assert.Single(
            retryMessages,
            message => message.Role == AiChatRole.Tool
                && message.Contents.OfType<FunctionResultContent>().Any(content => string.Equals(content.CallId, "call-2", StringComparison.Ordinal)));
        var retryToolResult = Assert.Single(retryToolMessage.Contents.OfType<FunctionResultContent>());
        var retryObservation = Assert.IsType<JsonElement>(retryToolResult.Result);
        Assert.Equal(AgentToolOutcomes.BudgetExhausted, retryObservation.GetProperty("outcome").GetString());

        Assert.Equal(2, toolService.InvocationCount);
        Assert.Equal(3, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_AppendsBudgetExhaustedReceiptsAndToolResultsForCurrentAndRemainingCallsWhenCumulativeBudgetOverflows()
    {
        var longSubject = new string('o', 50);
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [
                    new FunctionCallContent("call-1", "probe_observe", ValidArguments(subject: longSubject)),
                    new FunctionCallContent("call-2", "probe_observe", ValidArguments(subject: longSubject)),
                    new FunctionCallContent("call-3", "probe_observe", ValidArguments(subject: longSubject))
                ])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I could not inspect further, but the bins are nearby.")));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var result = await agent.RunTurnAsync(
            CreateRequest(maximumRounds: 2, maximumToolCalls: 3, maximumToolResultBytes: 256, maximumTotalToolResultBytes: 260),
            CancellationToken.None);

        Assert.Equal("I could not inspect further, but the bins are nearby.", result.AssistantMessage);
        Assert.Collection(
            result.ToolReceipts,
            receipt =>
            {
                Assert.Equal(ProbeObservationToolService.StableToolName, receipt.Tool);
                Assert.Equal(AgentToolOutcomes.Success, receipt.Outcome);
            },
            receipt =>
            {
                Assert.Equal(ProbeObservationToolService.StableToolName, receipt.Tool);
                Assert.Equal(AgentToolOutcomes.BudgetExhausted, receipt.Outcome);
            },
            receipt =>
            {
                Assert.Equal(ProbeObservationToolService.StableToolName, receipt.Tool);
                Assert.Equal(AgentToolOutcomes.BudgetExhausted, receipt.Outcome);
            });

        var retryMessages = fakeClient.RequestMessages[1];
        Assert.Equal(3, retryMessages.Count(message => message.Role == AiChatRole.Tool));
        Assert.Contains(
            retryMessages,
            message => message.Role == AiChatRole.Tool
                && message.Contents.OfType<FunctionResultContent>().Any(content => string.Equals(content.CallId, "call-1", StringComparison.Ordinal)));

        AssertBudgetExhaustedToolMessage(retryMessages, "call-2");
        AssertBudgetExhaustedToolMessage(retryMessages, "call-3");

        Assert.Equal(2, toolService.InvocationCount);
        Assert.Equal(2, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_HonorsCallerCancellation()
    {
        var fakeClient = new SequenceChatClient();
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            agent.RunTurnAsync(CreateRequest(), cancellationTokenSource.Token));

        Assert.Equal(0, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_PropagatesCallerCancellationDuringToolExecution()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "ore bins", delayMs: 5_000))])));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);
        using var cancellationTokenSource = new CancellationTokenSource();

        var runTask = agent.RunTurnAsync(
            CreateRequest(
                turnTimeout: TimeSpan.FromSeconds(10),
                toolTimeout: TimeSpan.FromSeconds(10)),
            cancellationTokenSource.Token);

        Assert.True(SpinWait.SpinUntil(() => toolService.InvocationCount == 1, TimeSpan.FromSeconds(1)));
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);
        Assert.Equal(1, toolService.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_MapsToolTimeoutToTimedOut()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "ore bins", delayMs: 200))])));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(toolTimeout: TimeSpan.FromMilliseconds(50)), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.TimedOut, exception.ErrorCode);
        Assert.Equal(1, toolService.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_MapsWholeTurnTimeoutDuringToolExecutionToTimedOut()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "ore bins", delayMs: 200))])));
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(
                CreateRequest(
                    turnTimeout: TimeSpan.FromMilliseconds(50),
                    toolTimeout: TimeSpan.FromSeconds(5)),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.TimedOut, exception.ErrorCode);
        Assert.Equal(1, toolService.InvocationCount);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_EnforcesTurnTimeout()
    {
        var fakeClient = new CallbackChatClient(async cancellationToken =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            return new ChatResponse(new ChatMessage(AiChatRole.Assistant, "late"));
        });
        var toolService = new ProbeObservationToolService();
        var agent = CreateAgent(fakeClient, toolService);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(turnTimeout: TimeSpan.FromMilliseconds(50)), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.TimedOut, exception.ErrorCode);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_EmitsStableSnakeCaseTelemetryErrorCategoryForUnknownTool()
    {
        var observed = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FortressSoulsTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observed.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-1", "probe_unknown", new Dictionary<string, object?>())])));

        var agent = CreateAgent(fakeClient, new ProbeObservationToolService());

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidData, exception.ErrorCode);

        var turnActivity = Assert.Single(observed, activity => activity.DisplayName == FortressSoulsTelemetry.AgentTurnActivityName);
        Assert.Contains(turnActivity.Tags, tag =>
            tag.Key == FortressSoulsTelemetry.ErrorCategoryTagName
            && string.Equals(tag.Value, AgentToolOutcomes.InvalidData, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunTurnAsync_EmitsContentFreeTelemetry()
    {
        var observed = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FortressSoulsTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observed.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "SENTINEL-TOOL"))])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "The bins look steady.")));

        var agent = CreateAgent(fakeClient, new ProbeObservationToolService());
        var result = await agent.RunTurnAsync(CreateRequest(userMessage: "SENTINEL-USER"), CancellationToken.None);

        Assert.Equal("The bins look steady.", result.AssistantMessage);

        var snapshot = observed.ToArray();
        Assert.Contains(snapshot, activity => activity.DisplayName == FortressSoulsTelemetry.AgentTurnActivityName);
        Assert.Contains(snapshot, activity => activity.DisplayName == FortressSoulsTelemetry.AgentToolCallActivityName);
        Assert.DoesNotContain(snapshot.SelectMany(activity => activity.Tags), tag =>
            (tag.Value?.ToString() ?? string.Empty).Contains("SENTINEL-USER", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.SelectMany(activity => activity.Tags), tag =>
            (tag.Value?.ToString() ?? string.Empty).Contains("SENTINEL-TOOL", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunTurnAsync_EmitsBoundedAgentAndToolTelemetryTags()
    {
        var observed = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FortressSoulsTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observed.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    "probe_observe",
                    ValidArguments(subject: "ore bins"))])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "The ore bins are near the stockpile.")));

        var agent = CreateAgent(fakeClient, new ProbeObservationToolService());
        var result = await agent.RunTurnAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal("The ore bins are near the stockpile.", result.AssistantMessage);

        var snapshot = observed.ToArray();
        var turnActivity = Assert.Single(snapshot, activity => activity.DisplayName == FortressSoulsTelemetry.AgentTurnActivityName);
        var toolActivity = Assert.Single(snapshot, activity => activity.DisplayName == FortressSoulsTelemetry.AgentToolCallActivityName);

        Assert.Equal("session-4101", turnActivity.GetTagItem(FortressSoulsTelemetry.ChatSessionIdTagName));
        Assert.Equal("4101", turnActivity.GetTagItem(FortressSoulsTelemetry.DwarfIdTagName));
        Assert.Equal(DwarfSchemaVersions.Snapshot, turnActivity.GetTagItem(FortressSoulsTelemetry.SnapshotSchemaVersionTagName));
        Assert.Equal("OpenAiCompatible", turnActivity.GetTagItem(FortressSoulsTelemetry.ProviderTypeTagName));
        Assert.Equal("deepseek/deepseek-v3.2", turnActivity.GetTagItem(FortressSoulsTelemetry.LlmModelTagName));
        Assert.Equal(FortressSoulsTelemetry.SuccessOutcome, turnActivity.GetTagItem(FortressSoulsTelemetry.OperationOutcomeTagName));

        Assert.Equal(ProbeObservationToolService.StableToolName, toolActivity.GetTagItem(FortressSoulsTelemetry.ToolNameTagName));
        Assert.Equal(1, Assert.IsType<int>(toolActivity.GetTagItem(FortressSoulsTelemetry.ToolCallIndexTagName)));
        Assert.Equal(1, Assert.IsType<int>(toolActivity.GetTagItem(FortressSoulsTelemetry.ToolRoundIndexTagName)));
        Assert.True(Assert.IsType<int>(toolActivity.GetTagItem(FortressSoulsTelemetry.ToolOutputBytesTagName)) > 0);
        Assert.Equal(FortressSoulsTelemetry.SuccessOutcome, toolActivity.GetTagItem(FortressSoulsTelemetry.OperationOutcomeTagName));
    }

    private static MicrosoftExtensionsAiDwarfAgent CreateAgent(IChatClient chatClient, ProbeObservationToolService toolService) =>
        new(chatClient, new ClosedAgentToolRegistry([toolService.CreateRegistration()]), new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "test-key",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        });

    private static MicrosoftExtensionsAiDwarfAgent CreateAgent(IChatClient chatClient, ThrowingTool tool) =>
        new(chatClient, new ClosedAgentToolRegistry([tool.CreateRegistration()]), new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "test-key",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        });

    private static AgentTurnRequest CreateRequest(
        string userMessage = "What do you see?",
        int maximumRounds = 2,
        int maximumToolCalls = 1,
        int maximumToolResultBytes = 512,
        int maximumTotalToolResultBytes = 1_024,
        TimeSpan? turnTimeout = null,
        TimeSpan? toolTimeout = null,
        AgentSessionContext? session = null) =>
        new(
            session ?? CreateSessionContext(),
            userMessage,
            new AgentExecutionPolicy(
                MaximumRounds: maximumRounds,
                MaximumToolCalls: maximumToolCalls,
                MaximumToolResultBytes: maximumToolResultBytes,
                MaximumTotalToolResultBytes: maximumTotalToolResultBytes,
                TurnTimeout: turnTimeout ?? TimeSpan.FromSeconds(5),
                ToolTimeout: toolTimeout ?? TimeSpan.FromSeconds(1)));

    private static AgentSessionContext CreateSessionContext(
        DwarfId? requestedDwarfId = null,
        DwarfId? identityDwarfId = null)
    {
        var dwarfId = DwarfId.Parse("4101");
        var snapshotRequestedDwarfId = requestedDwarfId ?? dwarfId;
        var snapshotIdentityId = identityDwarfId ?? dwarfId;

        return new AgentSessionContext(
            SessionId: "session-4101",
            DwarfId: dwarfId,
            Snapshot: new DwarfSnapshot(
                SchemaVersion: DwarfSchemaVersions.Snapshot,
                Source: new DwarfSnapshotSourceMetadata(
                    WorldLoaded: true,
                    SiteLoaded: true,
                    MapLoaded: true,
                    SoulPresent: true),
                RequestedDwarfId: snapshotRequestedDwarfId,
                Identity: new DwarfIdentity(
                    Id: snapshotIdentityId,
                    ReadableName: "Urist McProbe",
                    ProfessionName: "Miner",
                    ProfessionToken: "MINER",
                    CreatureId: "DWARF",
                    CasteId: "MALE"),
                Work: new DwarfWork(CurrentJobType: "HaulStone"),
                Stress: new DwarfStress(
                    Raw: 0,
                    Longterm: 0,
                    Category: 3,
                    CategoryScale: "0-most-stressed-6-least-stressed"),
                Skills: new DwarfSkillCollection(Count: 0, Items: []),
                Personality: new DwarfPersonality(
                    Present: true,
                    Traits: new DwarfTraitCollection(Count: 0, Items: []),
                    Values: new DwarfValueCollection(Count: 0, Items: []),
                    Needs: new DwarfNeedCollection(Count: 0, Items: []),
                    Mannerisms: new DwarfMannerismCollection(Count: 0, Items: [])),
                PromptCandidates: new DwarfPromptCandidates([], [], [], [], [])),
            Conversation: []);
    }

    private static Dictionary<string, object?> ValidArguments(
        string subject,
        bool emitLargePayload = false,
        int delayMs = 0,
        int repeatCount = 2) =>
        new()
        {
            ["subject"] = subject,
            ["repeatCount"] = repeatCount,
            ["emitLargePayload"] = emitLargePayload,
            ["delayMs"] = delayMs
        };

    private static void AssertBudgetExhaustedToolMessage(IReadOnlyList<ChatMessage> messages, string expectedCallId)
    {
        var toolMessage = Assert.Single(
            messages,
            message => message.Role == AiChatRole.Tool
                && message.Contents.OfType<FunctionResultContent>().Any(content => string.Equals(content.CallId, expectedCallId, StringComparison.Ordinal)));

        var result = Assert.Single(toolMessage.Contents.OfType<FunctionResultContent>());
        var observation = Assert.IsType<JsonElement>(result.Result);
        Assert.Equal(AgentToolOutcomes.BudgetExhausted, observation.GetProperty("outcome").GetString());
    }

    private sealed class SequenceChatClient(params ChatResponse[] responses) : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new(responses);

        public int RequestCount { get; private set; }

        public List<IReadOnlyList<ChatMessage>> RequestMessages { get; } = [];

        public List<ChatOptions?> RequestOptions { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            RequestMessages.Add(messages.ToArray());
            RequestOptions.Add(options);
            return Task.FromResult(_responses.Dequeue());
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CallbackChatClient(Func<CancellationToken, Task<ChatResponse>> callback) : IChatClient
    {
        private readonly Func<CancellationToken, Task<ChatResponse>> _callback = callback;

        public int RequestCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            return _callback(cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingTool
    {
        private int _invocationCount;

        public int InvocationCount => _invocationCount;

        public AgentToolRegistration CreateRegistration() => new(
            new AgentToolDefinition(
                ProbeObservationToolService.StableToolName,
                "Throw for regression coverage."),
            ExecuteAsync);

        private Task<AgentToolResult> ExecuteAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _invocationCount);
            throw new InvalidOperationException("boom");
        }
    }
}
