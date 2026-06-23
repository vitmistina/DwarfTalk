namespace FortressSouls.DwarfFortress;

using System.Globalization;
using System.Text.Json;
using FortressSouls.Application;
using FortressSouls.Domain;

public sealed class DfHackSurroundingsInspectionService(
    IDfHackProcessRunner processRunner,
    DfHackProcessAdapterOptions options) : ISurroundingsInspectionService
{
    private const string ResearchSchemaVersion = "fortress-souls-spatial-vision-research.v0.1";
    private const string ProductSchemaVersion = "fortress-souls-dwarf-surroundings.v0.2";
    private static readonly HashSet<string> AllowedTerrainClasses = new(StringComparer.Ordinal)
    {
        "building",
        "floor",
        "ramp",
        "wall"
    };

    private readonly IDfHackProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly DfHackProcessAdapterOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<LookAroundToolResult> InspectAroundAsync(
        DwarfId observerDwarfId,
        int requestedRadius,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _processRunner.RunCommandAsync(
            DfHackCommand.GetDwarfSurroundings,
            [observerDwarfId.ToString(), requestedRadius.ToString(CultureInfo.InvariantCulture)],
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

            return MapResult(document.RootElement, requestedRadius);
        }
        catch (JsonException exception)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.MalformedJson,
                "DFHack returned invalid JSON output.",
                exception);
        }
    }

    private static LookAroundToolResult MapResult(JsonElement root, int requestedRadius)
    {
        var schemaVersion = ReadRequiredString(root, "schemaVersion");

        if (root.TryGetProperty("error", out var errorElement)
            && errorElement.ValueKind == JsonValueKind.Object)
        {
            var code = TryReadOptionalString(errorElement, "code");
            throw new DwarfFortressDataException(
                string.Equals(code, "NO_MAP_LOADED", StringComparison.Ordinal)
                    ? DwarfFortressDataErrorCode.SourceUnavailable
                    : DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned an unavailable or invalid surroundings result.");
        }

        return schemaVersion switch
        {
            ProductSchemaVersion => MapProductResult(root, requestedRadius),
            ResearchSchemaVersion => MapResearchResult(root, requestedRadius),
            _ => throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.UnsupportedSchema,
                "DFHack returned an unsupported surroundings schema.")
        };
    }

    private static LookAroundToolResult MapProductResult(JsonElement root, int requestedRadius)
    {
        var boundsElement = ReadRequiredObject(root, "bounds");
        var radius = ReadRequiredInt32(boundsElement, "radius");
        var width = ReadRequiredInt32(boundsElement, "width");
        var height = ReadRequiredInt32(boundsElement, "height");
        ValidateBounds(radius, width, height, requestedRadius);

        var cells = ReadRequiredArray(root, "cells")
            .EnumerateArray()
            .Select(cell => MapProductCell(cell, radius))
            .ToArray();

        ValidateCellCoverage(cells, radius);

        return new LookAroundToolResult(
            SchemaVersion: "fortress-souls.look-around-result.v0.2",
            GameTime: ReadGameTime(root),
            Bounds: new LookAroundBounds(radius, width, height),
            Cells: cells,
            Legend: DeriveLegend(cells),
            Warnings: ReadWarnings(root));
    }

    private static LookAroundToolResult MapResearchResult(JsonElement root, int requestedRadius)
    {
        var queryElement = ReadRequiredObject(root, "query");
        if (!string.Equals(ReadRequiredString(queryElement, "mode"), "unit", StringComparison.Ordinal))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        var radius = ReadRequiredInt32(queryElement, "radius");
        var centerElement = ReadRequiredObject(queryElement, "unitPosition");
        var centerX = ReadRequiredInt32(centerElement, "x");
        var centerY = ReadRequiredInt32(centerElement, "y");
        var centerZ = ReadRequiredInt32(centerElement, "z");

        var boundsElement = ReadRequiredObject(root, "bounds");
        var width = ReadRequiredInt32(boundsElement, "width");
        var height = ReadRequiredInt32(boundsElement, "height");
        ValidateBounds(radius, width, height, requestedRadius);

        var cells = ReadRequiredArray(root, "cells")
            .EnumerateArray()
            .Select(cell => MapResearchCell(cell, centerX, centerY, centerZ, radius))
            .ToArray();

        ValidateCellCoverage(cells, radius);

        return new LookAroundToolResult(
            SchemaVersion: "fortress-souls.look-around-result.v0.2",
            GameTime: ReadGameTime(root),
            Bounds: new LookAroundBounds(radius, width, height),
            Cells: cells,
            Legend: DeriveLegend(cells),
            Warnings: ReadWarnings(root));
    }

    private static LookAroundCell MapProductCell(JsonElement cell, int radius)
    {
        var dx = ReadRequiredInt32(cell, "dx");
        var dy = ReadRequiredInt32(cell, "dy");
        ValidateOffset(dx, dy, radius);

        var visibility = ReadRequiredString(cell, "visibility");
        return visibility switch
        {
            "hidden" => new LookAroundCell(dx, dy, visibility),
            "visible" => new LookAroundCell(
                dx,
                dy,
                visibility,
                TerrainClass: ReadOptionalTerrainClass(cell),
                Walkable: ReadOptionalBoolean(cell, "walkable"),
                FeatureClass: ReadOptionalFeatureClass(cell),
                UnitCount: ReadOptionalNonNegativeInt32(cell, "unitCount")),
            _ => throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.")
        };
    }

    private static LookAroundCell MapResearchCell(JsonElement cell, int centerX, int centerY, int centerZ, int radius)
    {
        var x = ReadRequiredInt32(cell, "x");
        var y = ReadRequiredInt32(cell, "y");
        var z = ReadRequiredInt32(cell, "z");
        if (z != centerZ)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        var dx = x - centerX;
        var dy = y - centerY;
        ValidateOffset(dx, dy, radius);

        var isHidden = ReadRequiredBoolean(cell, "hidden");
        if (isHidden)
        {
            return new LookAroundCell(dx, dy, "hidden");
        }

        if (!ReadRequiredBoolean(cell, "visible"))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        return new LookAroundCell(
            dx,
            dy,
            "visible",
            TerrainClass: MapResearchTerrainClass(cell),
            Walkable: ReadOptionalResearchWalkable(cell),
            FeatureClass: MapResearchFeatureClass(cell),
            UnitCount: ReadResearchUnitCount(cell));
    }

    private static string? MapResearchTerrainClass(JsonElement cell)
    {
        if (HasResearchBuildingFeature(cell))
        {
            return "building";
        }

        if (!cell.TryGetProperty("terrain", out var terrainElement)
            || terrainElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var shape = TryReadOptionalString(terrainElement, "shape");
        return shape switch
        {
            "WALL" or "FORTIFICATION" => "wall",
            "RAMP" or "RAMP_TOP" => "ramp",
            "FLOOR" or "BOULDER" or "BROOK_TOP" => "floor",
            _ when shape is not null && shape.Contains("STAIR", StringComparison.Ordinal) => "floor",
            _ when ReadOptionalResearchWalkable(cell) is true => "floor",
            _ => null
        };
    }

    private static string? MapResearchFeatureClass(JsonElement cell) =>
        HasResearchBuildingFeature(cell) ? "building" : null;

    private static bool HasResearchBuildingFeature(JsonElement cell) =>
        (cell.TryGetProperty("building", out var buildingElement) && buildingElement.ValueKind == JsonValueKind.Object)
        || (cell.TryGetProperty("zones", out var zonesElement) && zonesElement.ValueKind == JsonValueKind.Array && zonesElement.GetArrayLength() > 0);

    private static bool? ReadOptionalResearchWalkable(JsonElement cell)
    {
        if (!cell.TryGetProperty("walkable", out var walkableElement))
        {
            return null;
        }

        return walkableElement.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when walkableElement.TryGetInt32(out var walkable) => walkable > 0,
            _ => throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.")
        };
    }

    private static int? ReadResearchUnitCount(JsonElement cell)
    {
        if (!cell.TryGetProperty("units", out var unitsElement)
            || unitsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var unitCount = unitsElement.GetArrayLength();
        return unitCount > 0 ? unitCount : null;
    }

    private static string[] DeriveLegend(IReadOnlyList<LookAroundCell> cells) =>
        cells
            .Where(cell => string.Equals(cell.Visibility, "visible", StringComparison.Ordinal))
            .SelectMany(cell => new[] { cell.TerrainClass, cell.FeatureClass })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static void ValidateBounds(int radius, int width, int height, int requestedRadius)
    {
        if (radius < 1
            || width != (radius * 2) + 1
            || height != (radius * 2) + 1
            || radius != requestedRadius)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }
    }

    private static void ValidateCellCoverage(IReadOnlyList<LookAroundCell> cells, int radius)
    {
        var expectedCellCount = ((radius * 2) + 1) * ((radius * 2) + 1);
        if (cells.Count != expectedCellCount)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        var seenOffsets = new HashSet<(int Dx, int Dy)>();
        foreach (var cell in cells)
        {
            if (!seenOffsets.Add((cell.Dx, cell.Dy)))
            {
                throw new DwarfFortressDataException(
                    DwarfFortressDataErrorCode.InvalidData,
                    "DFHack returned invalid surroundings data.");
            }
        }
    }

    private static void ValidateOffset(int dx, int dy, int radius)
    {
        if (dx < -radius || dx > radius || dy < -radius || dy > radius)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }
    }

    private static JsonElement ReadRequiredObject(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        return value;
    }

    private static JsonElement ReadRequiredArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        return value.GetString() ?? throw new DwarfFortressDataException(
            DwarfFortressDataErrorCode.InvalidData,
            "DFHack returned invalid surroundings data.");
    }

    private static int ReadRequiredInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var parsed))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        return parsed;
    }

    private static bool ReadRequiredBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        return value.GetBoolean();
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

    private static string? ReadOptionalTerrainClass(JsonElement element)
    {
        var terrainClass = TryReadOptionalString(element, "terrainClass");
        if (terrainClass is null)
        {
            return null;
        }

        if (!AllowedTerrainClasses.Contains(terrainClass))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        return terrainClass;
    }

    private static string? ReadOptionalFeatureClass(JsonElement element)
    {
        var featureClass = TryReadOptionalString(element, "featureClass");
        if (featureClass is null)
        {
            return null;
        }

        if (!string.Equals(featureClass, "building", StringComparison.Ordinal))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        return featureClass;
    }

    private static bool? ReadOptionalBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        return value.GetBoolean();
    }

    private static int? ReadOptionalNonNegativeInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var parsed)
            || parsed < 0)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "DFHack returned invalid surroundings data.");
        }

        return parsed == 0 ? null : parsed;
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
                        "DFHack returned invalid surroundings data.");
                }

                return warning.GetString() ?? string.Empty;
            })
            .ToArray();
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
