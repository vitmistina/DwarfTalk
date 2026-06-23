namespace FortressSouls.Application;

public interface IStockInspectionService
{
    Task<InspectStocksToolResult> InspectStocksAsync(string requestedCategory, CancellationToken cancellationToken);
}

public static class StockInspectionCategories
{
    public const string All = "all";
    public const string Drinks = "drinks";
    public const string PreparedFood = "prepared_food";
    public const string Wood = "wood";
    public const string Stone = "stone";

    public static IReadOnlyList<string> StableOrder { get; } =
    [
        Drinks,
        PreparedFood,
        Wood,
        Stone
    ];
}

public sealed record InspectStocksToolResult(
    string SchemaVersion,
    string? GameTime,
    string RequestedCategory,
    IReadOnlyList<StockCategory> Categories,
    IReadOnlyList<string> Warnings);

public sealed record StockCategory(string Category, int ExactCount);
