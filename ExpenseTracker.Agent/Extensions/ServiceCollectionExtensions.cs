using Azure.AI.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using ExpenseTracker.Agent.Interfaces;
using ExpenseTracker.Agent.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace ExpenseTracker.Agent.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IChatClient (provider-agnostic) and IAiOrchestrator as singletons.
    /// Provider is selected via AiOrchestrator:Provider config key:
    ///   "AzureFoundry" (default) — uses DefaultAzureCredential + AIProjectClient
    ///   "AzureOpenAI"            — uses AzureOpenAIClient with endpoint + API key
    ///   "OpenAI"                 — uses OpenAIClient with API key
    /// </summary>
    public static IServiceCollection AddAiOrchestration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IChatClient>(_ => CreateChatClient(configuration));
        services.AddSingleton<IAiOrchestrator, AiOrchestrator>();
        return services;
    }

    private static IChatClient CreateChatClient(IConfiguration configuration)
    {
        var provider = configuration["AiOrchestrator:Provider"] ?? "AzureFoundry";

        return provider switch
        {
            "AzureOpenAI" => new AzureOpenAIClient(
                    new Uri(configuration["AzureOpenAI:Endpoint"]
                        ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is not registered.")),
                    new Azure.AzureKeyCredential(configuration["AzureOpenAI:ApiKey"]
                        ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is not registered.")))
                .GetChatClient(configuration["AzureOpenAI:Deployment"]
                    ?? throw new InvalidOperationException("AzureOpenAI:Deployment is not registered."))
                .AsIChatClient(),

            "OpenAI" => new OpenAIClient(
                    new System.ClientModel.ApiKeyCredential(configuration["OpenAI:ApiKey"]
                        ?? throw new InvalidOperationException("OpenAI:ApiKey is not registered.")))
                .GetChatClient(configuration["OpenAI:Model"] ?? "gpt-4o")
                .AsIChatClient(),

            _ => new AIProjectClient(
                    new Uri(configuration["AzureFoundry:ProjectEndpoint"] ?? throw new InvalidOperationException("AzureFoundry:ProjectEndpoint is not registered.")),
                    new DefaultAzureCredential())
                .GetProjectOpenAIClient()
                .GetChatClient(configuration["AzureFoundry:Deployment"] ?? configuration["AzureFoundry:AgentName"] ?? throw new InvalidOperationException("AzureFoundry:Deployment veya AgentName should be registered."))
                .AsIChatClient(),
        };
    }
}
