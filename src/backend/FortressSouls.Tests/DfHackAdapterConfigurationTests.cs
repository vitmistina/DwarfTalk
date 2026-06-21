namespace FortressSouls.Tests;

using System.Net;
using System.Net.Http.Json;
using FortressSouls.Api;
using FortressSouls.Application;
using FortressSouls.Domain;
using FortressSouls.DwarfFortress;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[Collection("DfHackProcessSerial")]
public sealed class DfHackAdapterConfigurationTests
{
    [Fact]
    public void DfHackOptions_RejectsNonLoopbackAndInvalidTimeouts()
    {
        var invalidHost = new DfHackProcessAdapterOptions
        {
            Enabled = true,
            RunPath = "C:\\dfhack\\hack\\dfhack-run.exe",
            WorkingDirectory = "C:\\dfhack\\hack",
            Host = "10.0.0.5"
        };
        Assert.Throws<ArgumentException>(() => invalidHost.Validate());

        var invalidTimeout = new DfHackProcessAdapterOptions
        {
            Enabled = true,
            RunPath = "C:\\dfhack\\hack\\dfhack-run.exe",
            WorkingDirectory = "C:\\dfhack\\hack",
            Host = "127.0.0.1",
            TimeoutMs = 100
        };
        Assert.Throws<ArgumentException>(() => invalidTimeout.Validate());
    }

    [Fact]
    public void DwarfFortressAdapterOptions_RejectsNumericAdapterTypeValue()
    {
        var options = new DwarfFortressAdapterOptions
        {
            AdapterType = "1"
        };

        var exception = Assert.Throws<ArgumentException>(() => options.ResolveAdapterType(dfHackEnabled: false));

        Assert.Equal("AdapterType", exception.ParamName);
        Assert.Contains("Fake, JsonFile, DfHackProcess", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddFortressSoulsDwarfFortress_SelectsJsonFileAdapter_WhenAdapterTypeIsJsonFile()
    {
        var dwarfListPath = Path.GetTempFileName();
        var dwarfSnapshotPath = Path.GetTempFileName();

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FortressSouls:DwarfFortress:AdapterType"] = DwarfFortressAdapterType.JsonFile.ToString(),
                    ["FortressSouls:DwarfFortress:JsonFile:DwarfListPath"] = dwarfListPath,
                    ["FortressSouls:DwarfFortress:JsonFile:DwarfSnapshotPath"] = dwarfSnapshotPath
                })
                .Build();

            var services = new ServiceCollection();
            services.AddFortressSoulsDwarfFortress(configuration);

            using var provider = services.BuildServiceProvider();

            var adapter = provider.GetRequiredService<IDwarfFortressAdapter>();
            var descriptor = provider.GetRequiredService<DwarfAdapterDescriptor>();
            var status = provider.GetRequiredService<IDwarfAdapterStatusReader>().GetCurrentStatus();

            Assert.IsType<JsonFileDwarfFortressAdapter>(adapter);
            Assert.Equal(DwarfFortressAdapterType.JsonFile.ToString(), descriptor.AdapterType);
            Assert.Equal(DwarfFortressAdapterType.JsonFile.ToString(), status.AdapterType);
            Assert.True(status.IsConfigured);
            Assert.True(status.IsReady);
            Assert.Equal("not_started", status.LastOutcome);
        }
        finally
        {
            File.Delete(dwarfListPath);
            File.Delete(dwarfSnapshotPath);
        }
    }

    [Fact]
    public void AddFortressSoulsDwarfFortress_SelectsJsonFileAdapter_WhenAdapterTypeIsJsonFile_AndLegacyDfHackEnabledIsTrue()
    {
        var dwarfListPath = Path.GetTempFileName();
        var dwarfSnapshotPath = Path.GetTempFileName();

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FortressSouls:DwarfFortress:AdapterType"] = DwarfFortressAdapterType.JsonFile.ToString(),
                    ["FortressSouls:DwarfFortress:JsonFile:DwarfListPath"] = dwarfListPath,
                    ["FortressSouls:DwarfFortress:JsonFile:DwarfSnapshotPath"] = dwarfSnapshotPath,
                    ["FortressSouls:DfHack:Enabled"] = "true",
                    ["FortressSouls:DfHack:RunPath"] = "C:\\dfhack\\hack\\dfhack-run.exe",
                    ["FortressSouls:DfHack:WorkingDirectory"] = "C:\\dfhack\\hack",
                    ["FortressSouls:DfHack:Host"] = "127.0.0.1",
                })
                .Build();

            var services = new ServiceCollection();
            services.AddFortressSoulsDwarfFortress(configuration);

            using var provider = services.BuildServiceProvider();

            var adapter = provider.GetRequiredService<IDwarfFortressAdapter>();
            var descriptor = provider.GetRequiredService<DwarfAdapterDescriptor>();
            var status = provider.GetRequiredService<IDwarfAdapterStatusReader>().GetCurrentStatus();

            Assert.IsType<JsonFileDwarfFortressAdapter>(adapter);
            Assert.Equal(DwarfFortressAdapterType.JsonFile.ToString(), descriptor.AdapterType);
            Assert.Equal(DwarfFortressAdapterType.JsonFile.ToString(), status.AdapterType);
            Assert.True(status.IsConfigured);
            Assert.True(status.IsReady);
            Assert.Equal("not_started", status.LastOutcome);
        }
        finally
        {
            File.Delete(dwarfListPath);
            File.Delete(dwarfSnapshotPath);
        }
    }

    [Fact]
    public void AddFortressSoulsDwarfFortress_SelectsFakeAdapter_WhenAdapterTypeIsFake_AndLegacyDfHackEnabledIsTrue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FortressSouls:DwarfFortress:AdapterType"] = DwarfFortressAdapterType.Fake.ToString(),
                ["FortressSouls:DfHack:Enabled"] = "true",
                ["FortressSouls:DfHack:RunPath"] = "C:\\dfhack\\hack\\dfhack-run.exe",
                ["FortressSouls:DfHack:WorkingDirectory"] = "C:\\dfhack\\hack",
                ["FortressSouls:DfHack:Host"] = "127.0.0.1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFortressSoulsDwarfFortress(configuration);

        using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IDwarfFortressAdapter>();
        var descriptor = provider.GetRequiredService<DwarfAdapterDescriptor>();
        var status = provider.GetRequiredService<IDwarfAdapterStatusReader>().GetCurrentStatus();

        Assert.IsType<FakeDwarfFortressAdapter>(adapter);
        Assert.Equal(DwarfFortressAdapterType.Fake.ToString(), descriptor.AdapterType);
        Assert.Equal(DwarfFortressAdapterType.Fake.ToString(), status.AdapterType);
        Assert.True(status.IsConfigured);
        Assert.True(status.IsReady);
        Assert.Equal("not_started", status.LastOutcome);
    }

    [Fact]
    public void AddFortressSoulsDwarfFortress_FallsBackToLegacyDfHackSelection_WhenAdapterTypeIsMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FortressSouls:DfHack:Enabled"] = "true",
                ["FortressSouls:DfHack:RunPath"] = "C:\\dfhack\\hack\\dfhack-run.exe",
                ["FortressSouls:DfHack:WorkingDirectory"] = "C:\\dfhack\\hack",
                ["FortressSouls:DfHack:Host"] = "127.0.0.1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFortressSoulsDwarfFortress(configuration);

        using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IDwarfFortressAdapter>();
        var descriptor = provider.GetRequiredService<DwarfAdapterDescriptor>();
        var status = provider.GetRequiredService<IDwarfAdapterStatusReader>().GetCurrentStatus();

        Assert.IsType<DfHackDwarfFortressAdapter>(adapter);
        Assert.Equal(DwarfFortressAdapterType.DfHackProcess.ToString(), descriptor.AdapterType);
        Assert.Equal(DwarfFortressAdapterType.DfHackProcess.ToString(), status.AdapterType);
        Assert.True(status.IsConfigured);
        Assert.True(status.IsReady);
        Assert.Equal("not_started", status.LastOutcome);
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public void AddFortressSoulsDwarfFortress_SelectsDfHackProcess_WhenAdapterTypeExplicitlyOverridesLegacyEnabledSwitch(string? legacyEnabled)
    {
        var settings = new Dictionary<string, string?>
        {
            ["FortressSouls:DwarfFortress:AdapterType"] = DwarfFortressAdapterType.DfHackProcess.ToString(),
            ["FortressSouls:DfHack:RunPath"] = "C:\\dfhack\\hack\\dfhack-run.exe",
            ["FortressSouls:DfHack:WorkingDirectory"] = "C:\\dfhack\\hack",
            ["FortressSouls:DfHack:Host"] = "127.0.0.1"
        };

        if (legacyEnabled is not null)
        {
            settings["FortressSouls:DfHack:Enabled"] = legacyEnabled;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddFortressSoulsDwarfFortress(configuration);

        using var provider = services.BuildServiceProvider();

        var adapter = provider.GetRequiredService<IDwarfFortressAdapter>();
        var descriptor = provider.GetRequiredService<DwarfAdapterDescriptor>();
        var status = provider.GetRequiredService<IDwarfAdapterStatusReader>().GetCurrentStatus();

        Assert.IsType<DfHackDwarfFortressAdapter>(adapter);
        Assert.Equal(DwarfFortressAdapterType.DfHackProcess.ToString(), descriptor.AdapterType);
        Assert.Equal(DwarfFortressAdapterType.DfHackProcess.ToString(), status.AdapterType);
        Assert.True(status.IsConfigured);
        Assert.True(status.IsReady);
        Assert.Equal("not_started", status.LastOutcome);
    }

    [Fact]
    public async Task DfHackStatusEndpoint_ReadsProjectionWithoutProcessOrNetworkCalls()
    {
        var countingRunner = new CountingRunner();
        var countingPreflight = new CountingPreflight();
        using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IDwarfFortressAdapter>();
            services.RemoveAll<IDfHackProcessRunner>();
            services.RemoveAll<IDfHackTcpPreflight>();
            services.RemoveAll<IDfHackAdapterStatusRecorder>();
            services.RemoveAll<IDwarfAdapterStatusReader>();
            services.RemoveAll<DwarfAdapterDescriptor>();

            var statusTracker = new DfHackAdapterStatusTracker(enabled: true);
            services.AddSingleton<IDfHackProcessRunner>(countingRunner);
            services.AddSingleton<IDfHackTcpPreflight>(countingPreflight);
            services.AddSingleton(statusTracker);
            services.AddSingleton<IDfHackAdapterStatusRecorder>(statusTracker);
            services.AddSingleton<IDwarfAdapterStatusReader>(statusTracker);
            services.AddSingleton<IDwarfFortressAdapter, DfHackDwarfFortressAdapter>();
            services.AddSingleton(new DwarfAdapterDescriptor("DfHackProcess"));
            services.AddSingleton(new DfHackProcessAdapterOptions
            {
                Enabled = true,
                RunPath = "C:\\dfhack\\hack\\dfhack-run.exe",
                WorkingDirectory = "C:\\dfhack\\hack",
                Host = "127.0.0.1"
            }.Validate());
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/dwarves/adapter-status");
        var status = await response.Content.ReadFromJsonAsync<DwarfAdapterStatusResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(status);
        Assert.Equal("DfHackProcess", status!.AdapterType);
        Assert.Equal(0, countingRunner.InvocationCount);
        Assert.Equal(0, countingPreflight.InvocationCount);
    }

    private static WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection> configureServices) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureServices(configureServices);
            });

    private sealed class CountingPreflight : IDfHackTcpPreflight
    {
        public int InvocationCount { get; private set; }

        public Task<bool> IsReachableAsync(CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(true);
        }
    }

    private sealed class CountingRunner : IDfHackProcessRunner
    {
        public int InvocationCount { get; private set; }

        public Task<DfHackProcessCommandResult> RunCommandAsync(
            DfHackCommand command,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            return Task.FromResult(
                DfHackProcessCommandResult.Success(
                    command,
                    """{"schemaVersion":"fortress-souls-dwarf-list.v0.1","worldLoaded":true,"siteLoaded":true,"mapLoaded":true,"count":0,"items":[]}""",
                    string.Empty,
                    0,
                    TimeSpan.Zero));
        }
    }
}
