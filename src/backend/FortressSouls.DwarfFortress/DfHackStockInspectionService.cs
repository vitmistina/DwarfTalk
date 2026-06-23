namespace FortressSouls.DwarfFortress;

using System.Globalization;
using System.Text.Json;
using FortressSouls.Application;

public sealed class DfHackStockInspectionService(
    IDfHackProcessRunner processRunner,
    DfHackProcessAdapterOptions options) : IStockInspectionService
{
    private const string ResearchSchemaVersion = "fortress-souls-stock-summary-research.v0.1";
    private const string ProductSchemaVersion = "fortress-souls-stock-summary.v0.2";

    private readonly IDfHackProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly DfHackProcessAdapterOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<InspectStocksToolResult> InspectStocksAsync(string requestedCategory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedCategory = FixtureStockInspectionService.NormalizeRequestedCategory(requestedCategory);

        var result = await _processRunner.RunCommandAsync(
            DfHackCommand.GetStockSummary,
            [],
            cancellationToken);

        if (!result.IsSuccess)
        {
            var category = result.FailureCategory ?? DfHackProcessFailureCategory.Failed;
            throw new DwarfFortressDataException(MapFailureCode(category), BuildSafeMessage(category));
        }

        try
        {
            using var document = JsonDocument.Parse(
                result.Stdout ?? string.Empty,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = _options.MaxJsonDepth
                });

            return MapResult(document.RootElement, normalizedCategory);
        }
        catch (JsonException exception)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.MalformedJson,
                "DFHack returned invalid JSON output.",
                exception);
        }
    }

    private static InspectStocksToolResult MapResult(JsonElement root, string requestedCategory)
    {
        var schemaVersion = ReadRequiredString(root, "schemaVersion");
        if (!string.Equals(schemaVersion, ResearchSchemaVersion, StringComparison.Ordinal)
            && !string.Equals(schemaVersion, ProductSchemaVersion, StringComparison.Ordinal))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.UnsupportedSchema,
                "DFHack returned an unsupported stock summary schema.");
        }

        if (root.TryGetProperty("error", out var errorElement)
            && errorElement.ValueKind == JsonValueKind.Object)
        {
            var code = TryReadOptionalString(errorElement, "code");
            throw new DwarfFortressDataException(
                string.Equals(code, "NO_MAP_LOADED", StringComparison.Ordinal)
                    ? DwarfFortressDataErrorCode.SourceUnavailable
                    : DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned an unavailable or invalid stock summary.");
        }

        if (!root.TryGetProperty("categories", out var categoriesElement)
            || categoriesElement.ValueKind != JsonValueKind.Object)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid stock summary data.");
        }

        var allCategories = MapAllowlistedCategories(categoriesElement);
        var filteredCategories = string.Equals(requestedCategory, StockInspectionCategories.All, StringComparison.Ordinal)
            ? allCategories
            : allCategories.Where(category => string.Equals(category.Category, requestedCategory, StringComparison.Ordinal)).ToArray();

        return new InspectStocksToolResult(
            SchemaVersion: "fortress-souls.inspect-stocks-result.v0.2",
            GameTime: ReadGameTime(root),
            RequestedCategory: requestedCategory,
            Categories: filteredCategories,
            Warnings: ReadWarnings(root));
    }

    private static StockCategory[] MapAllowlistedCategories(JsonElement categoriesElement)
    {
        var mapped = new List<StockCategory>(StockInspectionCategories.StableOrder.Count);
        foreach (var category in StockInspectionCategories.StableOrder)
        {
            var sourcePropertyName = category switch
            {
                StockInspectionCategories.PreparedFood => "preparedFood",
                _ => category
            };

            if (!categoriesElement.TryGetProperty(sourcePropertyName, out var sourceCategory)
                || sourceCategory.ValueKind != JsonValueKind.Object
                || !sourceCategory.TryGetProperty("exact", out var exactValue)
                || exactValue.ValueKind != JsonValueKind.Number
                || !exactValue.TryGetInt32(out var exactCount)
                || exactCount < 0)
            {
                throw new DwarfFortressDataException(
                    DwarfFortressDataErrorCode.InvalidData,
                    "DFHack returned invalid stock summary data.");
            }

            mapped.Add(new StockCategory(category, exactCount));
        }

        return [.. mapped];
    }

    private static string? ReadGameTime(JsonElement root)
    {
        if (!root.TryGetProperty("gameTime", out var gameTimeElement)
            || gameTimeElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!gameTimeElement.TryGetProperty("year", out var yearElement)
            || yearElement.ValueKind != JsonValueKind.Number
            || !yearElement.TryGetInt32(out var year))
        {
            return null;
        }

        if (!gameTimeElement.TryGetProperty("tick", out var tickElement)
            || tickElement.ValueKind != JsonValueKind.Number
            || !tickElement.TryGetInt32(out var tick))
        {
            return year.ToString(CultureInfo.InvariantCulture);
        }

        return $"{year}:{tick}";
    }

    private static string[] ReadWarnings(JsonElement root)
    {
        if (!root.TryGetProperty("warnings", out var warningsElement)
            || warningsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return warningsElement.EnumerateArray()
            .Select(warning =>
            {
                if (warning.ValueKind != JsonValueKind.String)
                {
                    throw new DwarfFortressDataException(
                        DwarfFortressDataErrorCode.InvalidData,
                        "DFHack returned invalid stock summary data.");
                }

                return warning.GetString() ?? string.Empty;
            })
            .ToArray();
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid stock summary data.");
        }

        return value.GetString() ?? throw new DwarfFortressDataException(
            DwarfFortressDataErrorCode.InvalidData,
            "DFHack returned invalid stock summary data.");
    }

    private static string? TryReadOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static string BuildSafeMessage(DfHackProcessFailureCategory category) =>
        category switch
        {
            DfHackProcessFailureCategory.Unavailable => "DFHack is unavailable.",
            DfHackProcessFailureCategory.ExecutableUnavailable => "DFHack executable is unavailable.",
            DfHackProcessFailureCategory.Timeout => "DFHack invocation timed out.",
            DfHackProcessFailureCategory.Crashed => "DFHack process crashed.",
            DfHackProcessFailureCategory.OutputTooLarge => "DFHack output exceeded limits.",
            DfHackProcessFailureCategory.Cancelled => "DFHack invocation was cancelled.",
            _ => "DFHack invocation failed."
        };

    private static DwarfFortressDataErrorCode MapFailureCode(DfHackProcessFailureCategory category) =>
        category switch
        {
            DfHackProcessFailureCategory.Unavailable => DwarfFortressDataErrorCode.DfHackUnavailable,
            DfHackProcessFailureCategory.ExecutableUnavailable => DwarfFortressDataErrorCode.DfHackExecutableUnavailable,
            DfHackProcessFailureCategory.Timeout => DwarfFortressDataErrorCode.DfHackInvocationTimedOut,
            DfHackProcessFailureCategory.Crashed => DwarfFortressDataErrorCode.DfHackProcessCrashed,
            DfHackProcessFailureCategory.OutputTooLarge => DwarfFortressDataErrorCode.DfHackOutputTooLarge,
            DfHackProcessFailureCategory.Cancelled => DwarfFortressDataErrorCode.DfHackInvocationTimedOut,
            _ => DwarfFortressDataErrorCode.DfHackInvocationFailed
        };
}
