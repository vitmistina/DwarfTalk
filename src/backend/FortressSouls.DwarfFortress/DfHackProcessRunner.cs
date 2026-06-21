namespace FortressSouls.DwarfFortress;

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

public sealed class DfHackProcessRunner(
    DfHackProcessAdapterOptions options,
    IDfHackTcpPreflight preflight) : IDfHackProcessRunner
{
    private readonly DfHackProcessAdapterOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly IDfHackTcpPreflight _preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));

    public async Task<DfHackProcessCommandResult> RunCommandAsync(
        DfHackCommand command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var stopwatch = Stopwatch.StartNew();
        if (command.RequiresPreflight() && !await _preflight.IsReachableAsync(cancellationToken))
        {
            stopwatch.Stop();
            return DfHackProcessCommandResult.Failure(
                command,
                DfHackProcessFailureCategory.Unavailable,
                stdout: null,
                stderr: null,
                exitCode: null,
                duration: stopwatch.Elapsed);
        }

        using var process = new Process();
        process.StartInfo = CreateStartInfo(command, arguments);

        try
        {
            if (!process.Start())
            {
                stopwatch.Stop();
                return DfHackProcessCommandResult.Failure(
                    command,
                    DfHackProcessFailureCategory.ExecutableUnavailable,
                    stdout: null,
                    stderr: null,
                    exitCode: null,
                    duration: stopwatch.Elapsed);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or FileNotFoundException or DirectoryNotFoundException)
        {
            stopwatch.Stop();
            return DfHackProcessCommandResult.Failure(
                command,
                DfHackProcessFailureCategory.ExecutableUnavailable,
                stdout: null,
                stderr: null,
                exitCode: null,
                duration: stopwatch.Elapsed);
        }

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, _options.MaxStdoutBytes, CancellationToken.None);
        var stderrTask = ReadBoundedAsync(process.StandardError, _options.MaxStderrBytes, CancellationToken.None);

        bool timedOut = false;
        bool cancelled = false;

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(_options.TimeoutMs);

        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
        }

        if (timedOut || cancelled)
        {
            TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        stopwatch.Stop();

        if (cancelled)
        {
            return DfHackProcessCommandResult.Failure(
                command,
                DfHackProcessFailureCategory.Cancelled,
                stdout.Value,
                stderr.Value,
                process.HasExited ? process.ExitCode : null,
                stopwatch.Elapsed);
        }

        if (timedOut)
        {
            return DfHackProcessCommandResult.Failure(
                command,
                DfHackProcessFailureCategory.Timeout,
                stdout.Value,
                stderr.Value,
                process.HasExited ? process.ExitCode : null,
                stopwatch.Elapsed);
        }

        if (stdout.IsLimitExceeded || stderr.IsLimitExceeded)
        {
            return DfHackProcessCommandResult.Failure(
                command,
                DfHackProcessFailureCategory.OutputTooLarge,
                stdout.Value,
                stderr.Value,
                process.ExitCode,
                stopwatch.Elapsed);
        }

        if (process.ExitCode < 0)
        {
            return DfHackProcessCommandResult.Failure(
                command,
                DfHackProcessFailureCategory.Crashed,
                stdout.Value,
                stderr.Value,
                process.ExitCode,
                stopwatch.Elapsed);
        }

        if (process.ExitCode != 0)
        {
            return DfHackProcessCommandResult.Failure(
                command,
                DfHackProcessFailureCategory.Failed,
                stdout.Value,
                stderr.Value,
                process.ExitCode,
                stopwatch.Elapsed);
        }

        return DfHackProcessCommandResult.Success(
            command,
            stdout.Value,
            stderr.Value,
            process.ExitCode,
            stopwatch.Elapsed);
    }

    private ProcessStartInfo CreateStartInfo(DfHackCommand command, IReadOnlyList<string> arguments)
    {
        var outputEncoding = GetRedirectedStreamEncoding();
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.RunPath,
            WorkingDirectory = _options.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add(command.ToCommandName());
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static Encoding GetRedirectedStreamEncoding()
    {
        if (OperatingSystem.IsWindows())
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private static async Task<BoundedReadResult> ReadBoundedAsync(StreamReader reader, int limitBytes, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder();
        var writtenBytes = 0;
        var isLimitExceeded = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            var chunk = buffer.AsSpan(0, read);
            var chunkBytes = Encoding.UTF8.GetByteCount(chunk);

            if (!isLimitExceeded)
            {
                if (writtenBytes + chunkBytes > limitBytes)
                {
                    isLimitExceeded = true;
                }
                else
                {
                    builder.Append(chunk);
                    writtenBytes += chunkBytes;
                }
            }
        }

        return new BoundedReadResult(builder.ToString(), isLimitExceeded);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private sealed record BoundedReadResult(string Value, bool IsLimitExceeded);
}
