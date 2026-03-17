using System;
using System.ClientModel;
using System.ComponentModel;
using System.Net;

using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

using OpenAI.Chat;

namespace AIAgents;

public class AgentService
{
    public AIAgent CreateFriendlyAgent(string endpoint, string deploymentName, string apiKey)
    {
        ApiKeyCredential apiKeyCredential = new ApiKeyCredential(apiKey );
        AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), apiKeyCredential)
                                .GetChatClient(deploymentName)
                                .AsAIAgent(instructions: "You are a friendly assistant. Keep your answers brief.", name: "HelloAgent");

        return agent;
    }

    public AIAgent CreateWeatherReportingAgent(string endpoint, string deploymentName, string apiKey)
    {
        ApiKeyCredential apiKeyCredential = new ApiKeyCredential(apiKey);
        AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), apiKeyCredential)
                                .GetChatClient(deploymentName)
                                .AsAIAgent(instructions: "You are a help assistant that retrives weather of asked location by using GetWeather tool.", name: "WeatherAgent", tools: [AIFunctionFactory.Create(GetWeather)]);
        return agent;
    }

    public AIAgent CreateMultiTurnConversationalAgent(string endpoint, string deploymentName, string apiKey)
    {
        ApiKeyCredential apiKeyCredential = new ApiKeyCredential(apiKey);
        AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), apiKeyCredential)
                                .GetChatClient(deploymentName)
                                .AsAIAgent(instructions: "You are a friendly assistant that can have multi-turn conversation with user. Keep your answers brief.", name: "MultiTurnAgent", tools: [AIFunctionFactory.Create(GetWeather)]);
        return agent;
    }

    public AIAgent CreateAgentWithMemory(string endpoint, string deploymentName, string apiKey, ChatHistoryProvider vectorStore)
    {
        ApiKeyCredential apiKeyCredential = new ApiKeyCredential(apiKey);
        AIAgent agent = new AzureOpenAIClient(new Uri(endpoint), apiKeyCredential)
                                .GetChatClient(deploymentName)
                                .AsAIAgent( new ChatClientAgentOptions()
                                {
                                    ChatOptions = new() { Instructions = "You are good at telling jokes." },
                                    Name = "AgentWithMemory",
                                    ChatHistoryProvider = vectorStore
                                });
        return agent;
    }

    public AIAgent CreateRagBasedAgent(string endpoint, string deploymentName, string apiKey,string searchEndpoint,string searchapiKey, string indexName,string embeddingDeploymentName)
    {
        var azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
        // Option A: Entra ID auth (recommended in enterprise)
        var searchClient = new SearchClient(new Uri(searchEndpoint), indexName, new AzureKeyCredential(searchapiKey));
        var embeddingClient = azureOpenAIClient.GetEmbeddingClient(embeddingDeploymentName);

        // --- TextSearchProvider options ---
        TextSearchProviderOptions textSearchOptions = new()
        {
            SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
            RecentMessageMemoryLimit = 6,
        };

        // --- Adapter function (Azure AI Search) ---
        var SearchAdapter = AzureAISearchAdapter.Create(
            searchClient,
            embeddingClient,
            vectorFieldName: "contentVector",
            topK: 5,
            hybrid: true
        );

        ApiKeyCredential apiKeyCredential = new ApiKeyCredential(apiKey);
        AIAgent agent = azureOpenAIClient
                                .GetChatClient(deploymentName)
                                .AsAIAgent( new ChatClientAgentOptions()
                                {
                                    ChatOptions = new() { Instructions = "You are a travel agent that helps users book hotels provided by Margies Travel Company in various location. Answer questions using the provided context and cite the source document when available." },
                                    Name = "RagBasedAgent",
                                    AIContextProviders = [new TextSearchProvider(SearchAdapter, textSearchOptions)]
                                })
                                .AsBuilder()
                                .UseOpenTelemetry(sourceName: "MyApplication", configure: (cfg) => cfg.EnableSensitiveData = true)
                                .Build();
        
        return agent;
    }

    //custom tool , this can be replace by api call to get real weather data
    [Description("Get the weather for a given location.")]
    static string GetWeather([Description("The location to get the weather for.")] string location)
    => $"The weather in {location} is cloudy with a high of 15°C.";
}
