namespace FortressSouls.Tests;

using FortressSouls.Application;
using FortressSouls.DwarfFortress;

public sealed class StockInspectionServiceTests
{
    [Fact]
    public async Task FixtureStockInspectionService_ReturnsAllowlistedCategoriesInStableOrder()
    {
        var service = new FixtureStockInspectionService(FakePerceptionFixtureSet.Default.Stocks);

        var result = await service.InspectStocksAsync("all", CancellationToken.None);

        Assert.Equal("fortress-souls.inspect-stocks-result.v0.2", result.SchemaVersion);
        Assert.Equal("all", result.RequestedCategory);
        Assert.Equal(["drinks", "prepared_food", "wood", "stone"], result.Categories.Select(category => category.Category).ToArray());
        Assert.Equal([60, 32, 48, 128], result.Categories.Select(category => category.ExactCount).ToArray());
    }

    [Fact]
    public async Task DfHackStockInspectionService_MapsRetainedLiveResearchSampleToExactProductCategories()
    {
        var service = new DfHackStockInspectionService(
            new StubRunner(
                DfHackProcessCommandResult.Success(
                    DfHackCommand.GetStockSummary,
                    await File.ReadAllTextAsync(GetRetainedLiveStockSamplePath()),
                    string.Empty,
                    0,
                    TimeSpan.FromMilliseconds(5))),
            new DfHackProcessAdapterOptions
            {
                Enabled = true,
                RunPath = "C:\\dfhack\\hack\\dfhack-run.exe",
                WorkingDirectory = "C:\\dfhack\\hack",
                Host = "127.0.0.1"
            }.Validate());

        var result = await service.InspectStocksAsync("all", CancellationToken.None);

        Assert.Equal("fortress-souls.inspect-stocks-result.v0.2", result.SchemaVersion);
        Assert.Equal("all", result.RequestedCategory);
        Assert.NotNull(result.GameTime);
        Assert.Equal(["drinks", "prepared_food", "wood", "stone"], result.Categories.Select(category => category.Category).ToArray());
        Assert.Equal([60, 0, 3, 0], result.Categories.Select(category => category.ExactCount).ToArray());
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("approximate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DfHackStockInspectionService_MapsRetainedProductShapedLiveSample()
    {
        var service = new DfHackStockInspectionService(
            new StubRunner(
                DfHackProcessCommandResult.Success(
                    DfHackCommand.GetStockSummary,
                    await File.ReadAllTextAsync(GetRetainedProductStockSamplePath()),
                    string.Empty,
                    0,
                    TimeSpan.FromMilliseconds(5))),
            new DfHackProcessAdapterOptions
            {
                Enabled = true,
                RunPath = "C:\\dfhack\\hack\\dfhack-run.exe",
                WorkingDirectory = "C:\\dfhack\\hack",
                Host = "127.0.0.1"
            }.Validate());

        var result = await service.InspectStocksAsync("all", CancellationToken.None);

        Assert.Equal("fortress-souls.inspect-stocks-result.v0.2", result.SchemaVersion);
        Assert.Equal("100:16801", result.GameTime);
        Assert.Equal(["drinks", "prepared_food", "wood", "stone"], result.Categories.Select(category => category.Category).ToArray());
        Assert.Equal([60, 0, 3, 0], result.Categories.Select(category => category.ExactCount).ToArray());
    }

    [Fact]
    public async Task DfHackStockInspectionService_MapsProcessFailureToStableDataException()
    {
        var service = new DfHackStockInspectionService(
            new StubRunner(
                DfHackProcessCommandResult.Failure(
                    DfHackCommand.GetStockSummary,
                    DfHackProcessFailureCategory.Unavailable,
                    stdout: null,
                    stderr: null,
                    exitCode: null,
                    duration: TimeSpan.FromMilliseconds(10))),
            new DfHackProcessAdapterOptions
            {
                Enabled = true,
                RunPath = "C:\\dfhack\\hack\\dfhack-run.exe",
                WorkingDirectory = "C:\\dfhack\\hack",
                Host = "127.0.0.1"
            }.Validate());

        var exception = await Assert.ThrowsAsync<DwarfFortressDataException>(() =>
            service.InspectStocksAsync("all", CancellationToken.None));

        Assert.Equal(DwarfFortressDataErrorCode.DfHackUnavailable, exception.ErrorCode);
    }

    private static string GetRetainedLiveStockSamplePath([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Unable to determine the test source directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", "..", ".."));
        return Path.Combine(repoRoot, "dfhack", "samples", "research", "stock-summary.live-2026-06-21.json");
    }

    private static string GetRetainedProductStockSamplePath([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Unable to determine the test source directory.");
        var repoRoot = Path.GetFullPath(Path.Combine(testDirectory, "..", "..", ".."));
        return Path.Combine(repoRoot, "dfhack", "samples", "perception", "stock-summary.live-2026-06-23.json");
    }

    private sealed class StubRunner(DfHackProcessCommandResult result) : IDfHackProcessRunner
    {
        public Task<DfHackProcessCommandResult> RunCommandAsync(
            DfHackCommand command,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
