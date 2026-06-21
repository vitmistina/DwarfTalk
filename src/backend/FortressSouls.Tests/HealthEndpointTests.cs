namespace FortressSouls.Tests;

using FortressSouls.Application;
using FortressSouls.Observability;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

/// <summary>
/// Integration tests for the health endpoint.
/// </summary>
[Collection("ConsoleOutputSerial")]
public class HealthEndpointTests
{
    [Fact]
    public async Task HealthEndpointGeneratesCorrelationIdWhenMissing()
    {
        using var factory = CreateConsoleFallbackFactory();
        using var client = factory.CreateClient();
        var uri = "/api/health";

        var response = await client.GetAsync(uri);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(FortressSoulsTelemetry.CorrelationHeaderName, out var values));

        var correlationId = Assert.Single(values);
        Assert.True(IsSafeCorrelationId(correlationId));
    }

    [Fact]
    public async Task HealthEndpointPreservesValidCorrelationId()
    {
        using var factory = CreateConsoleFallbackFactory();
        using var client = factory.CreateClient();
        var uri = "/api/health";
        var expectedCorrelationId = "trace-123_abc";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add(FortressSoulsTelemetry.CorrelationHeaderName, expectedCorrelationId);

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(FortressSoulsTelemetry.CorrelationHeaderName, out var values));
        Assert.Equal(expectedCorrelationId, Assert.Single(values));
    }

    [Fact]
    public async Task HealthEndpointReplacesInvalidCorrelationId()
    {
        using var factory = CreateConsoleFallbackFactory();
        using var client = factory.CreateClient();
        var uri = "/api/health";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add(FortressSoulsTelemetry.CorrelationHeaderName, new string('a', 65));

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(FortressSoulsTelemetry.CorrelationHeaderName, out var values));

        var correlationId = Assert.Single(values);
        Assert.NotEqual(new string('a', 65), correlationId);
        Assert.True(IsSafeCorrelationId(correlationId));
    }

    [Fact]
    public async Task HealthEndpointProducesHttpTraceDataForRealRequest()
    {
        const string uri = "/api/health";

        using var factory = CreateConsoleFallbackFactory();
        using var client = factory.CreateClient();

        using (var warmupResponse = await client.GetAsync(uri))
        {
            Assert.Equal(System.Net.HttpStatusCode.OK, warmupResponse.StatusCode);
        }

        HttpResponseMessage? response = null;
        var output = await CaptureConsoleOutputAsync(async () =>
        {
            response = await client.GetAsync(uri);
        });

        using (response)
        {
            Assert.NotNull(response);
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Contains("http.request.method: GET", output, StringComparison.Ordinal);
        Assert.True(
            output.Contains("http.route: /api/health", StringComparison.Ordinal)
                || output.Contains("Activity.DisplayName:        GET /api/health", StringComparison.Ordinal),
            $"Expected HTTP route or display name for the captured health request.{Environment.NewLine}{output}");
    }

    [Fact]
    public async Task HealthEndpointReturnsExpectedContract()
    {
        using var factory = CreateConsoleFallbackFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var content = await response.Content.ReadAsStringAsync();
        var health = System.Text.Json.JsonSerializer.Deserialize<HealthResponse>(
            content,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(health);
        Assert.Equal("ok", health.Status);
        Assert.Equal("0.1.0", health.Version);
        Assert.NotNull(health.Adapter);
        Assert.NotNull(health.Provider);
        Assert.Equal(FortressSoulsTelemetry.ConsoleFallbackObservabilityState, health.Observability);
    }

    [Fact]
    public async Task HealthEndpointReturnsOtlpConfiguredContractWithoutEndpointLeak()
    {
        const string otlpEndpoint = "http://127.0.0.1:4317";

        using var factory = CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlpEndpoint
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var content = await response.Content.ReadAsStringAsync();
        var health = System.Text.Json.JsonSerializer.Deserialize<HealthResponse>(
            content,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(health);
        Assert.Equal(FortressSoulsTelemetry.OtlpConfiguredObservabilityState, health.Observability);
        Assert.DoesNotContain(otlpEndpoint, content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthEndpointReturnsConsoleFallback_WhenOtlpEndpointIsNonLoopback()
    {
        using var factory = CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://10.0.0.5:4317"
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var content = await response.Content.ReadAsStringAsync();
        var health = System.Text.Json.JsonSerializer.Deserialize<HealthResponse>(
            content,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(health);
        Assert.Equal(FortressSoulsTelemetry.ConsoleFallbackObservabilityState, health.Observability);
    }

    private static bool IsSafeCorrelationId(string value) =>
        value.Length is > 0 and <= 64
        && Regex.IsMatch(value, "^[A-Za-z0-9_.-]+$");

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

    private static WebApplicationFactory<Program> CreateConsoleFallbackFactory() =>
        CreateFactoryWithConfiguration(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty,
        });

    private static WebApplicationFactory<Program> CreateFactoryWithConfiguration(IDictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            });
}
