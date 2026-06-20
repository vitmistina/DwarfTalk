namespace FortressSouls.Application;

using System.Diagnostics;
using FortressSouls.Domain;
using FortressSouls.Observability;

public sealed class DwarfQueryService(
    IDwarfFortressAdapter adapter,
    DwarfAdapterDescriptor adapterDescriptor)
{
    private readonly IDwarfFortressAdapter _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    private readonly DwarfAdapterDescriptor _adapterDescriptor = adapterDescriptor ?? throw new ArgumentNullException(nameof(adapterDescriptor));

    public async Task<DwarfListQueryResult> ListDwarvesAsync(CancellationToken cancellationToken)
    {
        using var activity = FortressSoulsTelemetry.ActivitySource.StartActivity(
            FortressSoulsTelemetry.DwarvesListActivityName,
            ActivityKind.Internal);

        activity?.SetTag(FortressSoulsTelemetry.AdapterTypeTagName, _adapterDescriptor.AdapterType);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var list = await _adapter.ListDwarvesAsync(cancellationToken);
            stopwatch.Stop();

            activity?.SetTag(FortressSoulsTelemetry.SnapshotSchemaVersionTagName, list.SchemaVersion);
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.SuccessOutcome);
            FortressSoulsTelemetry.RecordDwarfListDuration(
                stopwatch.Elapsed.TotalMilliseconds,
                _adapterDescriptor.AdapterType,
                list.SchemaVersion,
                FortressSoulsTelemetry.SuccessOutcome);

            return new DwarfListQueryResult(_adapterDescriptor.AdapterType, list, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.CancelledOutcome);
            FortressSoulsTelemetry.RecordDwarfListDuration(
                stopwatch.Elapsed.TotalMilliseconds,
                _adapterDescriptor.AdapterType,
                "unknown",
                FortressSoulsTelemetry.CancelledOutcome);
            throw;
        }
        catch (Exception exception) when (exception is DwarfFortressDataException or DwarfNotFoundException)
        {
            stopwatch.Stop();
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, MapOutcome(exception));
            FortressSoulsTelemetry.RecordDwarfListDuration(
                stopwatch.Elapsed.TotalMilliseconds,
                _adapterDescriptor.AdapterType,
                "unknown",
                MapOutcome(exception));
            throw;
        }
    }

    public async Task<DwarfSnapshotQueryResult> GetDwarfSnapshotAsync(DwarfId dwarfId, CancellationToken cancellationToken)
    {
        using var activity = FortressSoulsTelemetry.ActivitySource.StartActivity(
            FortressSoulsTelemetry.DwarvesSnapshotActivityName,
            ActivityKind.Internal);

        activity?.SetTag(FortressSoulsTelemetry.AdapterTypeTagName, _adapterDescriptor.AdapterType);
        activity?.SetTag(FortressSoulsTelemetry.DwarfIdTagName, dwarfId.ToString());

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var snapshot = await _adapter.GetDwarfSnapshotAsync(dwarfId, cancellationToken);
            stopwatch.Stop();

            activity?.SetTag(FortressSoulsTelemetry.SnapshotSchemaVersionTagName, snapshot.SchemaVersion);
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.SuccessOutcome);
            FortressSoulsTelemetry.RecordDwarfSnapshotDuration(
                stopwatch.Elapsed.TotalMilliseconds,
                _adapterDescriptor.AdapterType,
                snapshot.SchemaVersion,
                FortressSoulsTelemetry.SuccessOutcome);

            return new DwarfSnapshotQueryResult(_adapterDescriptor.AdapterType, snapshot, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, FortressSoulsTelemetry.CancelledOutcome);
            FortressSoulsTelemetry.RecordDwarfSnapshotDuration(
                stopwatch.Elapsed.TotalMilliseconds,
                _adapterDescriptor.AdapterType,
                "unknown",
                FortressSoulsTelemetry.CancelledOutcome);
            throw;
        }
        catch (Exception exception) when (exception is DwarfFortressDataException or DwarfNotFoundException)
        {
            stopwatch.Stop();
            activity?.SetTag(FortressSoulsTelemetry.OperationOutcomeTagName, MapOutcome(exception));
            FortressSoulsTelemetry.RecordDwarfSnapshotDuration(
                stopwatch.Elapsed.TotalMilliseconds,
                _adapterDescriptor.AdapterType,
                "unknown",
                MapOutcome(exception));
            throw;
        }
    }

    private static string MapOutcome(Exception exception) =>
        exception switch
        {
            DwarfNotFoundException => FortressSoulsTelemetry.NotFoundOutcome,
            DwarfFortressDataException => FortressSoulsTelemetry.ErrorOutcome,
            _ => FortressSoulsTelemetry.ErrorOutcome
        };
}
