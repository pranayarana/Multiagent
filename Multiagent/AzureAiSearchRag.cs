using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

using Microsoft.Agents.AI;

using OpenAI.Embeddings;

public sealed class AzureAiSearchRag
{
    private readonly SearchClient _searchClient;
    private readonly AzureOpenAIClient _openAi;
    private readonly string _embeddingDeployment;

    public AzureAiSearchRag(
        Uri searchEndpoint,
        string indexName,
        Uri azureOpenAiEndpoint,
        string embeddingDeployment)
    {
        _searchClient = new SearchClient(searchEndpoint, indexName, new DefaultAzureCredential());
        _openAi = new AzureOpenAIClient(azureOpenAiEndpoint, new DefaultAzureCredential());
        _embeddingDeployment = embeddingDeployment;
    }

    // Signature expected by TextSearchProvider:
    // Func<string, CancellationToken, Task<IEnumerable<TextSearchProvider.TextSearchResult>>>
    public async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAdapter(
        string query,
        CancellationToken ct)
    {
        // 1) Create embedding for the query (vector)
        float[] queryVector = await GetQueryEmbeddingAsync(query, ct);

        // 2) Build Azure AI Search options (hybrid: keyword + vector)
        var options = new SearchOptions
        {
            Size = 5,
            QueryType = SearchQueryType.Simple,
        };

        options.Select.Add("content");
        options.Select.Add("title");
        options.Select.Add("url");


        // 3) Add vector query (field name must match your index schema)
        options.VectorSearch = new VectorSearchOptions();
        options.VectorSearch.Queries.Add(new VectorizedQuery(queryVector)
        {
            KNearestNeighborsCount = 5,
            // IMPORTANT: this must match your vector field name in the index
            Fields = { "contentVector" }
        });

        // 4) Execute search (keyword + vector)
        Response<SearchResults<SearchDocument>> resp =
            await _searchClient.SearchAsync<SearchDocument>(query, options, ct);

        // 5) Convert to TextSearchResult for Agent Framework
        var results = new List<TextSearchProvider.TextSearchResult>();

        await foreach (SearchResult<SearchDocument> r in resp.Value.GetResultsAsync())
        {
            var doc = r.Document;
            string content = doc.TryGetValue("content", out var c)
                       ? c?.ToString() ?? ""
                       : "";

            string title = doc.TryGetValue("title", out var t)
                ? t?.ToString() ?? "Knowledge Base"
                : "Knowledge Base";

            string url = doc.TryGetValue("url", out var u)
                ? u?.ToString() ?? ""
                : "";

            if (!string.IsNullOrWhiteSpace(content))
            {
                results.Add(new TextSearchProvider.TextSearchResult
                {
                    SourceName = title,   // maps to index.title
                    SourceLink = url,     // maps to index.url
                    Text = content
                });
            }
        }

        return results;
    }

    private async Task<float[]> GetQueryEmbeddingAsync(string text, CancellationToken ct)
    {
        // Azure.AI.OpenAI (v2.x) embeddings client
        EmbeddingClient embeddingClient = _openAi.GetEmbeddingClient(_embeddingDeployment);

        // Generate a single embedding for the query text
        var embedding = await embeddingClient.GenerateEmbeddingAsync(text, cancellationToken: ct);

        // Convert to float[]
        return embedding.Value.ToFloats().ToArray();
    }
}