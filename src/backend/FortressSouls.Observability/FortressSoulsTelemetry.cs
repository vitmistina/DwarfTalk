namespace FortressSouls.Observability;

using System.Diagnostics;
using System.Diagnostics.Metrics;

public static class FortressSoulsTelemetry
{
    public const string ActivitySourceName = "FortressSouls";

    public const string MeterName = "FortressSouls";

    public const string CorrelationHeaderName = "X-Correlation-ID";

    public const string CorrelationIdTagName = "correlationId";

    public const string CorrelationIdFieldName = "correlationId";

    public const string SessionIdFieldName = "sessionId";

    public const string DwarfIdFieldName = "dwarfId";

    public const string SnapshotSchemaVersionFieldName = "snapshotSchemaVersion";

    public const string ProviderTypeFieldName = "providerType";

    public const string ModelFieldName = "model";

    public const string OperationFieldName = "operation";

    public const string DurationMsFieldName = "durationMs";

    public const string ErrorCodeFieldName = "errorCode";

    public const string DwarvesListActivityName = "fortresssouls.dwarves.list";

    public const string DwarvesSnapshotActivityName = "fortresssouls.dwarves.snapshot";

    public const string DwarvesListDurationMetricName = "fortresssouls.dwarves.list.duration";

    public const string DwarvesSnapshotDurationMetricName = "fortresssouls.dwarves.snapshot.duration";

    public const string AdapterTypeTagName = "fortresssouls.adapter.type";

    public const string ProviderTypeTagName = "fortresssouls.provider.type";

    public const string LlmModelTagName = "fortresssouls.llm.model";

    public const string DwarfIdTagName = "fortresssouls.dwarf.id";

    public const string SnapshotSchemaVersionTagName = "fortresssouls.snapshot.schema_version";

    public const string PromptTemplateVersionTagName = "fortresssouls.prompt.template_version";

    public const string ChatSessionIdTagName = "fortresssouls.chat.session_id";

    public const string ConsoleFallbackObservabilityState = "ConsoleFallback";

    public const string OtlpConfiguredObservabilityState = "OtlpConfigured";

    public const string StartupCounterName = "fortresssouls.api.startup.count";

    public const string ObservabilityStateTagName = "observability.state";

    public const string OperationOutcomeTagName = "fortresssouls.operation.outcome";

    public const string SuccessOutcome = "success";

    public const string CancelledOutcome = "cancelled";

    public const string NotFoundOutcome = "not_found";

    public const string ErrorOutcome = "error";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> StartupCounter = Meter.CreateCounter<long>(StartupCounterName);

    private static readonly Histogram<double> DwarvesListDuration = Meter.CreateHistogram<double>(
        DwarvesListDurationMetricName,
        unit: "ms");

    private static readonly Histogram<double> DwarvesSnapshotDuration = Meter.CreateHistogram<double>(
        DwarvesSnapshotDurationMetricName,
        unit: "ms");

    public static void RecordStartup(string observabilityState)
    {
        StartupCounter.Add(1, new TagList
        {
            { ObservabilityStateTagName, observabilityState }
        });
    }

    public static void RecordDwarfListDuration(
        double durationMs,
        string adapterType,
        string schemaVersion,
        string outcome)
    {
        DwarvesListDuration.Record(durationMs, new TagList
        {
            { AdapterTypeTagName, adapterType },
            { SnapshotSchemaVersionTagName, schemaVersion },
            { OperationOutcomeTagName, outcome }
        });
    }

    public static void RecordDwarfSnapshotDuration(
        double durationMs,
        string adapterType,
        string schemaVersion,
        string outcome)
    {
        DwarvesSnapshotDuration.Record(durationMs, new TagList
        {
            { AdapterTypeTagName, adapterType },
            { SnapshotSchemaVersionTagName, schemaVersion },
            { OperationOutcomeTagName, outcome }
        });
    }
}
