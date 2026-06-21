namespace FortressSouls.Tests;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using FortressSouls.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

[Collection("ConsoleOutputSerial")]
public class ObservabilityTests
{
    private const string ConsoleFallbackActivitySentinel = "test.observability.console-fallback.activity.sentinel";
    private const string ConsoleFallbackStartupSentinel = "TestObservabilityConsoleFallbackStartupSentinel";
    private const string OtlpActivitySentinel = "test.observability.otlp.activity.sentinel";
    private const string OtlpStartupSentinel = "TestObservabilityOtlpStartupSentinel";
    private const string OtlpUnavailableActivitySentinel = "test.observability.otlp-unavailable.activity.sentinel";
    private const string OtlpUnavailableStartupSentinel = "TestObservabilityOtlpUnavailableStartupSentinel";
    private const string MetricStartupSentinel = "TestObservabilityMetricStartupSentinel";

    [Fact]
    public void ObservabilityHealthStateDoesNotExposeEndpointOrSecrets()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://localhost:4317",
                ["OTEL_EXPORTER_OTLP_HEADERS"] = "Authorization=secret"
            })
            .Build();

        // Act
        var healthState = ObservabilityConfiguration.GetHealthState(configuration);

        // Assert
        Assert.Equal(FortressSoulsTelemetry.OtlpConfiguredObservabilityState, healthState);
        Assert.DoesNotContain("http://localhost:4317", healthState, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", healthState, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://localhost:4317")]
    [InlineData("https://127.0.0.1:4318")]
    [InlineData("http://[::1]:4317")]
    public void TryGetOtlpEndpoint_AcceptsLoopbackEndpointsWithinLocalDevBoundary(string rawEndpoint)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = rawEndpoint,
            })
            .Build();

        var isConfigured = ObservabilityConfiguration.TryGetOtlpEndpoint(configuration, out var endpoint);
        var healthState = ObservabilityConfiguration.GetHealthState(configuration);

        Assert.True(isConfigured);
        Assert.NotNull(endpoint);
        Assert.Equal(FortressSoulsTelemetry.OtlpConfiguredObservabilityState, healthState);
    }

    [Theory]
    [InlineData("http://10.0.0.5:4317")]
    [InlineData("ftp://localhost:4317")]
    [InlineData("http://user:secret@localhost:4317")]
    [InlineData("http://localhost:4317?authorization=secret")]
    [InlineData("https://localhost:4317#dashboard")]
    public void TryGetOtlpEndpoint_RejectsEndpointsOutsideLocalDevBoundary(string rawEndpoint)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = rawEndpoint,
            })
            .Build();

        var isConfigured = ObservabilityConfiguration.TryGetOtlpEndpoint(configuration, out var endpoint);
        var healthState = ObservabilityConfiguration.GetHealthState(configuration);

        Assert.False(isConfigured);
        Assert.Null(endpoint);
        Assert.Equal(FortressSoulsTelemetry.ConsoleFallbackObservabilityState, healthState);
    }

    [Fact]
    public void AddFortressSoulsObservability_WithoutOtlp_ExportsFortressSoulsTracingAndMetrics()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddFortressSoulsObservability(configuration, new TestHostEnvironment());
        var output = CaptureConsoleOutput(() =>
        {
            using var provider = services.BuildServiceProvider();
            var tracerProvider = provider.GetRequiredService<TracerProvider>();
            var meterProvider = provider.GetRequiredService<MeterProvider>();

            using (var activity = FortressSoulsTelemetry.ActivitySource.StartActivity(ConsoleFallbackActivitySentinel))
            {
                activity?.SetTag(
                    FortressSoulsTelemetry.OperationOutcomeTagName,
                    ConsoleFallbackActivitySentinel);
            }

            FortressSoulsTelemetry.RecordStartup(ConsoleFallbackStartupSentinel);

            Assert.True(tracerProvider.ForceFlush());
            Assert.True(meterProvider.ForceFlush());
        });

        Assert.Contains(ConsoleFallbackActivitySentinel, output, StringComparison.Ordinal);
        Assert.Contains(ConsoleFallbackStartupSentinel, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddFortressSoulsObservability_WithOtlp_AlsoExportsFortressSoulsTracingAndMetricsToConsole()
    {
        await using var otlpProbe = new OtlpConnectionProbe();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlpProbe.Endpoint.ToString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFortressSoulsObservability(configuration, new TestHostEnvironment());
        var output = await CaptureConsoleOutputAsync(async () =>
        {
            using var provider = services.BuildServiceProvider();
            var tracerProvider = provider.GetRequiredService<TracerProvider>();
            var meterProvider = provider.GetRequiredService<MeterProvider>();

            using (var activity = FortressSoulsTelemetry.ActivitySource.StartActivity(OtlpActivitySentinel))
            {
                activity?.SetTag(
                    FortressSoulsTelemetry.OperationOutcomeTagName,
                    OtlpActivitySentinel);
            }

            FortressSoulsTelemetry.RecordStartup(OtlpStartupSentinel);

            _ = tracerProvider.ForceFlush();
            _ = meterProvider.ForceFlush();

            await otlpProbe.WaitForConnectionAsync();
        });

        Assert.Contains(OtlpActivitySentinel, output, StringComparison.Ordinal);
        Assert.Contains(OtlpStartupSentinel, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddFortressSoulsObservability_WithUnavailableOtlp_DoesNotThrowAndStillExportsFortressSoulsTelemetryToConsole()
    {
        await using var otlpFailureProbe = new OtlpDisconnectingProbe();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlpFailureProbe.Endpoint.ToString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFortressSoulsObservability(configuration, new TestHostEnvironment());

        var output = string.Empty;
        var exception = await Record.ExceptionAsync(async () =>
        {
            output = await CaptureConsoleOutputAsync(async () =>
            {
                using var provider = services.BuildServiceProvider();
                var tracerProvider = provider.GetRequiredService<TracerProvider>();
                var meterProvider = provider.GetRequiredService<MeterProvider>();

                using (var activity = FortressSoulsTelemetry.ActivitySource.StartActivity(OtlpUnavailableActivitySentinel))
                {
                    activity?.SetTag(
                        FortressSoulsTelemetry.OperationOutcomeTagName,
                        OtlpUnavailableActivitySentinel);
                }

                FortressSoulsTelemetry.RecordStartup(OtlpUnavailableStartupSentinel);

                _ = tracerProvider.ForceFlush();
                _ = meterProvider.ForceFlush();

                await otlpFailureProbe.WaitForConnectionAsync();
            });
        });

        Assert.Null(exception);
        Assert.Contains(OtlpUnavailableActivitySentinel, output, StringComparison.Ordinal);
        Assert.Contains(OtlpUnavailableStartupSentinel, output, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupMetricRecordsBoundedObservabilityTag()
    {
        // Arrange
        var measurements = new List<(long Measurement, IReadOnlyList<KeyValuePair<string, object?>> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FortressSoulsTelemetry.MeterName
                && instrument.Name == FortressSoulsTelemetry.StartupCounterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var recordedTags = tags.ToArray();
            if (recordedTags.Any(tag => tag.Key == FortressSoulsTelemetry.ObservabilityStateTagName
                && string.Equals(tag.Value as string, MetricStartupSentinel, StringComparison.Ordinal)))
            {
                measurements.Add((measurement, recordedTags));
            }
        });

        listener.Start();

        // Act
        FortressSoulsTelemetry.RecordStartup(MetricStartupSentinel);

        // Assert
        Assert.Single(measurements);
        Assert.Equal(1, measurements[0].Measurement);
        var tag = Assert.Single(measurements[0].Tags);
        Assert.Equal(FortressSoulsTelemetry.ObservabilityStateTagName, tag.Key);
        Assert.Equal(MetricStartupSentinel, tag.Value);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "FortressSouls.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string CaptureConsoleOutput(Action action) =>
        CaptureConsoleOutputAsync(() =>
        {
            action();
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

    private static async Task<string> CaptureConsoleOutputAsync(Func<Task> action)
    {
        var originalOutput = Console.Out;
        using var writer = new StringWriter();
        var synchronizedWriter = TextWriter.Synchronized(writer);

        Console.SetOut(synchronizedWriter);

        try
        {
            await action();
            synchronizedWriter.Flush();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    private sealed class OtlpConnectionProbe : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly Task<TcpClient> acceptTask;
        private readonly TcpListener listener;

        public OtlpConnectionProbe()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            acceptTask = listener.AcceptTcpClientAsync(cancellationTokenSource.Token).AsTask();

            var localEndpoint = (IPEndPoint)listener.LocalEndpoint;
            Endpoint = new Uri($"http://127.0.0.1:{localEndpoint.Port}");
        }

        public Uri Endpoint { get; }

        public async Task WaitForConnectionAsync()
        {
            using var client = await acceptTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            cancellationTokenSource.Cancel();
            listener.Stop();

            try
            {
                if (acceptTask.IsCompletedSuccessfully)
                {
                    acceptTask.Result.Dispose();
                }
                else
                {
                    using var client = await acceptTask;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            finally
            {
                cancellationTokenSource.Dispose();
            }
        }
    }

    private sealed class OtlpDisconnectingProbe : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly Task acceptLoopTask;
        private readonly TaskCompletionSource connectionReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TcpListener listener;

        public OtlpDisconnectingProbe()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            acceptLoopTask = Task.Run(AcceptLoopAsync);

            var localEndpoint = (IPEndPoint)listener.LocalEndpoint;
            Endpoint = new Uri($"http://127.0.0.1:{localEndpoint.Port}");
        }

        public Uri Endpoint { get; }

        public Task WaitForConnectionAsync() =>
            connectionReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public async ValueTask DisposeAsync()
        {
            cancellationTokenSource.Cancel();
            listener.Stop();

            try
            {
                await acceptLoopTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (SocketException)
            {
            }
            finally
            {
                cancellationTokenSource.Dispose();
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                TcpClient? client = null;

                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationTokenSource.Token);
                    connectionReceived.TrySetResult();
                    client.Client.LingerState = new LingerOption(true, 0);
                }
                catch (OperationCanceledException)
                {
                    connectionReceived.TrySetCanceled(cancellationTokenSource.Token);
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException) when (cancellationTokenSource.IsCancellationRequested)
                {
                    break;
                }
                finally
                {
                    client?.Dispose();
                }
            }
        }
    }
}
