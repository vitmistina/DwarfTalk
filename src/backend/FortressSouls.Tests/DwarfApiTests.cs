namespace FortressSouls.Tests;

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FortressSouls.Application;
using FortressSouls.Domain;
using FortressSouls.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

public sealed class DwarfApiTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ListEndpoint_ReturnsStableContractAndCorrelationHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/dwarves");
        request.Headers.Add(FortressSoulsTelemetry.CorrelationHeaderName, "dwarves-123");

        var response = await _client!.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(FortressSoulsTelemetry.CorrelationHeaderName, out var correlationValues));
        Assert.Equal("dwarves-123", Assert.Single(correlationValues));

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var items = root.GetProperty("items");

        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal("4101", items[0].GetProperty("id").GetString());
        Assert.Equal("Iden Torrentshade", items[0].GetProperty("displayName").GetString());
        Assert.Equal("Bookkeeper", items[2].GetProperty("profession").GetString());

        var source = root.GetProperty("source");
        Assert.Equal("Fake", source.GetProperty("adapter").GetString());
        Assert.Equal(DwarfSchemaVersions.List, source.GetProperty("schemaVersion").GetString());
        Assert.True(source.GetProperty("worldLoaded").GetBoolean());
        Assert.True(source.GetProperty("siteLoaded").GetBoolean());
        Assert.True(source.GetProperty("mapLoaded").GetBoolean());
    }

    [Fact]
    public async Task ListEndpoint_ReturnsEmptyItemsWhenAdapterReturnsNoDwarves()
    {
        using var factory = CreateFactory(new EmptyDwarfFortressAdapter(), "Empty");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/dwarves");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(0, root.GetProperty("items").GetArrayLength());
        Assert.Equal("Empty", root.GetProperty("source").GetProperty("adapter").GetString());
    }

    [Fact]
    public async Task SnapshotEndpoint_ReturnsValidatedSnapshotContract()
    {
        var response = await _client!.GetAsync("/api/dwarves/4103/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(DwarfSchemaVersions.Snapshot, root.GetProperty("schemaVersion").GetString());
        Assert.Equal("4103", root.GetProperty("dwarfId").GetString());
        Assert.Equal("Domas Inkgranite", root.GetProperty("identity").GetProperty("displayName").GetString());
        Assert.Equal("Bookkeeper", root.GetProperty("identity").GetProperty("profession").GetString());
        Assert.Equal("UpdateStockpileRecords", root.GetProperty("work").GetProperty("currentJob").GetString());
        Assert.True(root.GetProperty("skills").EnumerateArray().Any());
        Assert.Equal("Fake", root.GetProperty("source").GetProperty("adapter").GetString());
        Assert.True(root.GetProperty("source").GetProperty("soulPresent").GetBoolean());
    }

    [Fact]
    public async Task SnapshotEndpoint_ReturnsBadRequestForInvalidDwarfId()
    {
        var response = await _client!.GetAsync("/api/dwarves/not-a-dwarf/snapshot");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("invalid_dwarf_id", error.ErrorCode);
        Assert.DoesNotContain("not-a-dwarf", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SnapshotEndpoint_ReturnsNotFoundForUnknownDwarfId()
    {
        var response = await _client!.GetAsync("/api/dwarves/9999/snapshot");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("dwarf_not_found", error.ErrorCode);
        Assert.DoesNotContain("9999", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SnapshotEndpoint_PropagatesCancellation()
    {
        using var factory = CreateFactory(new BlockingDwarfFortressAdapter(), "Blocking");
        using var client = factory.CreateClient();
        using var cancellationTokenSource = new CancellationTokenSource();

        var requestTask = client.GetAsync("/api/dwarves/4101/snapshot", cancellationTokenSource.Token);
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => requestTask);
    }

    [Fact]
    public async Task DwarfEndpoints_EmitExpectedTelemetryWithoutDwarfNames()
    {
        var observedActivities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FortressSoulsTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observedActivities.Add(activity),
        };

        ActivitySource.AddActivityListener(listener);

        var response = await _client!.GetAsync("/api/dwarves/4103/snapshot");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var activity = Assert.Single(observedActivities, item => item.DisplayName == "fortresssouls.dwarves.snapshot");
        Assert.Equal("Fake", activity.GetTagItem(FortressSoulsTelemetry.AdapterTypeTagName));
        Assert.Equal("4103", activity.GetTagItem(FortressSoulsTelemetry.DwarfIdTagName));
        Assert.Equal(DwarfSchemaVersions.Snapshot, activity.GetTagItem(FortressSoulsTelemetry.SnapshotSchemaVersionTagName));
        Assert.Equal("success", activity.GetTagItem("fortresssouls.operation.outcome"));

        var tagValues = activity.Tags.Select(tag => tag.Value ?? string.Empty).ToArray();
        Assert.DoesNotContain("Domas Inkgranite", tagValues, StringComparer.Ordinal);
        Assert.DoesNotContain("Bookkeeper", tagValues, StringComparer.Ordinal);
    }

    private static WebApplicationFactory<Program> CreateFactory(IDwarfFortressAdapter adapter, string adapterType) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IDwarfFortressAdapter>(adapter);
                    services.AddSingleton(new DwarfAdapterDescriptor(adapterType));
                });
            });

    private sealed record ApiErrorResponse(string ErrorCode, string Message);

    private sealed class EmptyDwarfFortressAdapter : IDwarfFortressAdapter
    {
        public Task<DwarfSnapshot> GetDwarfSnapshotAsync(DwarfId dwarfId, CancellationToken cancellationToken) =>
            throw new DwarfNotFoundException(dwarfId);

        public Task<DwarfListResult> ListDwarvesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(
                new DwarfListResult(
                    DwarfSchemaVersions.List,
                    new DwarfListSourceMetadata(true, true, true),
                    []));
    }

    private sealed class BlockingDwarfFortressAdapter : IDwarfFortressAdapter
    {
        public async Task<DwarfSnapshot> GetDwarfSnapshotAsync(DwarfId dwarfId, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }

        public async Task<DwarfListResult> ListDwarvesAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }
    }
}
