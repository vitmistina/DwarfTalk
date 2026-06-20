using FortressSouls.Application;
using FortressSouls.Api;
using FortressSouls.Observability;
using FortressSouls.DwarfFortress;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "O";
});

builder.Services.AddFortressSoulsObservability(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<IDwarfFortressAdapter, FakeDwarfFortressAdapter>();
builder.Services.AddSingleton(new DwarfAdapterDescriptor("Fake"));
builder.Services.AddScoped<DwarfQueryService>();

var app = builder.Build();

app.UseFortressSoulsCorrelationId();

var observabilityState = ObservabilityConfiguration.GetHealthState(builder.Configuration);
var adapterDescriptor = app.Services.GetRequiredService<DwarfAdapterDescriptor>();
app.Logger.LogInformation(
    "Fortress Souls API starting with observability {ObservabilityState} and adapter {AdapterType}",
    observabilityState,
    adapterDescriptor.AdapterType);

app.Lifetime.ApplicationStarted.Register(() =>
{
    FortressSoulsTelemetry.RecordStartup(observabilityState);
});

app.MapGet("/api/health", () => HealthResponse.CreateBasic(observabilityState, adapterDescriptor.AdapterType))
    .WithName("Health")
    .Produces<HealthResponse>();
app.MapDwarfEndpoints();

app.Run();
