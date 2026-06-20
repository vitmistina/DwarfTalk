namespace FortressSouls.Application;

using FortressSouls.Domain;

public sealed class DwarfNotFoundException : Exception
{
    public DwarfNotFoundException(DwarfId dwarfId)
        : base("The requested dwarf was not found.")
    {
        DwarfId = dwarfId;
    }

    public DwarfId DwarfId { get; }
}
