namespace FortressSouls.Observability;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddFortressSoulsObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddSingleton(FortressSoulsTelemetry.ActivitySource)
            .AddSingleton(FortressSoulsTelemetry.Meter);

        var useOtlpExporter = ObservabilityConfiguration.TryGetOtlpEndpoint(configuration, out var otlpEndpoint);

        services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .AddSource(FortressSoulsTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddConsoleExporter();

                if (useOtlpExporter)
                {
                    builder.AddOtlpExporter(options => options.Endpoint = otlpEndpoint!);
                }
            })
            .WithMetrics(builder =>
            {
                builder
                    .AddMeter(FortressSoulsTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddConsoleExporter();

                if (useOtlpExporter)
                {
                    builder.AddOtlpExporter(options => options.Endpoint = otlpEndpoint!);
                }
            });

        return services;
    }
}
