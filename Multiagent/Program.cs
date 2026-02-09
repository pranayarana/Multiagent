using Azure.AI.OpenAI;

using Azure.Identity;

using Microsoft.Agents.AI;

using Microsoft.Agents.AI.Workflows;

using Microsoft.Extensions.AI;

using Microsoft.Extensions.Configuration;

IConfiguration config = new ConfigurationBuilder()

    .AddUserSecrets<Program>(optional: true)

    .AddEnvironmentVariables()

    .Build();

var endpoint = config["AZURE_OPENAI_ENDPOINT"]

    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");

string deploymentName = config["AZURE_OPENAI_DEPLOYMENT_NAME"]

    ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME not set");

IChatClient chatClient = new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())

    .GetChatClient(deploymentName)

    .AsIChatClient();

TextSearchProviderOptions ragOptions = new()

{

    SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,

    RecentMessageMemoryLimit = 6,

};


string searchEndpoint = config["AZURE_SEARCH_ENDPOINT"]

    ?? throw new InvalidOperationException("AZURE_SEARCH_ENDPOINT not set");

string searchIndex = config["AZURE_SEARCH_INDEX"]

    ?? throw new InvalidOperationException("AZURE_SEARCH_INDEX not set");

string embeddingDeployment = config["AZURE_OPENAI_EMBEDDINGS_DEPLOYMENT"]

    ?? throw new InvalidOperationException("AZURE_OPENAI_EMBEDDINGS_DEPLOYMENT not set");

var rag = new AzureAiSearchRag(

    searchEndpoint: new Uri(searchEndpoint!),

    indexName: searchIndex!,

    azureOpenAiEndpoint: new Uri(endpoint!),

    embeddingDeployment: embeddingDeployment!

);

// Extractor: usually NO RAG needed

AIAgent extractAgent = chatClient.AsAIAgent(new ChatClientAgentOptions

{

    Id = "extract",

    Name = "Extractor",

    ChatOptions = new()

    {

        Instructions =

            "Extract key clauses, parties, dates, obligations, and risky language from the legal document. " +

            "Return a structured JSON outline with clause IDs and short excerpts."

    }

});

// Compliance: USE RAG

AIAgent complianceAgent = chatClient.AsAIAgent(new ChatClientAgentOptions

{

    Id = "compliance",

    Name = "Compliance",

    ChatOptions = new()

    {

        Instructions =

            "Flag compliance issues (missing clauses, regulatory conflicts, data/privacy issues). " +

            "For every issue, cite retrieved policy text and reference the relevant clause excerpt."

    },

    AIContextProviderFactory = (ctx, ct) => new ValueTask<AIContextProvider>(

        new TextSearchProvider(rag.SearchAdapter, ctx.SerializedState, ctx.JsonSerializerOptions, ragOptions))

});

// Summary: USE RAG (so it can cite policies too)

AIAgent summaryAgent = chatClient.AsAIAgent(new ChatClientAgentOptions

{

    Id = "summary",

    Name = "GroundedSummarizer",

    ChatOptions = new()

    {

        Instructions =

            "Write a concise grounded summary of the document and the main issues. " +

            "Use the document + retrieved policy context. Include citations to policy sources when available."

    },

    AIContextProviderFactory = (ctx, ct) => new ValueTask<AIContextProvider>(

        new TextSearchProvider(rag.SearchAdapter, ctx.SerializedState, ctx.JsonSerializerOptions, ragOptions))

});

Workflow workflow = AgentWorkflowBuilder.BuildSequential(new[] { extractAgent, complianceAgent, summaryAgent });


Console.WriteLine("Paste legal text. End with a single line: END");

var sb = new System.Text.StringBuilder();

while (true)

{

    var line = Console.ReadLine();

    if (line is null) break;              // EOF

    if (line.Trim() == "END") break;      // sentinel

    sb.AppendLine(line);

}

string input = sb.ToString().Trim();


string prompt =

$"""

DOCUMENT:

{input}
 
TASKS:

1) Extract structure with clause IDs and short excerpts.

2) Flag compliance issues using policy KB (cite retrieved sources).

3) Provide grounded summary with citations.

""";

await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, prompt);

// IMPORTANT: Kick off a turn and ask it to emit events

await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

await foreach (WorkflowEvent evt in run.WatchStreamAsync())

{

    Console.WriteLine($"EVENT: {evt.GetType().Name}");

    // Useful: see what executors return even if you don't get streaming token updates

    if (evt is ExecutorCompletedEvent done && done.Data is not null)

    {

        Console.WriteLine($"  Completed: {done.ExecutorId} | DataType: {done.Data.GetType().Name}");

    }

    if (evt is AgentResponseUpdateEvent upd)

    {

        Console.WriteLine($"  [{upd.ExecutorId}] {upd.Data}");

    }

    else if (evt is WorkflowOutputEvent output)

    {

        // In sequential workflows, the output is commonly the full conversation

        if (output.Data is List<ChatMessage> convo)

        {

            Console.WriteLine("\n=== FINAL CONVERSATION ===");

            foreach (var m in convo)

                Console.WriteLine($"{m.Role}: {m.Text}");

        }

        else

        {

            Console.WriteLine("\n=== FINAL OUTPUT ===\n");

            Console.WriteLine(output.Data);

        }

        break; // done

    }

    else if (evt is WorkflowErrorEvent err)

    {

        Console.WriteLine("\n=== WORKFLOW ERROR ===");

        Console.WriteLine(err.Exception);

        break;

    }

}
