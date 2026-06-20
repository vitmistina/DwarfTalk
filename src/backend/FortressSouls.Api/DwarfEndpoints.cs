namespace FortressSouls.Api;

using FortressSouls.Application;
using FortressSouls.Domain;
using FortressSouls.Observability;
using Microsoft.AspNetCore.Http.HttpResults;

internal static class DwarfEndpoints
{
    public static IEndpointRouteBuilder MapDwarfEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dwarves");

        group.MapGet("/", ListDwarvesAsync)
            .WithName("ListDwarves")
            .Produces<DwarfListResponse>();

        group.MapGet("/{dwarfId}/snapshot", GetDwarfSnapshotAsync)
            .WithName("GetDwarfSnapshot")
            .Produces<DwarfSnapshotResponse>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status500InternalServerError)
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> ListDwarvesAsync(
        DwarfQueryService queryService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("FortressSouls.Api.Dwarves");

        try
        {
            var result = await queryService.ListDwarvesAsync(cancellationToken);
            logger.LogInformation(
                "Dwarf list completed with {Operation} for {AdapterType} in {DurationMs} ms",
                "ListDwarves",
                result.AdapterType,
                result.Duration.TotalMilliseconds);

            return TypedResults.Ok(MapList(result));
        }
        catch (DwarfFortressDataException exception)
        {
            var (statusCode, errorCode, message) = MapDataException(exception);

            logger.LogWarning(
                "Dwarf list failed with {Operation} and {ErrorCode}",
                "ListDwarves",
                errorCode);

            return TypedResults.Json(new ApiErrorResponse(errorCode, message), statusCode: statusCode);
        }
    }

    private static async Task<IResult> GetDwarfSnapshotAsync(
        string dwarfId,
        DwarfQueryService queryService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("FortressSouls.Api.Dwarves");

        if (!TryParseDwarfId(dwarfId, out var parsedDwarfId))
        {
            return TypedResults.BadRequest(new ApiErrorResponse("invalid_dwarf_id", "The provided dwarf ID is invalid."));
        }

        try
        {
            var result = await queryService.GetDwarfSnapshotAsync(parsedDwarfId, cancellationToken);
            logger.LogInformation(
                "Dwarf snapshot completed with {Operation} for {DwarfId} via {AdapterType} in {DurationMs} ms",
                "GetDwarfSnapshot",
                parsedDwarfId.ToString(),
                result.AdapterType,
                result.Duration.TotalMilliseconds);

            return TypedResults.Ok(MapSnapshot(result));
        }
        catch (DwarfNotFoundException)
        {
            logger.LogWarning(
                "Dwarf snapshot failed with {Operation} and {ErrorCode}",
                "GetDwarfSnapshot",
                "dwarf_not_found");

            return TypedResults.NotFound(new ApiErrorResponse("dwarf_not_found", "The requested dwarf was not found."));
        }
        catch (DwarfFortressDataException exception)
        {
            var (statusCode, errorCode, message) = MapDataException(exception);

            logger.LogWarning(
                "Dwarf snapshot failed with {Operation} and {ErrorCode}",
                "GetDwarfSnapshot",
                errorCode);

            return TypedResults.Json(new ApiErrorResponse(errorCode, message), statusCode: statusCode);
        }
    }

    private static bool TryParseDwarfId(string value, out DwarfId dwarfId)
    {
        try
        {
            dwarfId = DwarfId.Parse(value);
            return true;
        }
        catch (ArgumentException)
        {
            dwarfId = default;
            return false;
        }
    }

    private static (int StatusCode, string ErrorCode, string Message) MapDataException(DwarfFortressDataException exception) =>
        exception.ErrorCode switch
        {
            DwarfFortressDataErrorCode.MissingSource or DwarfFortressDataErrorCode.SourceUnavailable =>
                (StatusCodes.Status503ServiceUnavailable, "dwarf_source_unavailable", "The dwarf data source is unavailable."),
            _ => (StatusCodes.Status500InternalServerError, "dwarf_data_invalid", "The dwarf data source returned invalid data.")
        };

    private static DwarfListResponse MapList(DwarfListQueryResult result) =>
        new(
            Items: result.List.Items
                .Select(item => new DwarfListItemResponse(
                    Id: item.Id.ToString(),
                    DisplayName: item.DisplayName,
                    Profession: item.ProfessionName,
                    CurrentJob: item.CurrentJobType,
                    StressLevel: item.StressCategoryScale))
                .ToArray(),
            Source: new DwarfListSourceResponse(
                Adapter: result.AdapterType,
                SchemaVersion: result.List.SchemaVersion,
                WorldLoaded: result.List.Source.WorldLoaded,
                SiteLoaded: result.List.Source.SiteLoaded,
                MapLoaded: result.List.Source.MapLoaded));

    private static DwarfSnapshotResponse MapSnapshot(DwarfSnapshotQueryResult result) =>
        new(
            SchemaVersion: result.Snapshot.SchemaVersion,
            DwarfId: result.Snapshot.RequestedDwarfId.ToString(),
            Source: new DwarfSnapshotSourceResponse(
                Adapter: result.AdapterType,
                WorldLoaded: result.Snapshot.Source.WorldLoaded,
                SiteLoaded: result.Snapshot.Source.SiteLoaded,
                MapLoaded: result.Snapshot.Source.MapLoaded,
                SoulPresent: result.Snapshot.Source.SoulPresent),
            Identity: new DwarfIdentityResponse(
                DisplayName: result.Snapshot.Identity.ReadableName,
                Profession: result.Snapshot.Identity.ProfessionName,
                ProfessionToken: result.Snapshot.Identity.ProfessionToken,
                CreatureId: result.Snapshot.Identity.CreatureId,
                CasteId: result.Snapshot.Identity.CasteId),
            Work: new DwarfWorkResponse(result.Snapshot.Work.CurrentJobType),
            Stress: new DwarfStressResponse(
                result.Snapshot.Stress.Raw,
                result.Snapshot.Stress.Longterm,
                result.Snapshot.Stress.Category,
                result.Snapshot.Stress.CategoryScale),
            Skills: result.Snapshot.Skills.Items.Select(MapSkill).ToArray(),
            Personality: new DwarfPersonalityResponse(
                Present: result.Snapshot.Personality.Present,
                Traits: result.Snapshot.Personality.Traits.Items.Select(MapTrait).ToArray(),
                Values: result.Snapshot.Personality.Values.Items.Select(MapValue).ToArray(),
                Needs: result.Snapshot.Personality.Needs.Items.Select(MapNeed).ToArray(),
                Mannerisms: result.Snapshot.Personality.Mannerisms.Items.Select(MapMannerism).ToArray()),
            PromptCandidates: new DwarfPromptCandidatesResponse(
                TopSkills: result.Snapshot.PromptCandidates.TopSkills.Select(MapSkill).ToArray(),
                ExtremeTraits: result.Snapshot.PromptCandidates.ExtremeTraits.Select(MapTrait).ToArray(),
                StrongValues: result.Snapshot.PromptCandidates.StrongValues.Select(MapValue).ToArray(),
                StrongNeeds: result.Snapshot.PromptCandidates.StrongNeeds.Select(MapNeed).ToArray(),
                Mannerisms: result.Snapshot.PromptCandidates.Mannerisms.Select(MapMannerism).ToArray()));

    private static DwarfSkillResponse MapSkill(DwarfSkill skill) =>
        new(skill.Token, skill.Rating, skill.Effective, skill.Nominal, skill.Experience, skill.TotalExperience, skill.Rust);

    private static DwarfPersonalityTraitResponse MapTrait(DwarfPersonalityTrait trait) =>
        new(trait.Token, trait.Value, trait.DeviationFromNeutral50, trait.AbsDeviationFromNeutral50);

    private static DwarfValueResponse MapValue(DwarfValue value) =>
        new(value.Token, value.Type, value.Strength);

    private static DwarfNeedResponse MapNeed(DwarfNeed need) =>
        new(need.Token, need.Id, need.DeityId, need.FocusLevel, need.NeedLevel, need.IsUnmet, need.IsDeeplyUnmet);

    private static DwarfMannerismResponse MapMannerism(DwarfMannerism mannerism) =>
        new(mannerism.Token, mannerism.SituationToken);
}
