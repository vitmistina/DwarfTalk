namespace FortressSouls.DwarfFortress;

public enum DwarfFortressAdapterType
{
    Fake,
    JsonFile,
    DfHackProcess
}

public sealed class DwarfFortressAdapterOptions
{
    public const string ConfigurationSectionPath = "FortressSouls:DwarfFortress";

    public string AdapterType { get; set; } = string.Empty;

    public JsonFileDwarfFortressAdapterOptions JsonFile { get; set; } = new();

    public DwarfFortressAdapterType ResolveAdapterType(bool dfHackEnabled)
    {
        if (string.IsNullOrWhiteSpace(AdapterType))
        {
            return dfHackEnabled
                ? DwarfFortressAdapterType.DfHackProcess
                : DwarfFortressAdapterType.Fake;
        }

        var configuredAdapterType = AdapterType.Trim();

        foreach (var adapterType in Enum.GetValues<DwarfFortressAdapterType>())
        {
            if (string.Equals(adapterType.ToString(), configuredAdapterType, StringComparison.OrdinalIgnoreCase))
            {
                return adapterType;
            }
        }

        throw new ArgumentException(
            "The configured dwarf adapter type must be one of: Fake, JsonFile, DfHackProcess.",
            nameof(AdapterType));
    }
}