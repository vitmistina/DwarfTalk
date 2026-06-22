namespace FortressSouls.Tests;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FortressSouls.Application;
using FortressSouls.Domain;
using FortressSouls.Llm;
using FortressSouls.Observability;
using Microsoft.Extensions.AI;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

public sealed class OpenAiCompatibleToolLoopChatClientTests
{
    [Fact]
    public async Task RunTurnAsync_ExercisesOpenAiCompatibleEndpointDeterministically()
    {
        var handler = new StubHandler([
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":null,"tool_calls":[{"id":"call-1","function":{"name":"probe_observe","arguments":"{\"subject\":\"ore bins\",\"repeatCount\":2,\"emitLargePayload\":false,\"delayMs\":0}"}}]}}]}""", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"The ore bins are near the stockpile."}}]}""", Encoding.UTF8, "application/json")
            }
        ]);

        var options = new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "test-key",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.GetValidatedEndpointUri(),
            Timeout = Timeout.InfiniteTimeSpan
        };

        var agent = CreateAgent(httpClient, options);

        var result = await agent.RunTurnAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.Equal("The ore bins are near the stockpile.", result.AssistantMessage);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.RequestUris[0]);
        Assert.Contains("\"tools\":[{\"type\":\"function\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"name\":\"probe_observe\"", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.Contains("\"tool_calls\":[{\"id\":\"call-1\"", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Contains("\"tool_call_id\":\"call-1\"", handler.RequestBodies[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunTurnAsync_AppendsBudgetExhaustedToolResultsAndDisablesToolsOnFinalRetryAfterToolCallLimit()
    {
        var handler = new StubHandler([
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":null,"tool_calls":[{"id":"call-1","function":{"name":"probe_observe","arguments":"{\"subject\":\"ore bins\",\"repeatCount\":2,\"emitLargePayload\":false,\"delayMs\":0}"}},{"id":"call-2","function":{"name":"probe_observe","arguments":"{\"subject\":\"ore bins\",\"repeatCount\":2,\"emitLargePayload\":false,\"delayMs\":0}"}}]}}]}""", Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"I cannot inspect further."}}]}""", Encoding.UTF8, "application/json")
            }
        ]);

        var options = new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "test-key",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.GetValidatedEndpointUri(),
            Timeout = Timeout.InfiniteTimeSpan
        };

        var agent = CreateAgent(httpClient, options);

        var result = await agent.RunTurnAsync(
            CreateRequest(),
            CancellationToken.None);

        Assert.Equal("I cannot inspect further.", result.AssistantMessage);
        Assert.Collection(
            result.ToolReceipts,
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
        Assert.Equal(2, handler.RequestCount);

        using var retryRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        var retryMessages = retryRequest.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        var assistantWithToolCalls = Assert.Single(
            retryMessages,
            message => string.Equals(message.GetProperty("role").GetString(), "assistant", StringComparison.Ordinal));
        Assert.Equal(2, assistantWithToolCalls.GetProperty("tool_calls").GetArrayLength());

        AssertBudgetExhaustedToolMessage(retryMessages, "call-1");
        AssertBudgetExhaustedToolMessage(retryMessages, "call-2");
        Assert.False(retryRequest.RootElement.TryGetProperty("tools", out _));
        Assert.False(retryRequest.RootElement.TryGetProperty("tool_choice", out _));
    }

    [Fact]
    public async Task RunTurnAsync_AppendsBudgetExhaustedToolResultsForCurrentAndRemainingCallsOnFinalRetryAfterCumulativeBudgetOverflow()
    {
        var longSubject = new string('o', 50);
        var argumentsJson = JsonSerializer.Serialize(new
        {
            subject = longSubject,
            repeatCount = 2,
            emitLargePayload = false,
            delayMs = 0
        });
        var toolCallResponse = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = (string?)null,
                        tool_calls = new[]
                        {
                            new { id = "call-1", function = new { name = "probe_observe", arguments = argumentsJson } },
                            new { id = "call-2", function = new { name = "probe_observe", arguments = argumentsJson } },
                            new { id = "call-3", function = new { name = "probe_observe", arguments = argumentsJson } }
                        }
                    }
                }
            }
        });
        var handler = new StubHandler([
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(toolCallResponse, Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"I could not inspect further, but the bins are nearby."}}]}""", Encoding.UTF8, "application/json")
            }
        ]);

        var options = new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "test-key",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.GetValidatedEndpointUri(),
            Timeout = Timeout.InfiniteTimeSpan
        };

        var agent = CreateAgent(httpClient, options);

        var result = await agent.RunTurnAsync(
            CreateRequest(maximumToolCalls: 3, maximumToolResultBytes: 256, maximumTotalToolResultBytes: 260),
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
        Assert.Equal(2, handler.RequestCount);

        using var retryRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        var retryMessages = retryRequest.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        var assistantWithToolCalls = Assert.Single(
            retryMessages,
            message => string.Equals(message.GetProperty("role").GetString(), "assistant", StringComparison.Ordinal));
        Assert.Equal(3, assistantWithToolCalls.GetProperty("tool_calls").GetArrayLength());

        AssertToolMessageExists(retryMessages, "call-1");
        AssertBudgetExhaustedToolMessage(retryMessages, "call-2");
        AssertBudgetExhaustedToolMessage(retryMessages, "call-3");
        Assert.False(retryRequest.RootElement.TryGetProperty("tools", out _));
        Assert.False(retryRequest.RootElement.TryGetProperty("tool_choice", out _));
    }

    [Fact]
    public async Task RunTurnAsync_MapsFinalRetryTransportFailureAfterToolCallBudgetExhaustionToStableUnavailable()
    {
        var handler = new ResponseThenThrowHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":null,"tool_calls":[{"id":"call-1","function":{"name":"probe_observe","arguments":"{\"subject\":\"ore bins\",\"repeatCount\":2,\"emitLargePayload\":false,\"delayMs\":0}"}},{"id":"call-2","function":{"name":"probe_observe","arguments":"{\"subject\":\"ore bins\",\"repeatCount\":2,\"emitLargePayload\":false,\"delayMs\":0}"}}]}}]}""", Encoding.UTF8, "application/json")
            },
            new HttpRequestException("Dial failed for https://openrouter.ai/api/v1/chat/completions"));

        var options = new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "test-key",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.GetValidatedEndpointUri(),
            Timeout = Timeout.InfiniteTimeSpan
        };

        var agent = CreateAgent(httpClient, options);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.Unavailable, exception.ErrorCode);
        Assert.Equal("The agent turn is unavailable.", exception.Message);
        Assert.Equal(2, handler.RequestCount);

        using var retryRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.False(retryRequest.RootElement.TryGetProperty("tools", out _));
        Assert.False(retryRequest.RootElement.TryGetProperty("tool_choice", out _));
    }

    [Fact]
    public async Task RunTurnAsync_MapsOpenAiCompatibleTransportFailureToStableErrorWithoutRetry()
    {
        var handler = new ThrowingHandler(new HttpRequestException("Dial failed for https://openrouter.ai/api/v1/chat/completions"));
        var options = new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "test-key",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.GetValidatedEndpointUri(),
            Timeout = Timeout.InfiniteTimeSpan
        };

        var agent = CreateAgent(httpClient, options);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.Unavailable, exception.ErrorCode);
        Assert.Equal(1, handler.RequestCount);
        Assert.DoesNotContain("openrouter.ai", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTurnAsync_MapsProviderConfigurationFailureToStableUnavailableErrorAndTelemetryCategory()
    {
        var observed = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FortressSoulsTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observed.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var handler = new StubHandler([]);
        var options = new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.GetValidatedEndpointUri(),
            Timeout = Timeout.InfiniteTimeSpan
        };

        var agent = CreateAgent(httpClient, options);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(CreateRequest(), CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.Unavailable, exception.ErrorCode);
        Assert.Equal("The agent turn is unavailable.", exception.Message);
        Assert.Equal(0, handler.RequestCount);

        var turnActivity = Assert.Single(observed, activity => activity.DisplayName == "fortresssouls.agent.turn");
        Assert.Contains(turnActivity.Tags, tag =>
            tag.Key == FortressSoulsTelemetry.ErrorCategoryTagName
            && string.Equals(tag.Value, AgentToolOutcomes.Unavailable, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunTurnAsync_MapsProviderTimeoutToStableTimedOutAndTelemetryCategory()
    {
        var observed = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FortressSoulsTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observed.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var handler = new BlockingHandler();
        var options = new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "test-key",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        };

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.GetValidatedEndpointUri(),
            Timeout = Timeout.InfiniteTimeSpan
        };

        var agent = CreateAgent(httpClient, options);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            agent.RunTurnAsync(
                CreateRequest(turnTimeout: TimeSpan.FromSeconds(10)),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.TimedOut, exception.ErrorCode);
        Assert.Equal("The agent turn timed out.", exception.Message);

        var providerException = exception.InnerException;
        Assert.NotNull(providerException);
        Assert.Equal("LlmProviderException", providerException.GetType().Name);
        Assert.Equal("Timeout", providerException.GetType().GetProperty("ErrorCode")?.GetValue(providerException)?.ToString());
        Assert.Equal(1, handler.RequestCount);

        var turnActivity = Assert.Single(observed, activity => activity.DisplayName == "fortresssouls.agent.turn");
        Assert.Contains(turnActivity.Tags, tag =>
            tag.Key == FortressSoulsTelemetry.ErrorCategoryTagName
            && string.Equals(tag.Value, AgentToolOutcomes.TimedOut, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetResponseAsync_MapsToolCallResponsesToFunctionCallContent()
    {
        var handler = new StubHandler([
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":null,"tool_calls":[{"id":"call-1","function":{"name":"probe_observe","arguments":"{\"subject\":\"ore bins\",\"repeatCount\":2,\"emitLargePayload\":false,\"delayMs\":0}"}}]}}]}""", Encoding.UTF8, "application/json")
            }
        ]);
        var options = new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "test-key",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.GetValidatedEndpointUri(),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = new OpenAiCompatibleToolLoopChatClient(httpClient, options);

        var function = AIFunctionFactory.Create(
            (string subject, int repeatCount, bool emitLargePayload, int delayMs, CancellationToken cancellationToken) =>
                Task.FromResult(new ProbeObservationResult("probe.v1", subject)),
            "probe_observe",
            "probe",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            });

        var response = await client.GetResponseAsync(
            [new ChatMessage(AiChatRole.User, "What do you see?")],
            new ChatOptions { Tools = [function] },
            CancellationToken.None);

        var message = Assert.Single(response.Messages);
        var functionCall = Assert.Single(message.Contents.OfType<FunctionCallContent>());
        Assert.Equal("probe_observe", functionCall.Name);
        Assert.Equal("call-1", functionCall.CallId);
    }

    [Fact]
    public async Task GetResponseAsync_RejectsMalformedToolCallArgumentsJsonWithStableInvalidResponse()
    {
        var handler = new StubHandler([
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":null,"tool_calls":[{"id":"call-1","function":{"name":"probe_observe","arguments":"{not-json"}}]}}]}""", Encoding.UTF8, "application/json")
            }
        ]);
        var options = new LlmProviderOptions
        {
            ProviderType = LlmProviderType.OpenAiCompatible,
            Endpoint = "https://openrouter.ai/api/v1",
            Model = "deepseek/deepseek-v3.2",
            ApiKey = "test-key",
            MaxOutputTokens = 500,
            Temperature = 0.85,
            TimeoutSeconds = 5
        };
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = options.GetValidatedEndpointUri(),
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = new OpenAiCompatibleToolLoopChatClient(httpClient, options);

        var function = AIFunctionFactory.Create(
            (string subject, int repeatCount, bool emitLargePayload, int delayMs, CancellationToken cancellationToken) =>
                Task.FromResult(new ProbeObservationResult("probe.v1", subject)),
            "probe_observe",
            "probe",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            });

        var exception = await Record.ExceptionAsync(() =>
            client.GetResponseAsync(
                [new ChatMessage(AiChatRole.User, "What do you see?")],
                new ChatOptions { Tools = [function] },
                CancellationToken.None));

        Assert.NotNull(exception);
        Assert.Equal("LlmProviderException", exception.GetType().Name);
        Assert.Equal("InvalidResponse", exception.GetType().GetProperty("ErrorCode")?.GetValue(exception)?.ToString());
        Assert.Equal("The chat provider response was malformed.", exception.Message);
        Assert.IsType<JsonException>(exception.InnerException);
        Assert.Equal(1, handler.RequestCount);
    }

    private static MicrosoftExtensionsAiDwarfAgent CreateAgent(HttpClient httpClient, LlmProviderOptions options) =>
        new(
            new OpenAiCompatibleToolLoopChatClient(httpClient, options),
            new ClosedAgentToolRegistry([new ProbeObservationToolService().CreateRegistration()]),
            options);

    private static AgentTurnRequest CreateRequest(
        int maximumRounds = 2,
        int maximumToolCalls = 1,
        int maximumToolResultBytes = 512,
        int maximumTotalToolResultBytes = 1024,
        TimeSpan? turnTimeout = null) =>
        new(
            CreateSessionContext(),
            "What do you see?",
            new AgentExecutionPolicy(
                maximumRounds,
                maximumToolCalls,
                maximumToolResultBytes,
                maximumTotalToolResultBytes,
                turnTimeout ?? TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(1)));

    private static AgentSessionContext CreateSessionContext()
    {
        var dwarfId = DwarfId.Parse("4101");

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
                RequestedDwarfId: dwarfId,
                Identity: new DwarfIdentity(
                    Id: dwarfId,
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

    private static void AssertBudgetExhaustedToolMessage(JsonElement[] retryMessages, string expectedCallId)
    {
        var toolMessage = Assert.Single(
            retryMessages,
            message => string.Equals(message.GetProperty("role").GetString(), "tool", StringComparison.Ordinal)
                && string.Equals(message.GetProperty("tool_call_id").GetString(), expectedCallId, StringComparison.Ordinal));

        var content = toolMessage.GetProperty("content").GetString();
        Assert.False(string.IsNullOrWhiteSpace(content));

        using var payload = JsonDocument.Parse(content!);
        Assert.Equal(AgentToolOutcomes.BudgetExhausted, payload.RootElement.GetProperty("outcome").GetString());
    }

    private static void AssertToolMessageExists(JsonElement[] retryMessages, string expectedCallId)
    {
        Assert.Contains(
            retryMessages,
            message => string.Equals(message.GetProperty("role").GetString(), "tool", StringComparison.Ordinal)
                && string.Equals(message.GetProperty("tool_call_id").GetString(), expectedCallId, StringComparison.Ordinal));
    }

    private sealed class StubHandler(IReadOnlyList<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<string> RequestBodies { get; } = [];

        public List<string> RequestUris { get; } = [];

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUris.Add(request.RequestUri!.ToString());
            RequestBodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        private readonly Exception _exception = exception;

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromException<HttpResponseMessage>(_exception);
        }
    }

    private sealed class ResponseThenThrowHandler(HttpResponseMessage response, Exception exception) : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response = response;
        private readonly Exception _exception = exception;

        public List<string> RequestBodies { get; } = [];

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestBodies.Add(request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty);

            return RequestCount switch
            {
                1 => Task.FromResult(_response),
                2 => Task.FromException<HttpResponseMessage>(_exception),
                _ => throw new InvalidOperationException("The regression handler received more requests than expected.")
            };
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking handler should be canceled before returning.");
        }
    }
}