namespace FortressSouls.DwarfFortress;

using FortressSouls.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class DwarfFortressServiceCollectionExtensions
{
    public static IServiceCollection AddFortressSoulsDwarfFortress(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var dfHackOptions = configuration
            .GetSection(DfHackProcessAdapterOptions.ConfigurationSectionPath)
            .Get<DfHackProcessAdapterOptions>() ?? new DfHackProcessAdapterOptions();

        var adapterOptions = configuration
            .GetSection(DwarfFortressAdapterOptions.ConfigurationSectionPath)
            .Get<DwarfFortressAdapterOptions>() ?? new DwarfFortressAdapterOptions();

        var adapterType = adapterOptions.ResolveAdapterType(dfHackOptions.Enabled);

        return adapterType switch
        {
            DwarfFortressAdapterType.Fake => AddFakeAdapter(services),
            DwarfFortressAdapterType.JsonFile => AddJsonFileAdapter(services, adapterOptions.JsonFile.Validate()),
            DwarfFortressAdapterType.DfHackProcess => AddDfHackProcessAdapter(services, EnableAndValidateDfHackOptions(dfHackOptions)),
            _ => throw new ArgumentOutOfRangeException(nameof(adapterType), adapterType, "Unsupported dwarf adapter type.")
        };
    }

    private static IServiceCollection AddFakeAdapter(IServiceCollection services)
    {
        services.AddSingleton<IDwarfFortressAdapter, FakeDwarfFortressAdapter>();
        services.AddSingleton(new DwarfAdapterDescriptor(DwarfFortressAdapterType.Fake.ToString()));
        services.AddSingleton<IDwarfAdapterStatusReader>(new StaticDwarfAdapterStatusReader(DwarfFortressAdapterType.Fake.ToString()));
        return services;
    }

    private static IServiceCollection AddJsonFileAdapter(
        IServiceCollection services,
        JsonFileDwarfFortressAdapterOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<JsonFileDwarfFortressAdapter>();
        services.AddSingleton<IDwarfFortressAdapter>(sp => sp.GetRequiredService<JsonFileDwarfFortressAdapter>());
        services.AddSingleton(new DwarfAdapterDescriptor(DwarfFortressAdapterType.JsonFile.ToString()));
        services.AddSingleton<IDwarfAdapterStatusReader>(new StaticDwarfAdapterStatusReader(DwarfFortressAdapterType.JsonFile.ToString()));
        return services;
    }

    private static IServiceCollection AddDfHackProcessAdapter(
        IServiceCollection services,
        DfHackProcessAdapterOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton(new DfHackAdapterStatusTracker(enabled: true));

        services.AddSingleton<IDfHackTcpPreflight, TcpDfHackPreflight>();
        services.AddSingleton<IDfHackProcessRunner, DfHackProcessRunner>();
        services.AddSingleton<IDfHackAdapterStatusRecorder>(sp => sp.GetRequiredService<DfHackAdapterStatusTracker>());
        services.AddSingleton<IDwarfAdapterStatusReader>(sp => sp.GetRequiredService<DfHackAdapterStatusTracker>());
        services.AddSingleton<IDwarfFortressAdapter, DfHackDwarfFortressAdapter>();
        services.AddSingleton(new DwarfAdapterDescriptor(DwarfFortressAdapterType.DfHackProcess.ToString()));
        return services;
    }

    private static DfHackProcessAdapterOptions EnableAndValidateDfHackOptions(DfHackProcessAdapterOptions options)
    {
        options.Enabled = true;
        return options.Validate();
    }
}
