namespace FortressSouls.Tests;

using FortressSouls.Api;
using FortressSouls.Application;
using FortressSouls.Domain;
using FortressSouls.DwarfFortress;
using FortressSouls.Llm;
using FortressSouls.Prompting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;

public class ArchitectureTests
{
    [Fact]
    public void DomainProjectHasNoDependenciesOnAdaptersOrFramework()
    {
        var domainAssembly = typeof(SentinelType).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies();

        var forbidden = referencedAssemblies
            .Where(a => (a.Name ?? "").StartsWith("FortressSouls.", StringComparison.Ordinal)
                || (a.Name ?? "").StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || (a.Name ?? "").StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void DwarfFortressPortExposesOnlyListAndByIdSnapshotOperations()
    {
        var methods = typeof(IDwarfFortressAdapter)
            .GetMethods()
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Collection(
            methods,
            method =>
            {
                Assert.Equal(nameof(IDwarfFortressAdapter.GetDwarfSnapshotAsync), method.Name);
                var parameters = method.GetParameters();
                Assert.Collection(
                    parameters,
                    parameter => Assert.Equal(typeof(DwarfId), parameter.ParameterType),
                    parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
            },
            method =>
            {
                Assert.Equal(nameof(IDwarfFortressAdapter.ListDwarvesAsync), method.Name);
                var parameters = method.GetParameters();
                Assert.Collection(
                    parameters,
                    parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
            });
    }

    [Fact]
    public void SolutionAssembliesRemainLoadable()
    {
        var domainAssembly = typeof(SentinelType).Assembly;
        var applicationAssembly = typeof(HealthResponse).Assembly;

        Assert.Equal("FortressSouls.Domain", domainAssembly.GetName().Name);
        Assert.Equal("FortressSouls.Application", applicationAssembly.GetName().Name);
    }

    [Fact]
    public void ChatProviderPort_ExposesSingleBoundedCancellationAwareMethod()
    {
        var methods = typeof(IChatProvider).GetMethods();
        var method = Assert.Single(methods);
        Assert.Equal(nameof(IChatProvider.SendAsync), method.Name);

        var parameters = method.GetParameters();
        Assert.Collection(
            parameters,
            parameter => Assert.Equal(typeof(ChatProviderRequest), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    [Fact]
    public void DwarfAgentPort_ExposesSingleBoundedCancellationAwareMethod()
    {
        var methods = typeof(IDwarfAgent).GetMethods();
        var method = Assert.Single(methods);
        Assert.Equal(nameof(IDwarfAgent.RunTurnAsync), method.Name);

        var parameters = method.GetParameters();
        Assert.Collection(
            parameters,
            parameter => Assert.Equal(typeof(AgentTurnRequest), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    [Fact]
    public void StockInspectionPort_ExposesSingleBoundedCancellationAwareMethod()
    {
        var methods = typeof(IStockInspectionService).GetMethods();
        var method = Assert.Single(methods);
        Assert.Equal(nameof(IStockInspectionService.InspectStocksAsync), method.Name);

        var parameters = method.GetParameters();
        Assert.Collection(
            parameters,
                    parameter => Assert.Equal(typeof(string), parameter.ParameterType),
                    parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    [Fact]
    public void SurroundingsInspectionPort_ExposesSingleSessionBoundedCancellationAwareMethod()
    {
        var methods = typeof(ISurroundingsInspectionService).GetMethods();
        var method = Assert.Single(methods);
        Assert.Equal(nameof(ISurroundingsInspectionService.InspectAroundAsync), method.Name);

        var parameters = method.GetParameters();
        Assert.Collection(
            parameters,
            parameter => Assert.Equal(typeof(DwarfId), parameter.ParameterType),
            parameter => Assert.Equal(typeof(int), parameter.ParameterType),
            parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
    }

    [Fact]
    public void AgentTurnRequest_ExposesSessionBoundDwarfContext()
    {
        Assert.Equal(typeof(AgentSessionContext), typeof(AgentTurnRequest).GetProperty(nameof(AgentTurnRequest.Session))?.PropertyType);
        Assert.Equal(typeof(string), typeof(AgentSessionContext).GetProperty(nameof(AgentSessionContext.SessionId))?.PropertyType);
        Assert.Equal(typeof(DwarfId), typeof(AgentSessionContext).GetProperty(nameof(AgentSessionContext.DwarfId))?.PropertyType);
        Assert.Equal(typeof(DwarfSnapshot), typeof(AgentSessionContext).GetProperty(nameof(AgentSessionContext.Snapshot))?.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<ChatHistoryMessage>), typeof(AgentSessionContext).GetProperty(nameof(AgentSessionContext.Conversation))?.PropertyType);
    }

    [Fact]
    public void AgentToolRegistryPort_SeparatesDefinitionsFromExecution()
    {
        var methods = typeof(IAgentToolRegistry)
            .GetMethods()
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Collection(
            methods,
            method =>
            {
                Assert.Equal(nameof(IAgentToolRegistry.ExecuteAsync), method.Name);
                Assert.Equal(typeof(Task<AgentToolResult>), method.ReturnType);
                var parameters = method.GetParameters();
                Assert.Collection(
                    parameters,
                    parameter => Assert.Equal(typeof(AgentToolInvocation), parameter.ParameterType),
                    parameter => Assert.Equal(typeof(CancellationToken), parameter.ParameterType));
            },
            method =>
            {
                Assert.Equal(nameof(IAgentToolRegistry.ListDefinitions), method.Name);
                Assert.Equal(typeof(IReadOnlyList<AgentToolDefinition>), method.ReturnType);
            },
            method =>
            {
                Assert.Equal(nameof(IAgentToolRegistry.TryGetDefinition), method.Name);
                var parameters = method.GetParameters();
                Assert.Collection(
                    parameters,
                    parameter => Assert.Equal(typeof(string), parameter.ParameterType),
                    parameter => Assert.Equal(typeof(AgentToolDefinition).MakeByRefType(), parameter.ParameterType));
            },
            method =>
            {
                Assert.Equal(nameof(IAgentToolRegistry.ValidateInvocation), method.Name);
                var parameters = method.GetParameters();
                Assert.Collection(
                    parameters,
                    parameter => Assert.Equal(typeof(AgentToolInvocation), parameter.ParameterType));
                Assert.Equal(typeof(void), method.ReturnType);
            });
    }

    [Fact]
    public void DfHackCommandAllowlist_IsClosedAndReadOnly()
    {
        var values = Enum.GetNames<DfHackCommand>();
        Assert.Equal(
            ["Diagnose", "ListDwarves", "GetDwarfSnapshot", "GetDwarfSurroundings", "GetStockSummary"],
            values);
    }

    [Fact]
    public void NonAdapterAssemblies_DoNotReferenceMicrosoftAgentOrChatPackages()
    {
        var assemblies = new[]
        {
            typeof(Program).Assembly,
            typeof(HealthResponse).Assembly,
            typeof(SentinelType).Assembly,
            typeof(IDwarfFortressAdapter).Assembly,
            typeof(PromptAssembler).Assembly
        };
        var forbiddenPrefixes = new[]
        {
            "Microsoft.Extensions.AI",
            "Microsoft.Agents",
            "Microsoft.SemanticKernel",
            "OpenAI"
        };

        foreach (var assembly in assemblies)
        {
            var referencedAssemblies = assembly.GetReferencedAssemblies();
            Assert.DoesNotContain(
                referencedAssemblies,
                reference => forbiddenPrefixes.Any(prefix => (reference.Name ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void LlmRegistration_DoesNotRegisterToolLoopProbeServicesByDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("FortressSouls:Llm:ProviderType", "OpenAiCompatible"),
                new KeyValuePair<string, string?>("FortressSouls:Llm:Endpoint", "https://openrouter.ai/api/v1"),
                new KeyValuePair<string, string?>("FortressSouls:Llm:Model", "deepseek/deepseek-v3.2"),
                new KeyValuePair<string, string?>("FortressSouls:Llm:ApiKey", "test-key"),
                new KeyValuePair<string, string?>("FortressSouls:Llm:TimeoutSeconds", "5")
            ])
            .Build();
        var services = new ServiceCollection();

        services.AddFortressSoulsLlm(configuration);

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IDwarfAgent));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IAgentToolRegistry));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ProbeObservationToolService));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IChatClient));
    }
}
