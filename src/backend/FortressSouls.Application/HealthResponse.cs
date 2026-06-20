namespace FortressSouls.Application;

/// <summary>
/// Health status response for the v0.1 API.
/// </summary>
public sealed record HealthResponse(
    string Status,
    string Version,
    string Adapter,
    string Provider,
    string Observability)
{
    /// <summary>
    /// Create a basic health response with default adapter and provider status.
    /// </summary>
    public static HealthResponse CreateBasic(
        string observability = "ConsoleFallback",
        string adapter = "NotConfigured",
        string provider = "NotConfigured") =>
        new(
            Status: "ok",
            Version: "0.1.0",
            Adapter: adapter,
            Provider: provider,
            Observability: observability);
}
