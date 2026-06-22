namespace FortressSouls.Tests;

using System.Text.Json;
using FortressSouls.Application;
using FortressSouls.Domain;

public sealed class ToolLoopProbeRegistryTests
{
    [Fact]
    public async Task ClosedAgentToolRegistry_ListsDefinitionsInStableNameOrder_AndExecutesByInvocation()
    {
        var first = new AgentToolRegistration(
            new AgentToolDefinition("zzz_probe", "zzz probe"),
            (invocation, cancellationToken) => Task.FromResult(AgentToolResult.Create(invocation.Tool, new { status = "zzz" })));
        var second = new AgentToolRegistration(
            new AgentToolDefinition("aaa_probe", "aaa probe"),
            (invocation, cancellationToken) => Task.FromResult(AgentToolResult.Create(invocation.Tool, new { status = "aaa" })));
        var registry = new ClosedAgentToolRegistry([first, second]);

        var orderedNames = registry.ListDefinitions().Select(tool => tool.Name).ToArray();

        Assert.Equal(["aaa_probe", "zzz_probe"], orderedNames);
        Assert.True(registry.TryGetDefinition("zzz_probe", out var resolved));
        Assert.Equal("zzz_probe", resolved!.Name);
        Assert.False(registry.TryGetDefinition("missing", out var missing));
        Assert.Null(missing);

        var result = await registry.ExecuteAsync(
            new AgentToolInvocation(
                resolved,
                CreateSessionContext(),
                JsonSerializer.SerializeToElement(new { })),
            CancellationToken.None);

        Assert.Equal("zzz_probe", result.Tool.Name);
        Assert.Equal("zzz", result.Content.GetProperty("status").GetString());
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
}