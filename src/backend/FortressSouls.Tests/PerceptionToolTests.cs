namespace FortressSouls.Tests;

using System.Text.Json;
using FortressSouls.Application;
using FortressSouls.Domain;
using FortressSouls.DwarfFortress;
using FortressSouls.Llm;
using Microsoft.Extensions.AI;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

public sealed class PerceptionToolTests
{
    [Fact]
    public async Task ExecuteAsync_LookAround_ReturnsDeterministicRedactedFixture()
    {
        var registry = CreateRegistry();

        var result = await registry.ExecuteAsync(
            CreateInvocation(registry, FakePerceptionToolService.LookAroundToolName, new { radius = 1 }),
            CancellationToken.None);

        Assert.Equal(FakePerceptionToolService.LookAroundToolName, result.Tool.Name);
        Assert.Equal("fortress-souls.look-around-result.v0.2", result.Content.GetProperty("schemaVersion").GetString());

        var bounds = result.Content.GetProperty("bounds");
        Assert.Equal(1, bounds.GetProperty("radius").GetInt32());
        Assert.Equal(3, bounds.GetProperty("width").GetInt32());
        Assert.Equal(3, bounds.GetProperty("height").GetInt32());

        var cells = result.Content.GetProperty("cells").EnumerateArray().ToArray();
        Assert.Equal(9, cells.Length);

        var hiddenCell = Assert.Single(cells, cell => string.Equals(cell.GetProperty("visibility").GetString(), "hidden", StringComparison.Ordinal));
        Assert.Equal(-1, hiddenCell.GetProperty("dx").GetInt32());
        Assert.Equal(-1, hiddenCell.GetProperty("dy").GetInt32());
        Assert.False(hiddenCell.TryGetProperty("terrainClass", out _));
        Assert.False(hiddenCell.TryGetProperty("walkable", out _));
        Assert.False(hiddenCell.TryGetProperty("featureClass", out _));
        Assert.False(hiddenCell.TryGetProperty("unitCount", out _));

        var legend = result.Content.GetProperty("legend").EnumerateArray().Select(entry => entry.GetString()!).ToArray();
        Assert.Equal(["building", "floor", "ramp", "wall"], legend);
    }

    [Fact]
    public async Task ExecuteAsync_LookAround_AcceptsMaximumBoundedRadius()
    {
        var registry = CreateRegistry();

        var result = await registry.ExecuteAsync(
            CreateInvocation(registry, FakePerceptionToolService.LookAroundToolName, new { radius = 2 }),
            CancellationToken.None);

        var bounds = result.Content.GetProperty("bounds");
        Assert.Equal(2, bounds.GetProperty("radius").GetInt32());
        Assert.Equal(5, bounds.GetProperty("width").GetInt32());
        Assert.Equal(5, bounds.GetProperty("height").GetInt32());

        var cells = result.Content.GetProperty("cells").EnumerateArray().ToArray();
        Assert.Equal(25, cells.Length);
        Assert.True(cells.Count(cell => string.Equals(cell.GetProperty("visibility").GetString(), "hidden", StringComparison.Ordinal)) >= 2);
    }

    [Fact]
    public async Task ExecuteAsync_InspectStocks_ReturnsAllowlistedCategoriesInStableOrder()
    {
        var registry = CreateRegistry();

        var result = await registry.ExecuteAsync(
            CreateInvocation(registry, FakePerceptionToolService.InspectStocksToolName, new { category = "all" }),
            CancellationToken.None);

        Assert.Equal("fortress-souls.inspect-stocks-result.v0.2", result.Content.GetProperty("schemaVersion").GetString());
        Assert.Equal("all", result.Content.GetProperty("requestedCategory").GetString());

        var categories = result.Content.GetProperty("categories").EnumerateArray().ToArray();
        Assert.Equal(["drinks", "prepared_food", "wood", "stone"], categories.Select(category => category.GetProperty("category").GetString()!).ToArray());
        Assert.Equal([60, 32, 48, 128], categories.Select(category => category.GetProperty("exactCount").GetInt32()).ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_ListDwarves_AuthorizesInspectDwarfWithinSameTurn()
    {
        var adapter = new CountingDwarfFortressAdapter();
        var registry = CreateRegistry(adapter: adapter);
        var session = CreateSessionContext();
        session.TurnState.BeginTurn();

        var listResult = await registry.ExecuteAsync(
            CreateInvocation(registry, FakePerceptionToolService.ListDwarvesToolName, new { }, session),
            CancellationToken.None);

        Assert.Equal(1, adapter.ListCallCount);
        Assert.Equal(["4101", "4102", "4103"], listResult.Content.GetProperty("dwarves").EnumerateArray().Select(dwarf => dwarf.GetProperty("dwarfId").GetString()!).ToArray());

        var listedDwarf = listResult.Content.GetProperty("dwarves").EnumerateArray().First();
        Assert.Equal(["dwarfId", "displayName", "professionName"], listedDwarf.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.False(listedDwarf.TryGetProperty("currentJobType", out _));
        Assert.False(listedDwarf.TryGetProperty("stressCategory", out _));
        Assert.False(listedDwarf.TryGetProperty("stressCategoryScale", out _));

        var inspectResult = await registry.ExecuteAsync(
            CreateInvocation(registry, FakePerceptionToolService.InspectDwarfToolName, new { dwarfId = "4102" }, session),
            CancellationToken.None);

        Assert.Equal(1, adapter.SnapshotCallCount);
        Assert.Equal("fortress-souls.inspect-dwarf-result.v0.2", inspectResult.Content.GetProperty("schemaVersion").GetString());
        Assert.Equal("4102", inspectResult.Content.GetProperty("identity").GetProperty("dwarfId").GetString());
        Assert.Equal("Nil Stonereed", inspectResult.Content.GetProperty("identity").GetProperty("readableName").GetString());
        Assert.Equal("HarvestPlants", inspectResult.Content.GetProperty("work").GetProperty("currentJobType").GetString());
        Assert.Equal("BeWithFamily", inspectResult.Content.GetProperty("strongNeeds").EnumerateArray().Single().GetProperty("token").GetString());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ExecuteAsync_RejectsInspectDwarfSnapshotIdentityMismatchAfterSameTurnAuthorization(
        bool mismatchRequestedDwarfId,
        bool mismatchIdentityId)
    {
        var authorizedDwarfId = DwarfId.Parse("4102");
        var mismatchedDwarfId = DwarfId.Parse("4103");
        var adapter = new CountingDwarfFortressAdapter(
            snapshotOverride: (_, snapshot) => snapshot with
            {
                RequestedDwarfId = mismatchRequestedDwarfId ? mismatchedDwarfId : snapshot.RequestedDwarfId,
                Identity = mismatchIdentityId ? snapshot.Identity with { Id = mismatchedDwarfId } : snapshot.Identity
            });
        var registry = CreateRegistry(adapter: adapter);
        var session = CreateSessionContext();
        session.TurnState.BeginTurn();

        await registry.ExecuteAsync(
            CreateInvocation(registry, FakePerceptionToolService.ListDwarvesToolName, new { }, session),
            CancellationToken.None);

        Assert.True(session.TurnState.IsInspectable(authorizedDwarfId));

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            registry.ExecuteAsync(
                CreateInvocation(registry, FakePerceptionToolService.InspectDwarfToolName, new { dwarfId = authorizedDwarfId.ToString() }, session),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidData, exception.ErrorCode);
        Assert.Equal(1, adapter.ListCallCount);
        Assert.Equal(1, adapter.SnapshotCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInspectDwarfWithoutSameTurnAuthorizationBeforeSnapshotQuery()
    {
        var adapter = new CountingDwarfFortressAdapter();
        var registry = CreateRegistry(adapter: adapter);

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            registry.ExecuteAsync(
                CreateInvocation(registry, FakePerceptionToolService.InspectDwarfToolName, new { dwarfId = "4102" }),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidArguments, exception.ErrorCode);
        Assert.Equal(0, adapter.ListCallCount);
        Assert.Equal(0, adapter.SnapshotCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsUnknownToolAtClosedRegistryBoundary()
    {
        var registry = CreateRegistry();

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            registry.ExecuteAsync(
                new AgentToolInvocation(
                    new AgentToolDefinition("probe_unknown", "unknown tool"),
                    CreateSessionContext(),
                    JsonSerializer.SerializeToElement(new { })),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidData, exception.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvalidToolArgumentsBeforeExecution()
    {
        var adapter = new CountingDwarfFortressAdapter();
        var registry = CreateRegistry(adapter: adapter);

        var lookAroundException = await Assert.ThrowsAsync<AgentTurnException>(() =>
            registry.ExecuteAsync(
                CreateInvocation(registry, FakePerceptionToolService.LookAroundToolName, new { radius = 99 }),
                CancellationToken.None));

        var stocksException = await Assert.ThrowsAsync<AgentTurnException>(() =>
            registry.ExecuteAsync(
                CreateInvocation(registry, FakePerceptionToolService.InspectStocksToolName, new { category = "ore" }),
                CancellationToken.None));

        var inspectException = await Assert.ThrowsAsync<AgentTurnException>(() =>
            registry.ExecuteAsync(
                CreateInvocation(registry, FakePerceptionToolService.InspectDwarfToolName, new { dwarfId = "not-a-dwarf" }),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidArguments, lookAroundException.ErrorCode);
        Assert.Equal(AgentTurnErrorCode.InvalidArguments, stocksException.ErrorCode);
        Assert.Equal(AgentTurnErrorCode.InvalidArguments, inspectException.ErrorCode);
        Assert.Equal(0, adapter.ListCallCount);
        Assert.Equal(0, adapter.SnapshotCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsLookAroundUnknownPropertyWhenRadiusIsOmitted()
    {
        var registry = CreateRegistry();

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            registry.ExecuteAsync(
                CreateInvocation(registry, FakePerceptionToolService.LookAroundToolName, new { unexpected = 1 }),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidArguments, exception.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsHiddenSpatialLeakageAsInvalidData()
    {
        var invalidLookAround = FakePerceptionFixtureSet.Default.LookAround with
        {
            Cells =
            [
                new LookAroundCell(-1, -1, "hidden", TerrainClass: "wall")
            ]
        };

        var registry = CreateRegistry(
            fixtures: FakePerceptionFixtureSet.Default with
            {
                LookAround = invalidLookAround
            });

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            registry.ExecuteAsync(
                CreateInvocation(registry, FakePerceptionToolService.LookAroundToolName, new { radius = 1 }),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidData, exception.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsLegendEntriesNotBackedByVisibleCellsAsInvalidData()
    {
        var invalidLookAround = FakePerceptionFixtureSet.Default.LookAround with
        {
            Legend = ["building", "floor", "ramp", "wall", "hidden_magma"]
        };

        var registry = CreateRegistry(
            fixtures: FakePerceptionFixtureSet.Default with
            {
                LookAround = invalidLookAround
            });

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            registry.ExecuteAsync(
                CreateInvocation(registry, FakePerceptionToolService.LookAroundToolName, new { radius = 1 }),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidData, exception.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvalidStockFixtureAsInvalidData()
    {
        var invalidStocks = FakePerceptionFixtureSet.Default.Stocks with
        {
            Categories =
            [
                new StockCategory("drinks", 60),
                new StockCategory("prepared_food", -1),
                new StockCategory("wood", 48),
                new StockCategory("stone", 128)
            ]
        };

        var registry = CreateRegistry(
            fixtures: FakePerceptionFixtureSet.Default with
            {
                Stocks = invalidStocks
            });

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            registry.ExecuteAsync(
                CreateInvocation(registry, FakePerceptionToolService.InspectStocksToolName, new { category = "all" }),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidData, exception.ErrorCode);
    }

    private static ClosedAgentToolRegistry CreateRegistry(
        CountingDwarfFortressAdapter? adapter = null,
        FakePerceptionFixtureSet? fixtures = null)
    {
        var dwarfAdapter = adapter ?? new CountingDwarfFortressAdapter();
        var queryService = new DwarfQueryService(dwarfAdapter, new DwarfAdapterDescriptor("Fake"));
        var service = new FakePerceptionToolService(queryService, fixtures ?? FakePerceptionFixtureSet.Default);

        return new ClosedAgentToolRegistry(service.CreateRegistrations());
    }

    private static AgentToolInvocation CreateInvocation(
        ClosedAgentToolRegistry registry,
        string toolName,
        object arguments,
        AgentSessionContext? session = null)
    {
        Assert.True(registry.TryGetDefinition(toolName, out var definition));

        return new AgentToolInvocation(
            definition!,
            session ?? CreateSessionContext(),
            JsonSerializer.SerializeToElement(arguments));
    }

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
                    ReadableName: "Iden Torrentshade",
                    ProfessionName: "Miner",
                    ProfessionToken: "MINER",
                    CreatureId: "DWARF",
                    CasteId: "MALE"),
                Work: new DwarfWork(CurrentJobType: "DigChannel"),
                Stress: new DwarfStress(
                    Raw: 12000,
                    Longterm: 9000,
                    Category: 1,
                    CategoryScale: "Low"),
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

}

sealed class CountingDwarfFortressAdapter : IDwarfFortressAdapter
{
    private readonly FakeDwarfFortressAdapter _inner = new();
    private readonly Func<DwarfId, DwarfSnapshot, DwarfSnapshot>? _snapshotOverride;

    public CountingDwarfFortressAdapter(Func<DwarfId, DwarfSnapshot, DwarfSnapshot>? snapshotOverride = null)
    {
        _snapshotOverride = snapshotOverride;
    }

    public int ListCallCount { get; private set; }

    public int SnapshotCallCount { get; private set; }

    public async Task<DwarfSnapshot> GetDwarfSnapshotAsync(DwarfId dwarfId, CancellationToken cancellationToken)
    {
        SnapshotCallCount++;
        var snapshot = await _inner.GetDwarfSnapshotAsync(dwarfId, cancellationToken);
        return _snapshotOverride is null ? snapshot : _snapshotOverride(dwarfId, snapshot);
    }

    public async Task<DwarfListResult> ListDwarvesAsync(CancellationToken cancellationToken)
    {
        ListCallCount++;
        return await _inner.ListDwarvesAsync(cancellationToken);
    }
}

public sealed class PerceptionToolLoopTests
{
    [Fact]
    public async Task RunTurnAsync_RejectsStaleInspectableDwarfAuthorizationFromPreviousTurn()
    {
        var adapter = new CountingDwarfFortressAdapter();
        var registry = CreateRegistry(adapter);
        var session = CreateSessionContext();
        session.TurnState.BeginTurn();
        session.TurnState.SetInspectableDwarves([DwarfId.Parse("4102")]);
        session.TurnState.EndTurn();

        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-1", FakePerceptionToolService.InspectDwarfToolName, new Dictionary<string, object?> { ["dwarfId"] = "4102" })])));

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            CreateAgent(fakeClient, registry).RunTurnAsync(
                CreateRequest(session: session),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidArguments, exception.ErrorCode);
        Assert.Equal(1, fakeClient.RequestCount);
        Assert.Equal(0, adapter.SnapshotCallCount);
    }

    [Fact]
    public async Task RunTurnAsync_RejectsToolNotEnabledForCurrentRequestBeforeExecution()
    {
        var adapter = new CountingDwarfFortressAdapter();
        var registry = CreateRegistry(adapter);

        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-1", FakePerceptionToolService.InspectDwarfToolName, new Dictionary<string, object?> { ["dwarfId"] = "4102" })])));

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            CreateAgent(fakeClient, registry).RunTurnAsync(
                CreateRequest() with
                {
                    EnabledToolNames = [FakePerceptionToolService.LookAroundToolName]
                },
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.InvalidData, exception.ErrorCode);
        Assert.Equal(1, fakeClient.RequestCount);
        Assert.Equal(0, adapter.SnapshotCallCount);
    }

    [Fact]
    public async Task RunTurnAsync_SortsEnabledDwarfInspectionToolsBeforeSendingToChatClient()
    {
        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "Nil sounds diligent.")));

        var result = await CreateAgent(fakeClient, CreateRegistry()).RunTurnAsync(
            CreateRequest(userMessage: "Tell me about another dwarf in the fortress.") with
            {
                EnabledToolNames =
                [
                    FakePerceptionToolService.ListDwarvesToolName,
                    FakePerceptionToolService.InspectDwarfToolName
                ]
            },
            CancellationToken.None);

        Assert.Equal("Nil sounds diligent.", result.AssistantMessage);

        var options = Assert.Single(fakeClient.RequestOptions);
        Assert.NotNull(options);
        Assert.NotNull(options!.Tools);

        var orderedToolNames = options.Tools
            .OfType<AIFunctionDeclaration>()
            .Select(tool => tool.Name)
            .ToArray();

        Assert.Equal(
            [FakePerceptionToolService.InspectDwarfToolName, FakePerceptionToolService.ListDwarvesToolName],
            orderedToolNames);
    }

    [Fact]
    public async Task RunTurnAsync_MapsOversizedPerceptionResultToResultTooLarge()
    {
        var registry = CreateRegistry();
        var measuredInvocation = CreateInvocation(registry, FakePerceptionToolService.LookAroundToolName, new { radius = 1 });
        var measuredResult = await registry.ExecuteAsync(measuredInvocation, CancellationToken.None);
        var measuredBytes = JsonSerializer.SerializeToUtf8Bytes(measuredResult.Content).Length;

        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-1", FakePerceptionToolService.LookAroundToolName, new Dictionary<string, object?> { ["radius"] = 1 })])));

        var exception = await Assert.ThrowsAsync<AgentTurnException>(() =>
            CreateAgent(fakeClient, registry).RunTurnAsync(
                CreateRequest(maximumToolResultBytes: measuredBytes - 1, maximumTotalToolResultBytes: measuredBytes * 2),
                CancellationToken.None));

        Assert.Equal(AgentTurnErrorCode.ResultTooLarge, exception.ErrorCode);
        Assert.Equal(1, fakeClient.RequestCount);
    }

    [Fact]
    public async Task RunTurnAsync_MapsCumulativePerceptionResultOverflowToBudgetExhausted()
    {
        var registry = CreateRegistry();
        var measuredInvocation = CreateInvocation(registry, FakePerceptionToolService.LookAroundToolName, new { radius = 1 });
        var measuredResult = await registry.ExecuteAsync(measuredInvocation, CancellationToken.None);
        var measuredBytes = JsonSerializer.SerializeToUtf8Bytes(measuredResult.Content).Length;

        var fakeClient = new SequenceChatClient(
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-1", FakePerceptionToolService.LookAroundToolName, new Dictionary<string, object?> { ["radius"] = 1 })])),
            new ChatResponse(new ChatMessage(
                AiChatRole.Assistant,
                [new FunctionCallContent("call-2", FakePerceptionToolService.LookAroundToolName, new Dictionary<string, object?> { ["radius"] = 1 })])),
            new ChatResponse(new ChatMessage(AiChatRole.Assistant, "I cannot inspect further.")));

        var result = await CreateAgent(fakeClient, registry).RunTurnAsync(
            CreateRequest(
                maximumRounds: 2,
                maximumToolCalls: 2,
                maximumToolResultBytes: measuredBytes,
                maximumTotalToolResultBytes: measuredBytes + 1),
            CancellationToken.None);

        Assert.Equal("I cannot inspect further.", result.AssistantMessage);
        Assert.Collection(
            result.ToolReceipts,
            receipt =>
            {
                Assert.Equal(FakePerceptionToolService.LookAroundToolName, receipt.Tool);
                Assert.Equal(AgentToolOutcomes.Success, receipt.Outcome);
            },
            receipt =>
            {
                Assert.Equal(FakePerceptionToolService.LookAroundToolName, receipt.Tool);
                Assert.Equal(AgentToolOutcomes.BudgetExhausted, receipt.Outcome);
            });
        Assert.Equal(3, fakeClient.RequestCount);
    }

    private static ClosedAgentToolRegistry CreateRegistry(IDwarfFortressAdapter? adapter = null) =>
        new(new FakePerceptionToolService(
            new DwarfQueryService(adapter ?? new FakeDwarfFortressAdapter(), new DwarfAdapterDescriptor("Fake")),
            FakePerceptionFixtureSet.Default).CreateRegistrations());

    private static AgentToolInvocation CreateInvocation(ClosedAgentToolRegistry registry, string toolName, object arguments)
    {
        Assert.True(registry.TryGetDefinition(toolName, out var definition));

        return new AgentToolInvocation(
            definition!,
            CreateSessionContext(),
            JsonSerializer.SerializeToElement(arguments));
    }

    private static MicrosoftExtensionsAiDwarfAgent CreateAgent(IChatClient chatClient, ClosedAgentToolRegistry registry) =>
        new(chatClient, registry, new LlmProviderOptions
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
        AgentSessionContext? session = null,
        int maximumRounds = 2,
        int maximumToolCalls = 1,
        int maximumToolResultBytes = 512,
        int maximumTotalToolResultBytes = 1_024) =>
        new(
            session ?? CreateSessionContext(),
            userMessage,
            new AgentExecutionPolicy(
                MaximumRounds: maximumRounds,
                MaximumToolCalls: maximumToolCalls,
                MaximumToolResultBytes: maximumToolResultBytes,
                MaximumTotalToolResultBytes: maximumTotalToolResultBytes,
                TurnTimeout: TimeSpan.FromSeconds(5),
                ToolTimeout: TimeSpan.FromSeconds(1)));

    private static AgentSessionContext CreateSessionContext()
    {
        var dwarfId = DwarfId.Parse("4101");

        return new AgentSessionContext(
            SessionId: "session-4101",
            DwarfId: dwarfId,
            Snapshot: new DwarfSnapshot(
                SchemaVersion: DwarfSchemaVersions.Snapshot,
                Source: new DwarfSnapshotSourceMetadata(true, true, true, true),
                RequestedDwarfId: dwarfId,
                Identity: new DwarfIdentity(dwarfId, "Iden Torrentshade", "Miner", "MINER", "DWARF", "MALE"),
                Work: new DwarfWork("DigChannel"),
                Stress: new DwarfStress(12000, 9000, 1, "Low"),
                Skills: new DwarfSkillCollection(0, []),
                Personality: new DwarfPersonality(true, new DwarfTraitCollection(0, []), new DwarfValueCollection(0, []), new DwarfNeedCollection(0, []), new DwarfMannerismCollection(0, [])),
                PromptCandidates: new DwarfPromptCandidates([], [], [], [], [])),
            Conversation: []);
    }

    private sealed class SequenceChatClient(params ChatResponse[] responses) : IChatClient
    {
        private readonly Queue<ChatResponse> _responses = new(responses);

        public int RequestCount { get; private set; }

        public List<ChatOptions?> RequestOptions { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
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
}
