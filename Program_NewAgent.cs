using AIAgents;

using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;


Console.WriteLine("Hello, World!");


var configuration = new ConfigurationBuilder()
         .SetBasePath(Directory.GetCurrentDirectory())
         .AddJsonFile("appsettings.json", optional: true)
         .AddJsonFile("appsettings.Development.json", optional: true)
         .AddUserSecrets<Program>(optional: true)
         .Build();


var endpoint = configuration["AZURE_OPENAI_ENDPOINT"]
    ?? throw new InvalidOperationException("Set AZURE_OPENAI_ENDPOINT");
var deploymentName = configuration["AZURE_OPENAI_DEPLOYMENT_NAME"] ?? "gpt-4o-mini";
var apiKey = configuration["OPENAI_API_KEY"] ?? throw new InvalidOperationException("Set AZURE_OPENAI_KEY");
var searchapiKey = configuration["AZURE_SEARCH_API_KEY"]
    ?? throw new InvalidOperationException("AZURE_SEARCH_API_KEY not set");
var searchEndpoint = configuration["AZURE_SEARCH_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_SEARCH_ENDPOINT not set");
var indexName = configuration["AZURE_SEARCH_INDEX"]
    ?? throw new InvalidOperationException("AZURE_SEARCH_INDEX not set");
var embeddingDeploymentName = configuration["AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME"]
    ?? throw new InvalidOperationException("AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME not set");

AgentService agentService = new AgentService();

#region RAG based Agent example
Console.WriteLine("Start using Rag based Agent, write 'exit' to  quit");

var ragAgent = agentService.CreateRagBasedAgent(endpoint, deploymentName, apiKey, searchEndpoint, searchapiKey, indexName, embeddingDeploymentName);
var sessionRag = await ragAgent.CreateSessionAsync();
do
{
    Console.Write("User > ");
    var userRagInput = Console.ReadLine();
    if (userRagInput == "exit")
    {
        break;
    }
    var responseRag = await ragAgent.RunAsync(userRagInput!, sessionRag);
    Console.Write("Response> ");
    Console.WriteLine(responseRag.Text);
} while (true);

Console.ReadLine();
#endregion

#region History Agent Example
Console.WriteLine("Testing Agent with Memory, Write 'exit' to end conversation:");
var historyProvider = new FileChatHistoryProvider("store/sessions");
var memoryAgent = agentService.CreateAgentWithMemory(endpoint, deploymentName, apiKey, historyProvider);
// --- Choose session ---
Console.WriteLine("0) New session");
var sessions = FileSessionStore.ListSessions().ToList();

for (int i = 0; i < sessions.Count; i++)
    Console.WriteLine($"{i + 1}) {sessions[i]}");

Console.Write("> ");
var input = Console.ReadLine();

string sessionId;
AgentSession sessionMem;

if (input == "0")
{
    sessionId = Guid.NewGuid().ToString();
    sessionMem = await memoryAgent.CreateSessionAsync();
}
else
{
    sessionId = sessions[int.Parse(input!) - 1];
    var loaded = await FileSessionStore.LoadAsync(sessionId, memoryAgent);
    sessionMem = loaded!.Value.Session;
}

// IMPORTANT: Attach sessionId to history provider
historyProvider.AttachSessionId(sessionMem, sessionId);

Console.WriteLine($"Using Session: {sessionId}");

while (true)
{
    Console.Write("> ");
    var userInputmem = Console.ReadLine();
    if (userInputmem == "/exit") break;

    var output = await memoryAgent.RunAsync(userInputmem!, sessionMem);
    Console.WriteLine(output);

    await FileSessionStore.SaveAsync(sessionId, sessionMem, memoryAgent);
}

Console.ReadLine();
#endregion

#region Multi-turn Conversational Agent session example
Console.WriteLine("Testing Multi-turn Conversational Agent, Write 'exit' to end conversation:");
var multiTurnAgent = agentService.CreateMultiTurnConversationalAgent(endpoint, deploymentName, apiKey);
var userInput = "";
// Create a session to maintain conversation history
AgentSession session = await multiTurnAgent.CreateSessionAsync();
do
{
    Console.Write("User: ");

    userInput = Console.ReadLine();
    if (string.IsNullOrEmpty(userInput))
        break;

    var agentresponse = await multiTurnAgent.RunAsync(userInput, session);

    Console.WriteLine($"Agent: {agentresponse}");


} while (userInput != "exit");

Console.ReadLine();
#endregion

#region Weather Reporting Agent with tool
Console.WriteLine("Testing Weather Reporting Agent:");
var weatherReportingAgent = agentService.CreateWeatherReportingAgent(endpoint, deploymentName, apiKey);
await weatherReportingAgent.RunAsync("What's the weather in Shanghai?").ContinueWith(t => Console.WriteLine(t.Result));

Console.ReadLine();
#endregion

#region  Friendly Agent

Console.WriteLine("Testing Friendly Agent");
var friendlyAIAgent =  agentService.CreateFriendlyAgent(endpoint, deploymentName, apiKey);

Console.WriteLine(await friendlyAIAgent.RunAsync("What is the largest city in France?"));

await foreach (var update in friendlyAIAgent.RunStreamingAsync("Tell me a one-sentence fun fact."))
{
    Console.Write(update);
}

Console.ReadLine();
#endregion
