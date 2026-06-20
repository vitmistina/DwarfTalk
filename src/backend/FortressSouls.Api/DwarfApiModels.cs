namespace FortressSouls.Api;

public sealed record ApiErrorResponse(string ErrorCode, string Message);

public sealed record DwarfListResponse(
    IReadOnlyList<DwarfListItemResponse> Items,
    DwarfListSourceResponse Source);

public sealed record DwarfListItemResponse(
    string Id,
    string DisplayName,
    string Profession,
    string? CurrentJob,
    string StressLevel);

public sealed record DwarfListSourceResponse(
    string Adapter,
    string SchemaVersion,
    bool WorldLoaded,
    bool SiteLoaded,
    bool MapLoaded);

public sealed record DwarfSnapshotResponse(
    string SchemaVersion,
    string DwarfId,
    DwarfSnapshotSourceResponse Source,
    DwarfIdentityResponse Identity,
    DwarfWorkResponse Work,
    DwarfStressResponse Stress,
    IReadOnlyList<DwarfSkillResponse> Skills,
    DwarfPersonalityResponse Personality,
    DwarfPromptCandidatesResponse PromptCandidates);

public sealed record DwarfSnapshotSourceResponse(
    string Adapter,
    bool WorldLoaded,
    bool SiteLoaded,
    bool MapLoaded,
    bool SoulPresent);

public sealed record DwarfIdentityResponse(
    string DisplayName,
    string Profession,
    string ProfessionToken,
    string CreatureId,
    string CasteId);

public sealed record DwarfWorkResponse(string? CurrentJob);

public sealed record DwarfStressResponse(
    int Raw,
    int Longterm,
    int Category,
    string Scale);

public sealed record DwarfSkillResponse(
    string Token,
    int Rating,
    int Effective,
    int Nominal,
    int Experience,
    int TotalExperience,
    int Rust);

public sealed record DwarfPersonalityTraitResponse(
    string Token,
    int Value,
    int DeviationFromNeutral50,
    int AbsDeviationFromNeutral50);

public sealed record DwarfValueResponse(
    string Token,
    int Type,
    int Strength);

public sealed record DwarfNeedResponse(
    string Token,
    int Id,
    int DeityId,
    int FocusLevel,
    int NeedLevel,
    bool IsUnmet,
    bool IsDeeplyUnmet);

public sealed record DwarfMannerismResponse(
    string Token,
    string SituationToken);

public sealed record DwarfPersonalityResponse(
    bool Present,
    IReadOnlyList<DwarfPersonalityTraitResponse> Traits,
    IReadOnlyList<DwarfValueResponse> Values,
    IReadOnlyList<DwarfNeedResponse> Needs,
    IReadOnlyList<DwarfMannerismResponse> Mannerisms);

public sealed record DwarfPromptCandidatesResponse(
    IReadOnlyList<DwarfSkillResponse> TopSkills,
    IReadOnlyList<DwarfPersonalityTraitResponse> ExtremeTraits,
    IReadOnlyList<DwarfValueResponse> StrongValues,
    IReadOnlyList<DwarfNeedResponse> StrongNeeds,
    IReadOnlyList<DwarfMannerismResponse> Mannerisms);
