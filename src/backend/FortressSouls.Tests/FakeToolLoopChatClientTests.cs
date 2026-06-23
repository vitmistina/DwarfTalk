namespace FortressSouls.Tests;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FortressSouls.Application;
using FortressSouls.Llm;
using Microsoft.Extensions.AI;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

public sealed class FakeToolLoopChatClientTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetResponseAsync_BuildsGroundedReplyFromLookAroundFieldsOnly()
    {
        var client = new FakeToolLoopChatClient();
        var lookAround = new LookAroundToolResult(
            SchemaVersion: "fortress-souls.look-around-result.v0.2",
            GameTime: "125-03-12T08:15",
            Bounds: new LookAroundBounds(1, 3, 3),
            Cells:
            [
                new LookAroundCell(-1, -1, "hidden"),
                new LookAroundCell(0, -1, "visible", TerrainClass: "wall", Walkable: false),
                new LookAroundCell(1, -1, "visible", TerrainClass: "floor", Walkable: true),
                new LookAroundCell(-1, 0, "visible", TerrainClass: "ramp", Walkable: true),
                new LookAroundCell(0, 0, "visible", TerrainClass: "floor", Walkable: true, UnitCount: 1),
                new LookAroundCell(1, 0, "visible", TerrainClass: "floor", Walkable: true),
                new LookAroundCell(-1, 1, "visible", TerrainClass: "ramp", Walkable: true),
                new LookAroundCell(0, 1, "visible", TerrainClass: "building", Walkable: false, FeatureClass: "building"),
                new LookAroundCell(1, 1, "visible", TerrainClass: "building", Walkable: false, FeatureClass: "building", UnitCount: 2)
            ],
            Legend: ["building", "floor", "ramp", "wall"],
            Warnings: []);

        var response = await client.GetResponseAsync(
            [
                new ChatMessage(AiChatRole.User, "What do you see around you right now?"),
                new ChatMessage(
                    AiChatRole.Tool,
                    [new FunctionResultContent("call-1", SerializeToolResult(lookAround))])
            ],
            new ChatOptions
            {
                Tools = [CreateTool(FakePerceptionToolService.LookAroundToolName)]
            },
            CancellationToken.None);

        var message = Assert.Single(response.Messages);

        Assert.Equal(
            "I can see building, floor, ramp, and wall nearby, and I count 3 visible units.",
            message.Text);
        Assert.DoesNotContain("dwarf-shaped", message.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetResponseAsync_UsesListThenInspectForAnotherDwarfQuestion()
    {
        var client = new FakeToolLoopChatClient();
        var options = new ChatOptions
        {
            Tools =
            [
                CreateTool(FakePerceptionToolService.ListDwarvesToolName),
                CreateTool(FakePerceptionToolService.InspectDwarfToolName)
            ]
        };

        var initialResponse = await client.GetResponseAsync(
            [new ChatMessage(AiChatRole.User, "Tell me about another dwarf in the fortress.")],
            options,
            CancellationToken.None);

        var initialMessage = Assert.Single(initialResponse.Messages);
        var listCall = Assert.Single(initialMessage.Contents.OfType<FunctionCallContent>());
        Assert.Equal(FakePerceptionToolService.ListDwarvesToolName, listCall.Name);
        Assert.Empty(listCall.Arguments!);

        var listResult = new ListDwarvesToolResult(
            SchemaVersion: "fortress-souls.list-dwarves-result.v0.2",
            Dwarves:
            [
                new ListedDwarf("4101", "Iden Torrentshade", "Miner"),
                new ListedDwarf("4102", "Nil Stonereed", "Grower"),
                new ListedDwarf("4103", "Mistem Claspedcobalt", "Mason")
            ],
            Warnings: Array.Empty<string>());

        var inspectResponse = await client.GetResponseAsync(
            [
                new ChatMessage(AiChatRole.User, "Tell me about another dwarf in the fortress."),
                new ChatMessage(
                    AiChatRole.Tool,
                    [new FunctionResultContent("call-1", SerializeToolResult(listResult))])
            ],
            options,
            CancellationToken.None);

        var inspectMessage = Assert.Single(inspectResponse.Messages);
        var inspectCall = Assert.Single(inspectMessage.Contents.OfType<FunctionCallContent>());
        Assert.Equal(FakePerceptionToolService.InspectDwarfToolName, inspectCall.Name);
        Assert.NotNull(inspectCall.Arguments);
        Assert.Equal("4102", Assert.IsType<string>(inspectCall.Arguments!["dwarfId"]));

        var inspectResult = new InspectDwarfToolResult(
            SchemaVersion: "fortress-souls.inspect-dwarf-result.v0.2",
            Identity: new InspectDwarfIdentity("4102", "Nil Stonereed", "Grower"),
            Work: new InspectDwarfWork("HarvestPlants"),
            Stress: new InspectDwarfStress(2, "Elevated"),
            TopSkills: [new InspectDwarfSkill("grower", 9)],
            ExtremeTraits: [],
            StrongValues: [],
            StrongNeeds: [new InspectDwarfNeed("BeWithFamily", 4, true, false)],
            Mannerisms: [],
            Warnings: Array.Empty<string>());

        var finalResponse = await client.GetResponseAsync(
            [
                new ChatMessage(AiChatRole.User, "Tell me about another dwarf in the fortress."),
                new ChatMessage(
                    AiChatRole.Tool,
                    [new FunctionResultContent("call-1", SerializeToolResult(listResult))]),
                new ChatMessage(
                    AiChatRole.Tool,
                    [new FunctionResultContent("call-2", SerializeToolResult(inspectResult))])
            ],
            options,
            CancellationToken.None);

        var finalMessage = Assert.Single(finalResponse.Messages);
        Assert.Contains("Nil Stonereed", finalMessage.Text, StringComparison.Ordinal);
        Assert.Contains("HarvestPlants", finalMessage.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetResponseAsync_UsesInspectStocksForStockQuestionAndBuildsExactReply()
    {
        var client = new FakeToolLoopChatClient();
        var options = new ChatOptions
        {
            Tools =
            [
                CreateTool(FakePerceptionToolService.InspectStocksToolName)
            ]
        };

        var initialResponse = await client.GetResponseAsync(
            [new ChatMessage(AiChatRole.User, "How much wood do we have in stock?")],
            options,
            CancellationToken.None);

        var initialMessage = Assert.Single(initialResponse.Messages);
        var stockCall = Assert.Single(initialMessage.Contents.OfType<FunctionCallContent>());
        Assert.Equal(FakePerceptionToolService.InspectStocksToolName, stockCall.Name);
        Assert.NotNull(stockCall.Arguments);
        Assert.Equal("wood", Assert.IsType<string>(stockCall.Arguments!["category"]));

        var stockResult = new InspectStocksToolResult(
            SchemaVersion: "fortress-souls.inspect-stocks-result.v0.2",
            GameTime: "125-03-12T08:15",
            RequestedCategory: "wood",
            Categories:
            [
                new StockCategory("wood", 48)
            ],
            Warnings: Array.Empty<string>());

        var finalResponse = await client.GetResponseAsync(
            [
                new ChatMessage(AiChatRole.User, "How much wood do we have in stock?"),
                new ChatMessage(
                    AiChatRole.Tool,
                    [new FunctionResultContent("call-1", SerializeToolResult(stockResult))])
            ],
            options,
            CancellationToken.None);

        var finalMessage = Assert.Single(finalResponse.Messages);
        Assert.Equal("I can account for 48 wood.", finalMessage.Text);
        Assert.DoesNotContain("~48", finalMessage.Text, StringComparison.Ordinal);
    }

    private static AITool CreateTool(string name) =>
        AIFunctionFactory.Create(
            (CancellationToken cancellationToken) => Task.FromResult<object?>(null),
            name,
            "test tool",
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            });

    private static JsonElement SerializeToolResult<T>(T value) =>
        JsonSerializer.SerializeToElement(value, typeof(T), SerializerOptions);
}
