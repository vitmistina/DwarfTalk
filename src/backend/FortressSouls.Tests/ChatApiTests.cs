namespace FortressSouls.Tests;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FortressSouls.Api;
using FortressSouls.Application;
using FortressSouls.Domain;
using FortressSouls.Llm;
using FortressSouls.Prompting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

public sealed class ChatApiTests
{
    [Fact]
    public async Task ChatLifecycle_CreateSendAndPreview_WorksInDevelopment()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101"));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateChatSessionResponse>();
        Assert.NotNull(created);

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("How goes the mine?"));
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);
        var sent = await sendResponse.Content.ReadFromJsonAsync<SendChatMessageResponse>();
        Assert.NotNull(sent);
        Assert.Equal(created.SessionId, sent!.SessionId);
        Assert.Equal("4101", sent.DwarfId);
        Assert.Equal("assistant", sent.AssistantMessage.Role);
        Assert.False(string.IsNullOrWhiteSpace(sent.AssistantMessage.Text));
        Assert.Equal("Fake", sent.Diagnostics.Provider);
        Assert.Equal("fake-dwarf", sent.Diagnostics.Model);
        Assert.Empty(sent.ToolReceipts);

        var previewResponse = await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview");
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await previewResponse.Content.ReadFromJsonAsync<PromptPreviewResponse>();
        Assert.NotNull(preview);
        Assert.Equal(created.SessionId, preview!.SessionId);
        Assert.Equal("4101", preview.DwarfId);
        Assert.Contains("PLAYER_MESSAGE_JSON:", preview.PromptText, StringComparison.Ordinal);
        Assert.Contains("How goes the mine?", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerceptionQuestion_UsesFakeAgentTurnAndReturnsSafeLookAroundReceipt()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("What do you see around you right now?"));
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        using var sent = JsonDocument.Parse(await sendResponse.Content.ReadAsStreamAsync());
        var root = sent.RootElement;
        Assert.Equal(created.SessionId, root.GetProperty("sessionId").GetString());
        Assert.Equal("4101", root.GetProperty("dwarfId").GetString());

        var assistantText = root.GetProperty("assistantMessage").GetProperty("text").GetString();
        Assert.False(string.IsNullOrWhiteSpace(assistantText));
        Assert.Contains("wall", assistantText!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("3 visible units", assistantText, StringComparison.Ordinal);
        Assert.DoesNotContain("dwarf-shaped", assistantText, StringComparison.OrdinalIgnoreCase);

        var receipts = root.GetProperty("toolReceipts").EnumerateArray().ToArray();
        var receipt = Assert.Single(receipts);
        Assert.Equal("look_around", receipt.GetProperty("tool").GetString());
        Assert.Equal("success", receipt.GetProperty("outcome").GetString());

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.Contains("ENABLED_TOOLS: look_around", preview!.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("call-1", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"cells\"", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LongPerceptionQuestionWithinAcceptedEnvelope_StillUsesPerceptionPath()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var longQuestion = string.Concat(Enumerable.Repeat(
            "What do you see around you right now near the workshops and stockpiles? ",
            8));
        Assert.True(longQuestion.Length > 500);
        Assert.True(longQuestion.Length <= 1_200);

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest(longQuestion));
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var sent = await sendResponse.Content.ReadFromJsonAsync<SendChatMessageResponse>();
        Assert.NotNull(sent);
        Assert.False(string.IsNullOrWhiteSpace(sent!.AssistantMessage.Text));
        Assert.Collection(
            sent.ToolReceipts,
            receipt =>
            {
                Assert.Equal("look_around", receipt.Tool);
                Assert.Equal("success", receipt.Outcome);
            });
    }

    [Fact]
    public async Task ExplicitLookAroundRequest_UsesFakeAgentTurnWithoutAroundYouPhrase()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("Look around and tell me what you see."));
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var sent = await sendResponse.Content.ReadFromJsonAsync<SendChatMessageResponse>();
        Assert.NotNull(sent);
        Assert.Collection(
            sent!.ToolReceipts,
            receipt =>
            {
                Assert.Equal("look_around", receipt.Tool);
                Assert.Equal("success", receipt.Outcome);
            });

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.Contains("ENABLED_TOOLS: look_around", preview!.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StockQuestion_UsesInspectStocksToolAndReturnsExactSafeReceipt()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("How much wood do we have in stock?"));
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        using var sent = JsonDocument.Parse(await sendResponse.Content.ReadAsStreamAsync());
        var root = sent.RootElement;

        var assistantText = root.GetProperty("assistantMessage").GetProperty("text").GetString();
        Assert.Equal("I can account for 48 wood.", assistantText);
        Assert.DoesNotContain("~48", assistantText, StringComparison.Ordinal);

        var receipts = root.GetProperty("toolReceipts").EnumerateArray().ToArray();
        var receipt = Assert.Single(receipts);
        Assert.Equal("inspect_stocks", receipt.GetProperty("tool").GetString());
        Assert.Equal("success", receipt.GetProperty("outcome").GetString());

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.Contains("ENABLED_TOOLS: inspect_stocks", preview!.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("look_around", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"tool\":\"{FakePerceptionToolService.ListDwarvesToolName}\"", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"tool\":\"{FakePerceptionToolService.InspectDwarfToolName}\"", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"categories\"", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StockQuestion_UsesRegisteredStockInspectionService_WhenOverridden()
    {
        using var factory = CreateFactory(
            "Development",
            stockInspectionService: new StubStockInspectionService(
                new InspectStocksToolResult(
                    SchemaVersion: "fortress-souls.inspect-stocks-result.v0.2",
                    GameTime: "126:9000",
                    RequestedCategory: "wood",
                    Categories:
                    [
                        new StockCategory("wood", 7)
                    ],
                    Warnings: [])));
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("How much wood do we have in stock?"));
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var sent = await sendResponse.Content.ReadFromJsonAsync<SendChatMessageResponse>();
        Assert.NotNull(sent);
        Assert.Equal("I can account for 7 wood.", sent!.AssistantMessage.Text);
        Assert.Collection(
            sent.ToolReceipts,
            receipt =>
            {
                Assert.Equal("inspect_stocks", receipt.Tool);
                Assert.Equal("success", receipt.Outcome);
            });
    }

    [Fact]
    public async Task OtherDwarfQuestion_UsesListAndInspectToolsAndReturnsSafeAdditiveReceipts()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("Tell me about another dwarf in the fortress."));
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        using var sent = JsonDocument.Parse(await sendResponse.Content.ReadAsStreamAsync());
        var root = sent.RootElement;

        var assistantText = root.GetProperty("assistantMessage").GetProperty("text").GetString();
        Assert.False(string.IsNullOrWhiteSpace(assistantText));
        Assert.Contains("Nil Stonereed", assistantText!, StringComparison.Ordinal);
        Assert.Contains("HarvestPlants", assistantText, StringComparison.Ordinal);

        var receipts = root.GetProperty("toolReceipts").EnumerateArray().ToArray();
        Assert.Equal(2, receipts.Length);
        Assert.Equal(["tool", "outcome"], receipts[0].EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(["tool", "outcome"], receipts[1].EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal("list_dwarves", receipts[0].GetProperty("tool").GetString());
        Assert.Equal("success", receipts[0].GetProperty("outcome").GetString());
        Assert.Equal("inspect_dwarf", receipts[1].GetProperty("tool").GetString());
        Assert.Equal("success", receipts[1].GetProperty("outcome").GetString());

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.Contains("ENABLED_TOOLS: inspect_dwarf, list_dwarves", preview!.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("look_around", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("call-1", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"dwarves\"", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"dwarfId\":\"4102\"", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"currentJobType\":\"HarvestPlants\"", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NearbyReferenceWithoutLookAroundIntent_StaysOnPlainChatPath()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var sendResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("Are the ore bins nearby?"));
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var sent = await sendResponse.Content.ReadFromJsonAsync<SendChatMessageResponse>();
        Assert.NotNull(sent);
        Assert.Empty(sent!.ToolReceipts);

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.DoesNotContain("ENABLED_TOOLS: look_around", preview!.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulDwarfInspectionTurn_PersistsOnlyPlayerAndFinalAssistantMessages()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var inspectionResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("Tell me about another dwarf in the fortress."));
        Assert.Equal(HttpStatusCode.OK, inspectionResponse.StatusCode);

        var followUpResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created.SessionId}/messages",
            new SendChatMessageRequest("How goes the mine?"));
        Assert.Equal(HttpStatusCode.OK, followUpResponse.StatusCode);

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.Contains("Tell me about another dwarf in the fortress.", preview!.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("list_dwarves", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("inspect_dwarf", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("call-1", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"dwarves\"", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"dwarfId\":\"4102\"", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"currentJobType\":\"HarvestPlants\"", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulPerceptionTurn_PersistsOnlyPlayerAndFinalAssistantMessages()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var perceptionResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("What do you see around you right now?"));
        Assert.Equal(HttpStatusCode.OK, perceptionResponse.StatusCode);

        var followUpResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created.SessionId}/messages",
            new SendChatMessageRequest("How goes the mine?"));
        Assert.Equal(HttpStatusCode.OK, followUpResponse.StatusCode);

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.Contains("What do you see around you right now?", preview!.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("look_around", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("call-1", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"cells\"", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PerceptionTurn_UsesBoundedPromptHistoryWithoutReplayingRawConversationToProvider()
    {
        var chatClient = new CapturingToolLoopChatClient();
        using var factory = CreateFactory(
            "Development",
            provider: new CountingChatProvider(),
            chatClient: chatClient);
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        for (var turn = 1; turn <= 7; turn++)
        {
            var historyResponse = await client.PostAsJsonAsync(
                $"/api/chat/sessions/{created!.SessionId}/messages",
                new SendChatMessageRequest($"history-{turn:D2}"));
            Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        }

        var perceptionResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("What do you see around you right now?"));
        Assert.Equal(HttpStatusCode.OK, perceptionResponse.StatusCode);

        Assert.Equal(2, chatClient.RequestCount);

        var initialMessages = chatClient.RequestMessages[0];
        var userMessage = Assert.Single(initialMessages);
        Assert.Equal(AiChatRole.User, userMessage.Role);
        Assert.Equal("What do you see around you right now?", userMessage.Text);

        var initialInstructions = chatClient.RequestOptions[0]?.Instructions;
        Assert.False(string.IsNullOrWhiteSpace(initialInstructions));
        Assert.DoesNotContain("history-01", initialInstructions, StringComparison.Ordinal);
        Assert.Contains("history-02", initialInstructions, StringComparison.Ordinal);
        Assert.Contains("history-07", initialInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedPerceptionTurn_DoesNotAppendPartialHistory()
    {
        using var factory = CreateFactory("Development", agent: new InvalidDataAgent());
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var failedResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("What do you see around you right now?"));
        Assert.Equal(HttpStatusCode.BadGateway, failedResponse.StatusCode);

        var successResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created.SessionId}/messages",
            new SendChatMessageRequest("good-message"));
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.DoesNotContain("What do you see around you right now?", preview!.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("look_around", preview.PromptText, StringComparison.Ordinal);
        Assert.Contains("good-message", preview.PromptText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("4102")]
    [InlineData("not-a-dwarf")]
    public async Task FailedDwarfInspectionTurn_DoesNotAppendPartialHistory(string attemptedDwarfId)
    {
        using var factory = CreateFactory("Development", chatClient: new InspectDwarfOnlyChatClient(attemptedDwarfId));
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var failedResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("Tell me about another dwarf in the fortress."));
        Assert.Equal(HttpStatusCode.BadGateway, failedResponse.StatusCode);

        var successResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created.SessionId}/messages",
            new SendChatMessageRequest("good-message"));
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.DoesNotContain("Tell me about another dwarf in the fortress.", preview!.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("inspect_dwarf", preview.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("call-1", preview.PromptText, StringComparison.Ordinal);
        Assert.Contains("good-message", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PromptPreviewEndpoint_IsNotMappedOutsideDevelopment()
    {
        using var factory = CreateFactory("Production");
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101"));
        var created = await createResponse.Content.ReadFromJsonAsync<CreateChatSessionResponse>();
        Assert.NotNull(created);

        var response = await client.GetAsync($"/api/chat/sessions/{created!.SessionId}/prompt-preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SessionsAreProcessLocal_AndDoNotPersistAcrossFactoryRestart()
    {
        string sessionId;
        using (var factory = CreateFactory("Development"))
        using (var client = factory.CreateClient())
        {
            var createResponse = await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4102"));
            var created = await createResponse.Content.ReadFromJsonAsync<CreateChatSessionResponse>();
            sessionId = created!.SessionId;
        }

        using var restartedFactory = CreateFactory("Development");
        using var restartedClient = restartedFactory.CreateClient();
        var sendResponse = await restartedClient.PostAsJsonAsync(
            $"/api/chat/sessions/{sessionId}/messages",
            new SendChatMessageRequest("Still there?"));

        Assert.Equal(HttpStatusCode.NotFound, sendResponse.StatusCode);
        var error = await sendResponse.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.Equal("chat_session_not_found", error!.ErrorCode);
    }

    [Fact]
    public async Task SessionStore_AppliesDeterministicBoundsForSessionsAndHistory()
    {
        var options = new ChatSessionOptions
        {
            MaxSessions = 1,
            MaxHistoryMessages = 4,
            MaxPlayerMessageCharacters = 1_200,
            MaxAssistantMessageCharacters = 1_200,
            PromptAssembly = PromptAssemblyOptions.Default
        };

        using var factory = CreateFactory("Development", options: options);
        using var client = factory.CreateClient();

        var first = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();
        var second = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4102")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var evictedSend = await client.PostAsJsonAsync($"/api/chat/sessions/{first!.SessionId}/messages", new SendChatMessageRequest("evicted?"));
        Assert.Equal(HttpStatusCode.NotFound, evictedSend.StatusCode);

        await client.PostAsJsonAsync($"/api/chat/sessions/{second!.SessionId}/messages", new SendChatMessageRequest("m1-oldest"));
        await client.PostAsJsonAsync($"/api/chat/sessions/{second.SessionId}/messages", new SendChatMessageRequest("m2-middle"));
        await client.PostAsJsonAsync($"/api/chat/sessions/{second.SessionId}/messages", new SendChatMessageRequest("m3-middle"));
        await client.PostAsJsonAsync($"/api/chat/sessions/{second.SessionId}/messages", new SendChatMessageRequest("m4-latest"));

        var preview = await (await client.GetAsync($"/api/chat/sessions/{second.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.DoesNotContain("m1-oldest", preview!.PromptText, StringComparison.Ordinal);
        Assert.Contains("m2-middle", preview.PromptText, StringComparison.Ordinal);
        Assert.Contains("m3-middle", preview.PromptText, StringComparison.Ordinal);
        Assert.Contains("m4-latest", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionBindsDwarfIdentityAndPreventsCrossDwarfMixing()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4103")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();
        await client.PostAsJsonAsync($"/api/chat/sessions/{created!.SessionId}/messages", new SendChatMessageRequest("records?"));

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();

        Assert.NotNull(preview);
        Assert.Contains("\"dwarfId\":\"4103\"", preview!.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("\"dwarfId\":\"4102\"", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentTurns_ReturnConflictAndDoNotMutateFailedTurnHistory()
    {
        var provider = new BlockingChatProvider();
        using var factory = CreateFactory("Development", provider: provider);
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var firstTurn = client.PostAsJsonAsync($"/api/chat/sessions/{created!.SessionId}/messages", new SendChatMessageRequest("first-turn"));
        await provider.WaitUntilEnteredAsync();

        var secondTurn = await client.PostAsJsonAsync($"/api/chat/sessions/{created.SessionId}/messages", new SendChatMessageRequest("second-turn"));
        Assert.Equal(HttpStatusCode.Conflict, secondTurn.StatusCode);

        provider.Release();
        var firstResponse = await firstTurn;
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();
        Assert.NotNull(preview);
        Assert.Contains("first-turn", preview!.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("second-turn", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedOrCancelledTurn_DoesNotAppendPartialAssistantOrPlayer()
    {
        using var factory = CreateFactory("Development", provider: new FailingThenSuccessProvider());
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var failedResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("fail-this-message"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failedResponse.StatusCode);

        var successResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created.SessionId}/messages",
            new SendChatMessageRequest("good-message"));
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();
        Assert.NotNull(preview);
        Assert.DoesNotContain("fail-this-message", preview!.PromptText, StringComparison.Ordinal);
        Assert.Contains("good-message", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledTurn_DoesNotAppendPlayerHistory()
    {
        using var factory = CreateFactory("Development", provider: new CancelledThenSuccessProvider());
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();

        var cancelledResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("cancelled-message"));
        Assert.Equal(HttpStatusCode.RequestTimeout, cancelledResponse.StatusCode);

        var successResponse = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created.SessionId}/messages",
            new SendChatMessageRequest("survived-message"));
        Assert.Equal(HttpStatusCode.OK, successResponse.StatusCode);

        var preview = await (await client.GetAsync($"/api/chat/sessions/{created.SessionId}/prompt-preview"))
            .Content.ReadFromJsonAsync<PromptPreviewResponse>();
        Assert.NotNull(preview);
        Assert.DoesNotContain("cancelled-message", preview!.PromptText, StringComparison.Ordinal);
        Assert.Contains("survived-message", preview.PromptText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatTurnTelemetry_IsNestedAndContentFree()
    {
        var observed = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FortressSouls.Observability.FortressSoulsTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observed.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(listener);

        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/chat/sessions", new CreateChatSessionRequest("4101")))
            .Content.ReadFromJsonAsync<CreateChatSessionResponse>();
        var response = await client.PostAsJsonAsync(
            $"/api/chat/sessions/{created!.SessionId}/messages",
            new SendChatMessageRequest("SENTINEL-CONTENT-DO-NOT-LEAK"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var observedSnapshot = observed.ToArray();

        var chatTurn = Assert.Single(observedSnapshot, activity => activity.DisplayName == "fortresssouls.chat.turn");
        var prompt = Assert.Single(observedSnapshot, activity => activity.DisplayName == "fortresssouls.prompt.assemble");
        var llm = Assert.Single(observedSnapshot, activity => activity.DisplayName == "fortresssouls.llm.chat");

        Assert.Equal(chatTurn.SpanId, prompt.ParentSpanId);
        Assert.Equal(chatTurn.SpanId, llm.ParentSpanId);
        Assert.Equal("4101", chatTurn.GetTagItem("fortresssouls.dwarf.id"));
        Assert.Equal(DwarfSchemaVersions.Snapshot, chatTurn.GetTagItem("fortresssouls.snapshot.schema_version"));
        Assert.Equal("Fake", llm.GetTagItem("fortresssouls.provider.type"));
        Assert.Equal("fake-dwarf", llm.GetTagItem("fortresssouls.llm.model"));

        Assert.DoesNotContain(observedSnapshot.SelectMany(activity => activity.Tags), tag =>
            (tag.Value?.ToString() ?? string.Empty).Contains("SENTINEL-CONTENT-DO-NOT-LEAK", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendMessage_WithMalformedSessionId_ReturnsBadRequest()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/chat/sessions/not-a-session/messages",
            new SendChatMessageRequest("hello"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("invalid_session_id", error!.ErrorCode);
    }

    [Fact]
    public async Task PromptPreview_WithWhitespaceSessionId_ReturnsBadRequest()
    {
        using var factory = CreateFactory("Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/chat/sessions/%20/prompt-preview");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("invalid_session_id", error!.ErrorCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environmentName,
        IChatProvider? provider = null,
        ChatSessionOptions? options = null,
        IDwarfAgent? agent = null,
        IChatClient? chatClient = null,
        IStockInspectionService? stockInspectionService = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environmentName);
                builder.ConfigureServices(services =>
                {
                    if (provider is not null)
                    {
                        services.RemoveAll<IChatProvider>();
                        services.AddSingleton(provider);
                    }

                    if (options is not null)
                    {
                        services.RemoveAll<ChatSessionOptions>();
                        services.AddSingleton(options);
                    }

                    if (agent is not null)
                    {
                        services.RemoveAll<IDwarfAgent>();
                        services.AddSingleton(agent);
                    }

                    if (chatClient is not null)
                    {
                        services.RemoveAll<IChatClient>();
                        services.AddSingleton(chatClient);
                    }

                    if (stockInspectionService is not null)
                    {
                        services.RemoveAll<IStockInspectionService>();
                        services.AddSingleton(stockInspectionService);
                    }
                });
            });

    private sealed class BlockingChatProvider : IChatProvider
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ChatProviderResponse> SendAsync(ChatProviderRequest request, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new ChatProviderResponse("blocked-response", "BlockingFake", "blocking-model", TimeSpan.FromMilliseconds(10));
        }

        public Task WaitUntilEnteredAsync() => _entered.Task;

        public void Release() => _release.TrySetResult();
    }

    private sealed class FailingThenSuccessProvider : IChatProvider
    {
        private int _callCount;

        public Task<ChatProviderResponse> SendAsync(ChatProviderRequest request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (call == 1)
            {
                throw new ChatProviderException(ChatProviderErrorCode.Unavailable, "Simulated provider outage.");
            }

            return Task.FromResult(new ChatProviderResponse("recovered-response", "Fake", "fake-dwarf", TimeSpan.FromMilliseconds(25)));
        }
    }

    private sealed class CancelledThenSuccessProvider : IChatProvider
    {
        private int _callCount;

        public Task<ChatProviderResponse> SendAsync(ChatProviderRequest request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            if (call == 1)
            {
                return Task.FromCanceled<ChatProviderResponse>(new CancellationToken(canceled: true));
            }

            return Task.FromResult(new ChatProviderResponse("recovered-response", "Fake", "fake-dwarf", TimeSpan.FromMilliseconds(25)));
        }
    }

    private sealed class CountingChatProvider : IChatProvider
    {
        private int _callCount;

        public Task<ChatProviderResponse> SendAsync(ChatProviderRequest request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            return Task.FromResult(new ChatProviderResponse($"reply-{call:D2}", "Fake", "fake-dwarf", TimeSpan.FromMilliseconds(10)));
        }
    }

    private sealed class CapturingToolLoopChatClient : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new(
        [
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    FakePerceptionToolService.LookAroundToolName,
                    new Dictionary<string, object?>
                    {
                        ["radius"] = 1
                    })])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I can see wall nearby."))
        ]);

        public int RequestCount => RequestMessages.Count;

        public List<IReadOnlyList<ChatMessage>> RequestMessages { get; } = [];

        public List<ChatOptions?> RequestOptions { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

    private sealed class InspectDwarfOnlyChatClient(string dwarfId) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent(
                    "call-1",
                    FakePerceptionToolService.InspectDwarfToolName,
                    new Dictionary<string, object?>
                    {
                        ["dwarfId"] = dwarfId
                    })])));
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

    private sealed class InvalidDataAgent : IDwarfAgent
    {
        public Task<AgentTurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken) =>
            throw new AgentTurnException(AgentTurnErrorCode.InvalidData, "Simulated malformed tool payload.");
    }

    private sealed class StubStockInspectionService(InspectStocksToolResult result) : IStockInspectionService
    {
        public Task<InspectStocksToolResult> InspectStocksAsync(string requestedCategory, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed record ApiErrorResponse(string ErrorCode, string Message);
}
