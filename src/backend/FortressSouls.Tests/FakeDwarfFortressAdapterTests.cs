namespace FortressSouls.Tests;

using System.Text.Json;
using FortressSouls.Application;
using FortressSouls.Domain;
using FortressSouls.DwarfFortress;

public sealed class FakeDwarfFortressAdapterTests
{
    [Fact]
    public async Task ListDwarvesAsync_ReturnsStableSyntheticRoster()
    {
        var adapter = new FakeDwarfFortressAdapter();

        var first = await adapter.ListDwarvesAsync(CancellationToken.None);
        var second = await adapter.ListDwarvesAsync(CancellationToken.None);

        Assert.Equal(DwarfSchemaVersions.List, first.SchemaVersion);
        Assert.Equal(
            JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(second));
        Assert.Equal(3, first.Items.Count);
        Assert.Equal(
            ["Miner", "Farmer", "Bookkeeper"],
            first.Items.Select(item => item.ProfessionName).ToArray());
        Assert.Equal(3, first.Items.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task GetDwarfSnapshotAsync_ReturnsSnapshotForListedDwarf()
    {
        var adapter = new FakeDwarfFortressAdapter();
        var dwarfId = DwarfId.Parse("4103");

        var snapshot = await adapter.GetDwarfSnapshotAsync(dwarfId, CancellationToken.None);

        Assert.Equal(DwarfSchemaVersions.Snapshot, snapshot.SchemaVersion);
        Assert.Equal(dwarfId, snapshot.RequestedDwarfId);
        Assert.Equal(dwarfId, snapshot.Identity.Id);
        Assert.Equal("Domas Inkgranite", snapshot.Identity.ReadableName);
        Assert.Equal("UpdateStockpileRecords", snapshot.Work.CurrentJobType);
        Assert.True(snapshot.Source.SoulPresent);
        Assert.NotEmpty(snapshot.Skills.Items);
        Assert.NotEmpty(snapshot.Personality.Traits.Items);
        Assert.NotEmpty(snapshot.PromptCandidates.TopSkills);
    }

    [Fact]
    public async Task GetDwarfSnapshotAsync_ThrowsForUnknownDwarfId()
    {
        var adapter = new FakeDwarfFortressAdapter();

        await Assert.ThrowsAsync<DwarfNotFoundException>(() =>
            adapter.GetDwarfSnapshotAsync(DwarfId.Parse("9999"), CancellationToken.None));
    }

    [Fact]
    public async Task ListDwarvesAsync_HonorsCancellation()
    {
        var adapter = new FakeDwarfFortressAdapter();
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.ListDwarvesAsync(cancellationTokenSource.Token));
    }
}
