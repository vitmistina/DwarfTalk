namespace FortressSouls.Llm;

using FortressSouls.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddFortressSoulsLlm(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = LlmProviderOptions.LoadAndValidate(configuration);
        services.AddSingleton(options);
        services.AddSingleton<ChatProviderStatusTracker>();
        services.AddSingleton<IChatProviderStatusReader>(sp => sp.GetRequiredService<ChatProviderStatusTracker>());
        services.AddSingleton<IChatProviderStatusRecorder>(sp => sp.GetRequiredService<ChatProviderStatusTracker>());
        services.AddSingleton<FakeChatProvider>();

        if (options.ProviderType == LlmProviderType.Fake)
        {
            services.AddSingleton<IChatProvider>(sp => sp.GetRequiredService<FakeChatProvider>());
            return services;
        }

        services.AddSingleton(sp =>
        {
            var providerOptions = sp.GetRequiredService<LlmProviderOptions>();
            return new HttpClient
            {
                BaseAddress = providerOptions.GetValidatedEndpointUri(),
                Timeout = Timeout.InfiniteTimeSpan
            };
        });
        services.AddSingleton<OpenAiCompatibleChatProvider>();
        services.AddSingleton<IChatProvider>(sp => sp.GetRequiredService<OpenAiCompatibleChatProvider>());
        return services;
    }

    public static IServiceCollection AddFortressSoulsFakePerceptionAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<Microsoft.Extensions.AI.IChatClient, FakeToolLoopChatClient>();
        services.AddScoped<IAgentToolRegistry>(sp =>
        {
            var queryService = sp.GetRequiredService<DwarfQueryService>();
            var toolService = new FakePerceptionToolService(
                queryService,
                sp.GetRequiredService<ISurroundingsInspectionService>(),
                sp.GetRequiredService<IStockInspectionService>(),
                FakePerceptionFixtureSet.Default);
            var enabledToolNames = new HashSet<string>(StringComparer.Ordinal)
            {
                FakePerceptionToolService.LookAroundToolName,
                FakePerceptionToolService.InspectStocksToolName,
                FakePerceptionToolService.ListDwarvesToolName,
                FakePerceptionToolService.InspectDwarfToolName
            };

            var registrations = toolService
                .CreateRegistrations()
                .Where(tool => enabledToolNames.Contains(tool.Definition.Name))
                .ToArray();

            return new ClosedAgentToolRegistry(registrations);
        });
        services.AddScoped<IDwarfAgent>(sp => new MicrosoftExtensionsAiDwarfAgent(
            sp.GetRequiredService<Microsoft.Extensions.AI.IChatClient>(),
            sp.GetRequiredService<IAgentToolRegistry>(),
            sp.GetRequiredService<LlmProviderOptions>()));

        return services;
    }
}
