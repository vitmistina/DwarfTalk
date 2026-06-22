using FortressSouls.Application;
using FortressSouls.Api;
using FortressSouls.Observability;
using FortressSouls.DwarfFortress;
using FortressSouls.Llm;
using FortressSouls.Prompting;

var builder = WebApplication.CreateBuilder(args);
var llmOptions = LlmProviderOptions.LoadAndValidate(builder.Configuration);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "O";
});

builder.Services.AddFortressSoulsObservability(builder.Configuration, builder.Environment);
builder.Services.AddFortressSoulsDwarfFortress(builder.Configuration);
builder.Services.AddSingleton(ChatSessionOptions.Default);
builder.Services.AddSingleton<IChatSessionStore, InMemoryChatSessionStore>();
builder.Services.AddFortressSoulsLlm(builder.Configuration);
builder.Services.AddSingleton<PromptAssembler>();
builder.Services.AddScoped<DwarfQueryService>();

if (llmOptions.ProviderType == LlmProviderType.Fake)
{
    builder.Services.AddFortressSoulsFakePerceptionAgent();
}

builder.Services.AddScoped<ChatSessionService>();

var app = builder.Build();

app.UseFortressSoulsCorrelationId();

var observabilityState = ObservabilityConfiguration.GetHealthState(builder.Configuration);
var adapterDescriptor = app.Services.GetRequiredService<DwarfAdapterDescriptor>();
var providerStatusReader = app.Services.GetRequiredService<IChatProviderStatusReader>();
app.Logger.LogInformation(
    "Fortress Souls API starting with observability {ObservabilityState}, adapter {AdapterType}, and provider {ProviderType}",
    observabilityState,
    adapterDescriptor.AdapterType,
    providerStatusReader.GetCurrentStatus().ProviderType);

app.Lifetime.ApplicationStarted.Register(() =>
{
    FortressSoulsTelemetry.RecordStartup(observabilityState);
});

app.MapGet(
        "/api/health",
        (IChatProviderStatusReader providerStatus) => HealthResponse.CreateBasic(observabilityState, adapterDescriptor.AdapterType, providerStatus.GetCurrentStatus().ProviderType))
    .WithName("Health")
    .Produces<HealthResponse>();
app.MapProviderStatusEndpoints();
app.MapDwarfAdapterStatusEndpoints();
app.MapDwarfEndpoints();
app.MapChatEndpoints(builder.Environment.IsDevelopment());

app.Run();
