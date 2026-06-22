namespace FortressSouls.Application;

using System.Text.Json;
using System.Text.Json.Serialization;
using FortressSouls.Domain;

public sealed class FakePerceptionToolService
{
    public const string LookAroundToolName = "look_around";
    public const string InspectStocksToolName = "inspect_stocks";
    public const string ListDwarvesToolName = "list_dwarves";
    public const string InspectDwarfToolName = "inspect_dwarf";

    private static readonly AgentToolDefinition LookAroundDefinition = new(
        LookAroundToolName,
        "Inspect a bounded revealed area around the selected dwarf.");
    private static readonly AgentToolDefinition InspectStocksDefinition = new(
        InspectStocksToolName,
        "Inspect an exact bounded fortress stock summary.");
    private static readonly AgentToolDefinition ListDwarvesDefinition = new(
        ListDwarvesToolName,
        "List eligible dwarves that may be inspected within the same turn.");
    private static readonly AgentToolDefinition InspectDwarfDefinition = new(
        InspectDwarfToolName,
        "Inspect one eligible dwarf previously returned in this turn.");
    private static readonly IReadOnlySet<string> LookAroundArgumentNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "radius"
    };
    private static readonly IReadOnlySet<string> InspectStocksArgumentNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "category"
    };
    private static readonly IReadOnlySet<string> InspectDwarfArgumentNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "dwarfId"
    };
    private static readonly string[] StockCategoryOrder = ["drinks", "prepared_food", "wood", "stone"];

    private readonly DwarfQueryService _dwarfQueryService;
    private readonly FakePerceptionFixtureSet _fixtures;

    public FakePerceptionToolService(DwarfQueryService dwarfQueryService, FakePerceptionFixtureSet fixtures)
    {
        _dwarfQueryService = dwarfQueryService ?? throw new ArgumentNullException(nameof(dwarfQueryService));
        _fixtures = fixtures ?? throw new ArgumentNullException(nameof(fixtures));
    }

    public IReadOnlyList<AgentToolRegistration> CreateRegistrations() =>
    [
        new AgentToolRegistration(LookAroundDefinition, ExecuteLookAroundAsync, ValidateLookAroundInvocation),
        new AgentToolRegistration(InspectStocksDefinition, ExecuteInspectStocksAsync, ValidateInspectStocksInvocation),
        new AgentToolRegistration(ListDwarvesDefinition, ExecuteListDwarvesAsync, ValidateListDwarvesInvocation),
        new AgentToolRegistration(InspectDwarfDefinition, ExecuteInspectDwarfAsync, ValidateInspectDwarfInvocation)
    ];

    private Task<AgentToolResult> ExecuteLookAroundAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var radius = ParseLookAroundArguments(invocation);
        var result = CreateLookAroundResult(radius);

        return Task.FromResult(AgentToolResult.Create(invocation.Tool, result));
    }

    private Task<AgentToolResult> ExecuteInspectStocksAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestedCategory = ParseInspectStocksArguments(invocation);
        ValidateInspectStocksFixture(_fixtures.Stocks);
        var categories = string.Equals(requestedCategory, "all", StringComparison.Ordinal)
            ? _fixtures.Stocks.Categories
            : _fixtures.Stocks.Categories.Where(category => string.Equals(category.Category, requestedCategory, StringComparison.Ordinal)).ToArray();

        var result = new InspectStocksToolResult(
            SchemaVersion: _fixtures.Stocks.SchemaVersion,
            GameTime: _fixtures.Stocks.GameTime,
            RequestedCategory: requestedCategory,
            Categories: categories.Select(category => new StockCategory(category.Category, category.ExactCount)).ToArray(),
            Warnings: _fixtures.Stocks.Warnings);

        return Task.FromResult(AgentToolResult.Create(invocation.Tool, result));
    }

    private async Task<AgentToolResult> ExecuteListDwarvesAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
    {
        ValidateNoArguments(invocation.Arguments);

        DwarfListQueryResult queryResult;
        try
        {
            queryResult = await _dwarfQueryService.ListDwarvesAsync(cancellationToken);
        }
        catch (DwarfFortressDataException exception)
        {
            throw InvalidData(exception);
        }

        invocation.Session.TurnState.SetInspectableDwarves(queryResult.List.Items.Select(item => item.Id));

        var result = new ListDwarvesToolResult(
            SchemaVersion: "fortress-souls.list-dwarves-result.v0.2",
            Dwarves: queryResult.List.Items
                .Select(item => new ListedDwarf(
                    DwarfId: item.Id.ToString(),
                    DisplayName: item.DisplayName,
                    ProfessionName: item.ProfessionName))
                .ToArray(),
            Warnings: Array.Empty<string>());

        return AgentToolResult.Create(invocation.Tool, result);
    }

    private async Task<AgentToolResult> ExecuteInspectDwarfAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
    {
        var dwarfId = ParseInspectDwarfArguments(invocation);

        if (!invocation.Session.TurnState.IsInspectable(dwarfId))
        {
            throw InvalidArguments();
        }

        DwarfSnapshotQueryResult queryResult;
        try
        {
            queryResult = await _dwarfQueryService.GetDwarfSnapshotAsync(dwarfId, cancellationToken);
        }
        catch (DwarfNotFoundException exception)
        {
            throw NotFound(exception);
        }
        catch (DwarfFortressDataException exception)
        {
            throw InvalidData(exception);
        }

        var snapshot = queryResult.Snapshot;
        if (snapshot.RequestedDwarfId != dwarfId || snapshot.Identity.Id != dwarfId)
        {
            throw InvalidData();
        }

        var result = new InspectDwarfToolResult(
            SchemaVersion: "fortress-souls.inspect-dwarf-result.v0.2",
            Identity: new InspectDwarfIdentity(
                DwarfId: snapshot.Identity.Id.ToString(),
                ReadableName: snapshot.Identity.ReadableName,
                ProfessionName: snapshot.Identity.ProfessionName),
            Work: new InspectDwarfWork(snapshot.Work.CurrentJobType),
            Stress: new InspectDwarfStress(snapshot.Stress.Category, snapshot.Stress.CategoryScale),
            TopSkills: snapshot.PromptCandidates.TopSkills
                .Select(skill => new InspectDwarfSkill(skill.Token, skill.Rating))
                .ToArray(),
            ExtremeTraits: snapshot.PromptCandidates.ExtremeTraits
                .Select(trait => new InspectDwarfTrait(trait.Token, trait.Value))
                .ToArray(),
            StrongValues: snapshot.PromptCandidates.StrongValues
                .Select(value => new InspectDwarfValue(value.Token, value.Strength))
                .ToArray(),
            StrongNeeds: snapshot.PromptCandidates.StrongNeeds
                .Select(need => new InspectDwarfNeed(need.Token, need.NeedLevel, need.IsUnmet, need.IsDeeplyUnmet))
                .ToArray(),
            Mannerisms: snapshot.PromptCandidates.Mannerisms
                .Select(mannerism => new InspectDwarfMannerism(mannerism.Token, mannerism.SituationToken))
                .ToArray(),
            Warnings: Array.Empty<string>());

        return AgentToolResult.Create(invocation.Tool, result);
    }

    private void ValidateLookAroundInvocation(AgentToolInvocation invocation) =>
        _ = ParseLookAroundArguments(invocation);

    private void ValidateInspectStocksInvocation(AgentToolInvocation invocation) =>
        _ = ParseInspectStocksArguments(invocation);

    private void ValidateListDwarvesInvocation(AgentToolInvocation invocation) =>
        ValidateNoArguments(invocation.Arguments);

    private void ValidateInspectDwarfInvocation(AgentToolInvocation invocation) =>
        _ = ParseInspectDwarfArguments(invocation);

    private LookAroundToolResult CreateLookAroundResult(int requestedRadius)
    {
        ValidateLookAroundFixture(_fixtures.LookAround);

        if (requestedRadius != _fixtures.LookAround.Radius)
        {
            throw InvalidArguments();
        }

        return new LookAroundToolResult(
            SchemaVersion: _fixtures.LookAround.SchemaVersion,
            GameTime: _fixtures.LookAround.GameTime,
            Bounds: new LookAroundBounds(
                Radius: _fixtures.LookAround.Radius,
                Width: (_fixtures.LookAround.Radius * 2) + 1,
                Height: (_fixtures.LookAround.Radius * 2) + 1),
            Cells: _fixtures.LookAround.Cells,
            Legend: _fixtures.LookAround.Legend,
            Warnings: _fixtures.LookAround.Warnings);
    }

    private static int ParseLookAroundArguments(AgentToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        if (invocation.Arguments.ValueKind is not JsonValueKind.Object)
        {
            throw InvalidArguments();
        }

        EnsureOnlyProperties(invocation.Arguments, LookAroundArgumentNames);

        if (!invocation.Arguments.TryGetProperty("radius", out var radiusValue))
        {
            return 1;
        }

        if (radiusValue.ValueKind != JsonValueKind.Number || !radiusValue.TryGetInt32(out var radius) || radius < 1 || radius > 1)
        {
            throw InvalidArguments();
        }

        return radius;
    }

    private static string ParseInspectStocksArguments(AgentToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var category = ReadRequiredString(invocation.Arguments, "category");
        var normalized = NormalizeToken(category);
        EnsureOnlyProperties(invocation.Arguments, InspectStocksArgumentNames);

        if (!string.Equals(normalized, "all", StringComparison.Ordinal)
            && !StockCategoryOrder.Contains(normalized, StringComparer.Ordinal))
        {
            throw InvalidArguments();
        }

        return normalized;
    }

    private static DwarfId ParseInspectDwarfArguments(AgentToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var dwarfIdValue = ReadRequiredString(invocation.Arguments, "dwarfId");
        EnsureOnlyProperties(invocation.Arguments, InspectDwarfArgumentNames);

        try
        {
            return DwarfId.Parse(dwarfIdValue);
        }
        catch (ArgumentException exception)
        {
            throw InvalidArguments(exception);
        }
    }

    private static void ValidateLookAroundFixture(FakeLookAroundFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        if (string.IsNullOrWhiteSpace(fixture.SchemaVersion)
            || fixture.Radius < 1
            || fixture.Cells is null
            || fixture.Legend is null
            || fixture.Warnings is null)
        {
            throw InvalidData();
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
                throw InvalidData();
            }

            var isHidden = string.Equals(cell.Visibility, "hidden", StringComparison.Ordinal);
            var isVisible = string.Equals(cell.Visibility, "visible", StringComparison.Ordinal);
            if (!isHidden && !isVisible)
            {
                throw InvalidData();
            }

            if (isHidden
                && (cell.TerrainClass is not null
                    || cell.Walkable is not null
                    || cell.FeatureClass is not null
                    || cell.UnitCount is not null))
            {
                throw InvalidData();
            }

            if (isVisible)
            {
                AddLegendEntry(visibleLegendEntries, cell.TerrainClass);
                AddLegendEntry(visibleLegendEntries, cell.FeatureClass);
            }
        }

        if (fixture.Cells.Count != expectedWidth * expectedWidth)
        {
            throw InvalidData();
        }

        var legendEntries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in fixture.Legend)
        {
            if (!legendEntries.Add(NormalizeFixtureToken(entry)))
            {
                throw InvalidData();
            }
        }

        if (!legendEntries.SetEquals(visibleLegendEntries))
        {
            throw InvalidData();
        }
    }

    private static void ValidateInspectStocksFixture(FakeStockFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        if (string.IsNullOrWhiteSpace(fixture.SchemaVersion)
            || fixture.Categories is null
            || fixture.Warnings is null
            || fixture.Categories.Count != StockCategoryOrder.Length)
        {
            throw InvalidData();
        }

        for (var index = 0; index < StockCategoryOrder.Length; index++)
        {
            var category = fixture.Categories[index];
            if (category is null
                || !string.Equals(NormalizeFixtureToken(category.Category), StockCategoryOrder[index], StringComparison.Ordinal)
                || category.ExactCount < 0)
            {
                throw InvalidData();
            }
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
            throw InvalidData();
        }

        return normalized;
    }

    private static void ValidateNoArguments(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || arguments.EnumerateObject().Any())
        {
            throw InvalidArguments();
        }
    }

    private static void EnsureOnlyProperties(JsonElement arguments, IReadOnlySet<string> allowedProperties)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw InvalidArguments();
        }

        foreach (var property in arguments.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name))
            {
                throw InvalidArguments();
            }
        }
    }

    private static string ReadRequiredString(JsonElement arguments, string propertyName)
    {
        if (arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw InvalidArguments();
        }

        return value.GetString() ?? throw InvalidArguments();
    }

    private static string NormalizeToken(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 64)
        {
            throw InvalidArguments();
        }

        return normalized;
    }

    private static AgentTurnException InvalidArguments(Exception? innerException = null) =>
        new(AgentTurnErrorCode.InvalidArguments, "The agent tool arguments are invalid.", innerException);

    private static AgentTurnException InvalidData(Exception? innerException = null) =>
        new(AgentTurnErrorCode.InvalidData, "The agent turn received invalid data.", innerException);

    private static AgentTurnException NotFound(Exception? innerException = null) =>
        new(AgentTurnErrorCode.NotFound, "The requested dwarf was not found.", innerException);
}

public sealed record FakePerceptionFixtureSet(
    FakeLookAroundFixture LookAround,
    FakeStockFixture Stocks)
{
    public static FakePerceptionFixtureSet Default { get; } = new(
        LookAround: new FakeLookAroundFixture(
            SchemaVersion: "fortress-souls.look-around-result.v0.2",
            GameTime: "125-03-12T08:15",
            Radius: 1,
            Cells:
            [
                new LookAroundCell(-1, -1, "hidden"),
                new LookAroundCell(0, -1, "visible", TerrainClass: "wall", Walkable: false),
                new LookAroundCell(1, -1, "visible", TerrainClass: "floor", Walkable: true),
                new LookAroundCell(-1, 0, "visible", TerrainClass: "ramp", Walkable: true),
                new LookAroundCell(0, 0, "visible", TerrainClass: "floor", Walkable: true, UnitCount: 1),
                new LookAroundCell(1, 0, "visible", TerrainClass: "floor", Walkable: true),
                new LookAroundCell(-1, 1, "visible", TerrainClass: "ramp", Walkable: true),
                new LookAroundCell(0, 1, "visible", TerrainClass: "building", Walkable: false, FeatureClass: "building"),
                new LookAroundCell(1, 1, "visible", TerrainClass: "building", Walkable: false, FeatureClass: "building", UnitCount: 2)
            ],
            Legend: ["building", "floor", "ramp", "wall"],
            Warnings: Array.Empty<string>()),
        Stocks: new FakeStockFixture(
            SchemaVersion: "fortress-souls.inspect-stocks-result.v0.2",
            GameTime: "125-03-12T08:15",
            Categories:
            [
                new StockCategory("drinks", 60),
                new StockCategory("prepared_food", 32),
                new StockCategory("wood", 48),
                new StockCategory("stone", 128)
            ],
            Warnings: Array.Empty<string>()));
}

public sealed record FakeLookAroundFixture(
    string SchemaVersion,
    string? GameTime,
    int Radius,
    IReadOnlyList<LookAroundCell> Cells,
    IReadOnlyList<string> Legend,
    IReadOnlyList<string> Warnings);

public sealed record FakeStockFixture(
    string SchemaVersion,
    string? GameTime,
    IReadOnlyList<StockCategory> Categories,
    IReadOnlyList<string> Warnings);

public sealed record LookAroundToolResult(
    string SchemaVersion,
    string? GameTime,
    LookAroundBounds Bounds,
    IReadOnlyList<LookAroundCell> Cells,
    IReadOnlyList<string> Legend,
    IReadOnlyList<string> Warnings);

public sealed record LookAroundBounds(int Radius, int Width, int Height);

public sealed record LookAroundCell(
    int Dx,
    int Dy,
    string Visibility,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TerrainClass = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? Walkable = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FeatureClass = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? UnitCount = null);

public sealed record InspectStocksToolResult(
    string SchemaVersion,
    string? GameTime,
    string RequestedCategory,
    IReadOnlyList<StockCategory> Categories,
    IReadOnlyList<string> Warnings);

public sealed record StockCategory(string Category, int ExactCount);

public sealed record ListDwarvesToolResult(
    string SchemaVersion,
    IReadOnlyList<ListedDwarf> Dwarves,
    IReadOnlyList<string> Warnings);

public sealed record ListedDwarf(
    string DwarfId,
    string DisplayName,
    string ProfessionName);

public sealed record InspectDwarfToolResult(
    string SchemaVersion,
    InspectDwarfIdentity Identity,
    InspectDwarfWork Work,
    InspectDwarfStress Stress,
    IReadOnlyList<InspectDwarfSkill> TopSkills,
    IReadOnlyList<InspectDwarfTrait> ExtremeTraits,
    IReadOnlyList<InspectDwarfValue> StrongValues,
    IReadOnlyList<InspectDwarfNeed> StrongNeeds,
    IReadOnlyList<InspectDwarfMannerism> Mannerisms,
    IReadOnlyList<string> Warnings);

public sealed record InspectDwarfIdentity(
    string DwarfId,
    string ReadableName,
    string ProfessionName);

public sealed record InspectDwarfWork(string? CurrentJobType);

public sealed record InspectDwarfStress(int Category, string CategoryScale);

public sealed record InspectDwarfSkill(string Token, int Rating);

public sealed record InspectDwarfTrait(string Token, int Value);

public sealed record InspectDwarfValue(string Token, int Strength);

public sealed record InspectDwarfNeed(
    string Token,
    int NeedLevel,
    bool IsUnmet,
    bool IsDeeplyUnmet);

public sealed record InspectDwarfMannerism(string Token, string SituationToken);