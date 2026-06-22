namespace FortressSouls.Application;

using System.Text.Json;
using System.Threading;

public sealed record ProbeObservationResult(
    string SchemaVersion,
    string Summary);

public sealed class ProbeObservationToolService
{
    public const string StableToolName = "probe_observe";
    public static readonly AgentToolDefinition StableDefinition = new(
        StableToolName,
        "Return a deterministic observation for the B2-001 contract harness.");

    private int _invocationCount;

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public AgentToolRegistration CreateRegistration() => new(StableDefinition, ExecuteAsync, ValidateInvocation);

    public async Task<AgentToolResult> ExecuteAsync(AgentToolInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        var arguments = ParseAndValidateArguments(invocation);

        var result = await ObserveValidatedAsync(
            arguments.Subject,
            arguments.RepeatCount,
            arguments.EmitLargePayload,
            arguments.DelayMs,
            cancellationToken);

        return AgentToolResult.Create(invocation.Tool, result);
    }

    private static void ValidateInvocation(AgentToolInvocation invocation)
    {
        _ = ParseAndValidateArguments(invocation);
    }

    public async Task<ProbeObservationResult> ObserveAsync(
        string subject,
        int repeatCount,
        bool emitLargePayload,
        int delayMs,
        CancellationToken cancellationToken)
    {
        var arguments = ValidateArguments(new ProbeObservationArguments(subject, repeatCount, emitLargePayload, delayMs));

        return await ObserveValidatedAsync(
            arguments.Subject,
            arguments.RepeatCount,
            arguments.EmitLargePayload,
            arguments.DelayMs,
            cancellationToken);
    }

    private async Task<ProbeObservationResult> ObserveValidatedAsync(
        string subject,
        int repeatCount,
        bool emitLargePayload,
        int delayMs,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _invocationCount);

        if (delayMs > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
        }

        var summary = emitLargePayload
            ? new string('x', 4_096)
            : string.Join(' ', Enumerable.Repeat(subject, repeatCount));

        return new ProbeObservationResult("probe.v1", summary);
    }

    private static ProbeObservationArguments ParseAndValidateArguments(AgentToolInvocation invocation) =>
        ValidateArguments(ParseArguments(invocation));

    private static ProbeObservationArguments ValidateArguments(ProbeObservationArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        ValidateRepeatCount(arguments.RepeatCount);
        ValidateDelayMs(arguments.DelayMs);

        return arguments with
        {
            Subject = NormalizeSubject(arguments.Subject)
        };
    }

    private static string NormalizeSubject(string subject)
    {
        if (subject is null)
        {
            throw InvalidArguments();
        }

        var normalized = subject.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 200)
        {
            throw InvalidArguments();
        }

        return normalized;
    }

    private static void ValidateRepeatCount(int repeatCount)
    {
        if (repeatCount is < 1 or > 4)
        {
            throw InvalidArguments();
        }
    }

    private static void ValidateDelayMs(int delayMs)
    {
        if (delayMs is < 0 or > 10_000)
        {
            throw InvalidArguments();
        }
    }

    private static ProbeObservationArguments ParseArguments(AgentToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        return new ProbeObservationArguments(
            ReadRequiredString(invocation.Arguments, "subject"),
            ReadRequiredInt32(invocation.Arguments, "repeatCount"),
            ReadRequiredBoolean(invocation.Arguments, "emitLargePayload"),
            ReadRequiredInt32(invocation.Arguments, "delayMs"));
    }

    private static string ReadRequiredString(JsonElement arguments, string key)
    {
        if (!TryGetRequiredProperty(arguments, key, out var value))
        {
            throw InvalidArguments();
        }

        return value.ValueKind switch
        {
            JsonValueKind.String when value.GetString() is { } text => text,
            _ => throw InvalidArguments()
        };
    }

    private static int ReadRequiredInt32(JsonElement arguments, string key)
    {
        if (!TryGetRequiredProperty(arguments, key, out var value))
        {
            throw InvalidArguments();
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            _ => throw InvalidArguments()
        };
    }

    private static bool ReadRequiredBoolean(JsonElement arguments, string key)
    {
        if (!TryGetRequiredProperty(arguments, key, out var value))
        {
            throw InvalidArguments();
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw InvalidArguments()
        };
    }

    private static bool TryGetRequiredProperty(JsonElement arguments, string key, out JsonElement value)
    {
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(key, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static AgentTurnException InvalidArguments() =>
        new(AgentTurnErrorCode.InvalidArguments, "The agent tool arguments are invalid.");

    private sealed record ProbeObservationArguments(
        string Subject,
        int RepeatCount,
        bool EmitLargePayload,
        int DelayMs);
}