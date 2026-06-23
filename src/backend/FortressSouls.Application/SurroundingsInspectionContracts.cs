namespace FortressSouls.Application;

using FortressSouls.Domain;

public interface ISurroundingsInspectionService
{
    Task<LookAroundToolResult> InspectAroundAsync(
        DwarfId observerDwarfId,
        int requestedRadius,
        CancellationToken cancellationToken);
}
