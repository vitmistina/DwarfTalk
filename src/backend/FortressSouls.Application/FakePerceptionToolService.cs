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
    public const int DefaultLookAroundRadius = 1;
    public const int MaxLookAroundRadius = 2;

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
    private readonly DwarfQueryService _dwarfQueryService;
    private readonly ISurroundingsInspectionService _surroundingsInspectionService;
    private readonly IStockInspectionService _stockInspectionService;

    public FakePerceptionToolService(DwarfQueryService dwarfQueryService, FakePerceptionFixtureSet fixtures)
        : this(
            dwarfQueryService,
            new FixtureSurroundingsInspectionService((fixtures ?? throw new ArgumentNullException(nameof(fixtures))).LookAround),
            new FixtureStockInspectionService((fixtures ?? throw new ArgumentNullException(nameof(fixtures))).Stocks),
            fixtures)
    {
    }

    public FakePerceptionToolService(
        DwarfQueryService dwarfQueryService,
        ISurroundingsInspectionService surroundingsInspectionService,
        IStockInspectionService stockInspectionService,
        FakePerceptionFixtureSet fixtures)
    {
        _dwarfQueryService = dwarfQueryService ?? throw new ArgumentNullException(nameof(dwarfQueryService));
        _surroundingsInspectionService = surroundingsInspectionService ?? throw new ArgumentNullException(nameof(surroundingsInspectionService));
        _stockInspectionService = stockInspectionService ?? throw new ArgumentNullException(nameof(stockInspectionService));
        ArgumentNullException.ThrowIfNull(fixtures);
    }

    public IReadOnlyList<AgentToolRegistration> CreateRegistrations() =>
    [
        new AgentToolRegistration(LookAroundDefinition, ExecuteLookAroundAsync, ValidateLookAroundInvocation),
        new AgentToolRegistration(InspectStocksDefinition, ExecuteInspectStocksAsync, ValidateInspectStocksInvocation),
        new AgentToolRegistration(ListDwarvesDefinition, ExecuteListDwarvesAsync, ValidateListDwarvesInvocation),
        new AgentToolRegistration(InspectDwarfDefinition, ExecuteInspectDwarfAsync, ValidateInspectDwarfInvocation)
    ];

    private Task<AgentToolResult> ExecuteLookAroundAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
        => ExecuteLookAroundCoreAsync(invocation, cancellationToken);

    private async Task<AgentToolResult> ExecuteLookAroundCoreAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var radius = ParseLookAroundArguments(invocation);
        LookAroundToolResult result;
        try
        {
            result = await _surroundingsInspectionService.InspectAroundAsync(
                invocation.Session.DwarfId,
                radius,
                cancellationToken);
        }
        catch (DwarfFortressDataException exception) when (exception.ErrorCode is DwarfFortressDataErrorCode.DfHackUnavailable
            or DwarfFortressDataErrorCode.DfHackExecutableUnavailable
            or DwarfFortressDataErrorCode.DfHackInvocationTimedOut
            or DwarfFortressDataErrorCode.SourceUnavailable)
        {
            throw Unavailable(exception);
        }
        catch (DwarfFortressDataException exception)
        {
            throw InvalidData(exception);
        }

        return AgentToolResult.Create(invocation.Tool, result);
    }

    private Task<AgentToolResult> ExecuteInspectStocksAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
        => ExecuteInspectStocksCoreAsync(invocation, cancellationToken);

    private async Task<AgentToolResult> ExecuteInspectStocksCoreAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestedCategory = ParseInspectStocksArguments(invocation);

        InspectStocksToolResult result;
        try
        {
            result = await _stockInspectionService.InspectStocksAsync(requestedCategory, cancellationToken);
        }
        catch (DwarfFortressDataException exception) when (exception.ErrorCode is DwarfFortressDataErrorCode.DfHackUnavailable
            or DwarfFortressDataErrorCode.DfHackExecutableUnavailable
            or DwarfFortressDataErrorCode.DfHackInvocationTimedOut
            or DwarfFortressDataErrorCode.SourceUnavailable)
        {
            throw Unavailable(exception);
        }
        catch (DwarfFortressDataException exception)
        {
            throw InvalidData(exception);
        }

        return AgentToolResult.Create(invocation.Tool, result);
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
            return DefaultLookAroundRadius;
        }

        if (radiusValue.ValueKind != JsonValueKind.Number
            || !radiusValue.TryGetInt32(out var radius)
            || radius < DefaultLookAroundRadius
            || radius > MaxLookAroundRadius)
        {
            throw InvalidArguments();
        }

        return radius;
    }

    private static string ParseInspectStocksArguments(AgentToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        var category = ReadRequiredString(invocation.Arguments, "category");
        EnsureOnlyProperties(invocation.Arguments, InspectStocksArgumentNames);

        try
        {
            return FixtureStockInspectionService.NormalizeRequestedCategory(category);
        }
        catch (DwarfFortressDataException exception)
        {
            throw InvalidArguments(exception);
        }
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

    private static AgentTurnException Unavailable(Exception? innerException = null) =>
        new(AgentTurnErrorCode.Unavailable, "The agent tool is unavailable.", innerException);

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
            Radius: 2,
            Cells:
            [
                new LookAroundCell(-2, -2, "hidden"),
                new LookAroundCell(-1, -2, "visible", TerrainClass: "wall", Walkable: false),
                new LookAroundCell(0, -2, "visible", TerrainClass: "wall", Walkable: false),
                new LookAroundCell(1, -2, "visible", TerrainClass: "floor", Walkable: true),
                new LookAroundCell(2, -2, "hidden"),
                new LookAroundCell(-2, -1, "hidden"),
                new LookAroundCell(-1, -1, "hidden"),
                new LookAroundCell(0, -1, "visible", TerrainClass: "wall", Walkable: false),
                new LookAroundCell(1, -1, "visible", TerrainClass: "floor", Walkable: true),
                new LookAroundCell(2, -1, "visible", TerrainClass: "floor", Walkable: true),
                new LookAroundCell(-2, 0, "visible", TerrainClass: "ramp", Walkable: true),
                new LookAroundCell(-1, 0, "visible", TerrainClass: "ramp", Walkable: true),
                new LookAroundCell(0, 0, "visible", TerrainClass: "floor", Walkable: true, UnitCount: 1),
                new LookAroundCell(1, 0, "visible", TerrainClass: "floor", Walkable: true),
                new LookAroundCell(2, 0, "hidden"),
                new LookAroundCell(-2, 1, "visible", TerrainClass: "ramp", Walkable: true),
                new LookAroundCell(-1, 1, "visible", TerrainClass: "ramp", Walkable: true),
                new LookAroundCell(0, 1, "visible", TerrainClass: "building", Walkable: false, FeatureClass: "building"),
                new LookAroundCell(1, 1, "visible", TerrainClass: "building", Walkable: false, FeatureClass: "building", UnitCount: 2),
                new LookAroundCell(2, 1, "hidden"),
                new LookAroundCell(-2, 2, "hidden"),
                new LookAroundCell(-1, 2, "hidden"),
                new LookAroundCell(0, 2, "visible", TerrainClass: "floor", Walkable: true),
                new LookAroundCell(1, 2, "hidden"),
                new LookAroundCell(2, 2, "hidden")
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
