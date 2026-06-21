namespace FortressSouls.Tests;

using FortressSouls.Application;
using FortressSouls.Domain;
using FortressSouls.DwarfFortress;
using FortressSouls.Observability;
using System.Diagnostics;

[Collection("DfHackProcessSerial")]
public sealed class DfHackAdapterTests
{
    [Fact]
    public async Task DfHackAdapter_MapsListAndSnapshotThroughJsonValidationSeam()
    {
        var statusTracker = new DfHackAdapterStatusTracker(enabled: true);
        var adapter = new DfHackDwarfFortressAdapter(
            CreateRunner("success"),
            CreateOptions(),
            statusTracker);

        var list = await adapter.ListDwarvesAsync(CancellationToken.None);
        var snapshot = await adapter.GetDwarfSnapshotAsync(DwarfId.Parse("6597"), CancellationToken.None);

        var listedDwarf = Assert.Single(list.Items);
        Assert.Equal("6597", listedDwarf.Id.ToString());
        Assert.Equal("6597", snapshot.Identity.Id.ToString());
        Assert.Equal("success", statusTracker.GetCurrentStatus().LastOutcome);
    }

    [Fact]
    public async Task DfHackAdapter_PreservesOemEncodedDwarfNames()
    {
        var statusTracker = new DfHackAdapterStatusTracker(enabled: true);
        var adapter = new DfHackDwarfFortressAdapter(
            CreateRunner("oem_name"),
            CreateOptions(),
            statusTracker);

        var list = await adapter.ListDwarvesAsync(CancellationToken.None);
        var snapshot = await adapter.GetDwarfSnapshotAsync(DwarfId.Parse("6601"), CancellationToken.None);

        Assert.Contains(list.Items, dwarf => dwarf.DisplayName.Contains("Kadôl Thocitoddom", StringComparison.Ordinal));
        Assert.Contains(list.Items, dwarf => dwarf.DisplayName.Contains("îton Oltarkurik", StringComparison.Ordinal));
        Assert.Equal("Kadôl Thocitoddom \"Spikescloisters\", Fisherdwarf", snapshot.Identity.ReadableName);
    }

    [Fact]
    public async Task DfHackAdapter_ClassifiesInvalidJsonSchemaAndMappingFailures()
    {
        var statusTracker = new DfHackAdapterStatusTracker(enabled: true);
        var invalidJsonAdapter = new DfHackDwarfFortressAdapter(CreateRunner("invalid_json"), CreateOptions(), statusTracker);

        var jsonException = await Assert.ThrowsAsync<DwarfFortressDataException>(() =>
            invalidJsonAdapter.ListDwarvesAsync(CancellationToken.None));
        Assert.Equal(DwarfFortressDataErrorCode.MalformedJson, jsonException.ErrorCode);

        var invalidSchemaAdapter = new DfHackDwarfFortressAdapter(
            new StubRunner(
                DfHackProcessCommandResult.Success(
                    DfHackCommand.ListDwarves,
                    """{"schemaVersion":"bad.v0.1","worldLoaded":true,"siteLoaded":true,"mapLoaded":true,"count":0,"items":[]}""",
                    "",
                    0,
                    TimeSpan.FromMilliseconds(5))),
            CreateOptions(),
            statusTracker);

        var schemaException = await Assert.ThrowsAsync<DwarfFortressDataException>(() =>
            invalidSchemaAdapter.ListDwarvesAsync(CancellationToken.None));
        Assert.Equal(DwarfFortressDataErrorCode.UnsupportedSchema, schemaException.ErrorCode);

        var mappingAdapter = new DfHackDwarfFortressAdapter(
            new StubRunner(
                DfHackProcessCommandResult.Success(
                    DfHackCommand.GetDwarfSnapshot,
                    """{"schemaVersion":"fortress-souls-dwarf-snapshot.v0.1","requestedUnitId":"6597","worldLoaded":true,"siteLoaded":true,"mapLoaded":true,"soulPresent":true}""",
                    "",
                    0,
                    TimeSpan.FromMilliseconds(5))),
            CreateOptions(),
            statusTracker);

        var mappingException = await Assert.ThrowsAsync<DwarfFortressDataException>(() =>
            mappingAdapter.GetDwarfSnapshotAsync(DwarfId.Parse("6597"), CancellationToken.None));
        Assert.Equal(DwarfFortressDataErrorCode.InvalidData, mappingException.ErrorCode);
    }

    [Fact]
    public async Task DfHackAdapter_MapsProcessFailureCategoriesToStableErrorCodes()
    {
        var statusTracker = new DfHackAdapterStatusTracker(enabled: true);
        var adapter = new DfHackDwarfFortressAdapter(
            new StubRunner(
                DfHackProcessCommandResult.Failure(
                    DfHackCommand.ListDwarves,
                    DfHackProcessFailureCategory.Unavailable,
                    stdout: null,
                    stderr: null,
                    exitCode: null,
                    duration: TimeSpan.FromMilliseconds(10))),
            CreateOptions(),
            statusTracker);

        var exception = await Assert.ThrowsAsync<DwarfFortressDataException>(() =>
            adapter.ListDwarvesAsync(CancellationToken.None));

        Assert.Equal(DwarfFortressDataErrorCode.DfHackUnavailable, exception.ErrorCode);
        Assert.Equal("unavailable", statusTracker.GetCurrentStatus().LastErrorCategory);
    }

    [Fact]
    public async Task DfHackAdapter_AddsContentSafeTelemetryTags()
    {
        var statusTracker = new DfHackAdapterStatusTracker(enabled: true);
        var adapter = new DfHackDwarfFortressAdapter(CreateRunner("success"), CreateOptions(), statusTracker);
        using var activity = new Activity("fortresssouls.dwarves.list").Start();

        await adapter.ListDwarvesAsync(CancellationToken.None);

        Assert.Equal("ListDwarves", activity?.GetTagItem(FortressSoulsTelemetry.DfHackCommandTagName));
        Assert.Equal("success", activity?.GetTagItem(FortressSoulsTelemetry.OperationOutcomeTagName));
        var tagValues = activity?.Tags.Select(tag => tag.Value ?? string.Empty).ToArray() ?? [];
        Assert.DoesNotContain("Dwarf One", tagValues, StringComparer.Ordinal);
        Assert.DoesNotContain("6597", tagValues, StringComparer.Ordinal);
    }

    [Fact]
    public void DfHackAdapterStatusTracker_ReadHasNoSideEffects()
    {
        var tracker = new DfHackAdapterStatusTracker(enabled: true);

        var first = tracker.GetCurrentStatus();
        var second = tracker.GetCurrentStatus();

        Assert.Equal(first, second);
        Assert.Null(first.LastUpdatedAtUtc);
    }

    private static IDfHackProcessRunner CreateRunner(string mode)
    {
        Environment.SetEnvironmentVariable("FORTRESS_SOULS_DFHACK_TEST_MODE", mode);
        var options = CreateOptions();
        var runner = new DfHackProcessRunner(options, new StubPreflight(true));
        return runner;
    }

    private static DfHackProcessAdapterOptions CreateOptions()
    {
        var hostPath = GetHostExecutablePath();
        return new DfHackProcessAdapterOptions
        {
            Enabled = true,
            RunPath = hostPath,
            WorkingDirectory = Path.GetDirectoryName(hostPath) ?? throw new InvalidOperationException("Missing host directory."),
            TimeoutMs = 2000,
            MaxStdoutBytes = 256 * 1024,
            MaxStderrBytes = 16 * 1024
        }.Validate();
    }

    private static string GetHostExecutablePath()
    {
        var assemblyLocation = typeof(FortressSouls.DfHackProcessTestHost.Marker).Assembly.Location;
        var exePath = Path.ChangeExtension(assemblyLocation, ".exe");
        if (exePath is null || !File.Exists(exePath))
        {
            throw new InvalidOperationException("Unable to locate DFHack process test host executable.");
        }

        return exePath;
    }

    private sealed class StubPreflight(bool value) : IDfHackTcpPreflight
    {
        public Task<bool> IsReachableAsync(CancellationToken cancellationToken) => Task.FromResult(value);
    }

    private sealed class StubRunner(DfHackProcessCommandResult result) : IDfHackProcessRunner
    {
        public Task<DfHackProcessCommandResult> RunCommandAsync(
            DfHackCommand command,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
