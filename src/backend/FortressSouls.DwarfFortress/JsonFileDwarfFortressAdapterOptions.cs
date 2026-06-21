namespace FortressSouls.DwarfFortress;

public sealed class JsonFileDwarfFortressAdapterOptions
{
    public string DwarfListPath { get; set; } = string.Empty;

    public string DwarfSnapshotPath { get; set; } = string.Empty;

    public int MaxListFileBytes { get; set; } = 128 * 1024;

    public int MaxSnapshotFileBytes { get; set; } = 512 * 1024;

    public int MaxJsonDepth { get; set; } = 64;

    public int MaxStringLength { get; set; } = 512;

    public int MaxListItems { get; set; } = 1024;

    public int MaxSkills { get; set; } = 256;

    public int MaxTraits { get; set; } = 64;

    public int MaxValues { get; set; } = 128;

    public int MaxNeeds { get; set; } = 256;

    public int MaxMannerisms { get; set; } = 64;

    public JsonFileDwarfFortressAdapterOptions Validate()
    {
        if (string.IsNullOrWhiteSpace(DwarfListPath))
        {
            throw new ArgumentException("A configured dwarf list path is required.", nameof(DwarfListPath));
        }

        if (string.IsNullOrWhiteSpace(DwarfSnapshotPath))
        {
            throw new ArgumentException("A configured dwarf snapshot path is required.", nameof(DwarfSnapshotPath));
        }

        if (MaxListFileBytes <= 0
            || MaxSnapshotFileBytes <= 0
            || MaxJsonDepth <= 0
            || MaxStringLength <= 0
            || MaxListItems <= 0
            || MaxSkills <= 0
            || MaxTraits <= 0
            || MaxValues <= 0
            || MaxNeeds <= 0
            || MaxMannerisms <= 0)
        {
            throw new ArgumentException("JSON-file adapter limits must be positive.");
        }

        return this;
    }
}
