namespace FortressSouls.Application;

public sealed class FixtureStockInspectionService(FakeStockFixture fixture) : IStockInspectionService
{
    private readonly FakeStockFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    public Task<InspectStocksToolResult> InspectStocksAsync(string requestedCategory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateFixture(_fixture);

        var normalizedCategory = NormalizeRequestedCategory(requestedCategory);
        var categories = string.Equals(normalizedCategory, StockInspectionCategories.All, StringComparison.Ordinal)
            ? _fixture.Categories
            : _fixture.Categories.Where(category => string.Equals(category.Category, normalizedCategory, StringComparison.Ordinal)).ToArray();

        return Task.FromResult(new InspectStocksToolResult(
            SchemaVersion: _fixture.SchemaVersion,
            GameTime: _fixture.GameTime,
            RequestedCategory: normalizedCategory,
            Categories: categories.Select(category => new StockCategory(category.Category, category.ExactCount)).ToArray(),
            Warnings: _fixture.Warnings));
    }

    public static string NormalizeRequestedCategory(string requestedCategory)
    {
        ArgumentNullException.ThrowIfNull(requestedCategory);

        var normalized = requestedCategory.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > 64)
        {
            throw InvalidArguments();
        }

        if (!string.Equals(normalized, StockInspectionCategories.All, StringComparison.Ordinal)
            && !StockInspectionCategories.StableOrder.Contains(normalized, StringComparer.Ordinal))
        {
            throw InvalidArguments();
        }

        return normalized;
    }

    internal static void ValidateFixture(FakeStockFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        if (string.IsNullOrWhiteSpace(fixture.SchemaVersion)
            || fixture.Categories is null
            || fixture.Warnings is null
            || fixture.Categories.Count != StockInspectionCategories.StableOrder.Count)
        {
            throw InvalidData();
        }

        for (var index = 0; index < StockInspectionCategories.StableOrder.Count; index++)
        {
            var category = fixture.Categories[index];
            if (category is null
                || !string.Equals(NormalizeFixtureToken(category.Category), StockInspectionCategories.StableOrder[index], StringComparison.Ordinal)
                || category.ExactCount < 0)
            {
                throw InvalidData();
            }
        }
    }

    private static string NormalizeFixtureToken(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 64)
        {
            throw InvalidData();
        }

        return normalized;
    }

    private static DwarfFortressDataException InvalidArguments(Exception? innerException = null) =>
        new(DwarfFortressDataErrorCode.InvalidData, "The requested stock category is invalid.", innerException);

    private static DwarfFortressDataException InvalidData(Exception? innerException = null) =>
        new(DwarfFortressDataErrorCode.InvalidData, "The stock inspection data is invalid.", innerException);
}

public sealed class UnavailableStockInspectionService : IStockInspectionService
{
    public Task<InspectStocksToolResult> InspectStocksAsync(string requestedCategory, CancellationToken cancellationToken) =>
        throw new DwarfFortressDataException(
            DwarfFortressDataErrorCode.SourceUnavailable,
            "Stock inspection is unavailable for the configured adapter.");
}
