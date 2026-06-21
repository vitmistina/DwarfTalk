namespace FortressSouls.Observability;

using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.Extensions.Configuration;

public static class ObservabilityConfiguration
{
    private const string OtlpEndpointKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    public static string GetHealthState(IConfiguration configuration) =>
        TryGetOtlpEndpoint(configuration, out _)
            ? FortressSoulsTelemetry.OtlpConfiguredObservabilityState
            : FortressSoulsTelemetry.ConsoleFallbackObservabilityState;

    public static bool TryGetOtlpEndpoint(IConfiguration configuration, [NotNullWhen(true)] out Uri? endpoint)
    {
        var rawEndpoint = configuration[OtlpEndpointKey];
        if (string.IsNullOrWhiteSpace(rawEndpoint))
        {
            endpoint = null;
            return false;
        }

        if (Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var configuredEndpoint)
            && IsAcceptedLocalOtlpEndpoint(configuredEndpoint))
        {
            endpoint = configuredEndpoint;
            return true;
        }

        endpoint = null;
        return false;
    }

    private static bool IsAcceptedLocalOtlpEndpoint(Uri endpoint)
    {
        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsLoopbackHost(endpoint.DnsSafeHost)
            && string.IsNullOrEmpty(endpoint.UserInfo)
            && string.IsNullOrEmpty(endpoint.Query)
            && string.IsNullOrEmpty(endpoint.Fragment);
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
