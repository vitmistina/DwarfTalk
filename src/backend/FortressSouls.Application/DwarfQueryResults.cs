namespace FortressSouls.Application;

using FortressSouls.Domain;

public sealed record DwarfListQueryResult(
    string AdapterType,
    DwarfListResult List,
    TimeSpan Duration);

public sealed record DwarfSnapshotQueryResult(
    string AdapterType,
    DwarfSnapshot Snapshot,
    TimeSpan Duration);
