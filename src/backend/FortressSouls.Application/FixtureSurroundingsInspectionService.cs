namespace FortressSouls.Application;

using FortressSouls.Domain;

public sealed class FixtureSurroundingsInspectionService(FakeLookAroundFixture fixture) : ISurroundingsInspectionService
{
    private readonly FakeLookAroundFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    public Task<LookAroundToolResult> InspectAroundAsync(
        DwarfId observerDwarfId,
        int requestedRadius,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateFixture(_fixture);

        if (requestedRadius < 1 || requestedRadius > _fixture.Radius)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "The requested surroundings radius is unsupported.");
        }

        var cells = _fixture.Cells
            .Where(cell => Math.Abs(cell.Dx) <= requestedRadius && Math.Abs(cell.Dy) <= requestedRadius)
            .ToArray();

        return Task.FromResult(new LookAroundToolResult(
            SchemaVersion: _fixture.SchemaVersion,
            GameTime: _fixture.GameTime,
            Bounds: new LookAroundBounds(
                Radius: requestedRadius,
                Width: (requestedRadius * 2) + 1,
                Height: (requestedRadius * 2) + 1),
            Cells: cells,
            Legend: DeriveLegend(cells),
            Warnings: _fixture.Warnings));
    }

    private static void ValidateFixture(FakeLookAroundFixture fixture)
    {
        if (string.IsNullOrWhiteSpace(fixture.SchemaVersion)
            || fixture.Radius < 1
            || fixture.Cells is null
            || fixture.Legend is null
            || fixture.Warnings is null)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "The configured surroundings fixture is invalid.");
        }

        var expectedWidth = (fixture.Radius * 2) + 1;
        var seenPositions = new HashSet<(int Dx, int Dy)>();
        var visibleLegendEntries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in fixture.Cells)
        {
            if (cell is null
                || cell.Dx < -fixture.Radius
                || cell.Dx > fixture.Radius
                || cell.Dy < -fixture.Radius
                || cell.Dy > fixture.Radius
                || !seenPositions.Add((cell.Dx, cell.Dy)))
            {
                throw new DwarfFortressDataException(
                    DwarfFortressDataErrorCode.InvalidData,
                    "The configured surroundings fixture is invalid.");
            }

            var isHidden = string.Equals(cell.Visibility, "hidden", StringComparison.Ordinal);
            var isVisible = string.Equals(cell.Visibility, "visible", StringComparison.Ordinal);
            if (!isHidden && !isVisible)
            {
                throw new DwarfFortressDataException(
                    DwarfFortressDataErrorCode.InvalidData,
                    "The configured surroundings fixture is invalid.");
            }

            if (isHidden
                && (cell.TerrainClass is not null
                    || cell.Walkable is not null
                    || cell.FeatureClass is not null
                    || cell.UnitCount is not null))
            {
                throw new DwarfFortressDataException(
                    DwarfFortressDataErrorCode.InvalidData,
                    "The configured surroundings fixture is invalid.");
            }

            if (isVisible)
            {
                AddLegendEntry(visibleLegendEntries, cell.TerrainClass);
                AddLegendEntry(visibleLegendEntries, cell.FeatureClass);
            }
        }

        if (fixture.Cells.Count != expectedWidth * expectedWidth)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "The configured surroundings fixture is invalid.");
        }

        var legendEntries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in fixture.Legend)
        {
            if (!legendEntries.Add(NormalizeFixtureToken(entry)))
            {
                throw new DwarfFortressDataException(
                    DwarfFortressDataErrorCode.InvalidData,
                    "The configured surroundings fixture is invalid.");
            }
        }

        if (!legendEntries.SetEquals(visibleLegendEntries))
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "The configured surroundings fixture is invalid.");
        }
    }

    private static void AddLegendEntry(ISet<string> legendEntries, string? value)
    {
        if (value is null)
        {
            return;
        }

        legendEntries.Add(NormalizeFixtureToken(value));
    }

    private static string NormalizeFixtureToken(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 64)
        {
            throw new DwarfFortressDataException(
                DwarfFortressDataErrorCode.InvalidData,
                "The configured surroundings fixture is invalid.");
        }

        return normalized;
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
}

public sealed class UnavailableSurroundingsInspectionService : ISurroundingsInspectionService
{
    public Task<LookAroundToolResult> InspectAroundAsync(
        DwarfId observerDwarfId,
        int requestedRadius,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        throw new DwarfFortressDataException(
            DwarfFortressDataErrorCode.SourceUnavailable,
            "Live surroundings inspection is unavailable for the active adapter.");
    }
}
