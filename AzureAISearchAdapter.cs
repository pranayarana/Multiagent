using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;

using Microsoft.Agents.AI;

using OpenAI.Embeddings;

namespace AIAgents;

public static class AzureAISearchAdapter
{
    public static Func<string, CancellationToken, Task<IEnumerable<TextSearchProvider.TextSearchResult>>> Create(
        SearchClient searchClient, 
        EmbeddingClient embeddingClient,
        string vectorFieldName,          // e.g. "contentVector"
        int topK = 5,
        bool hybrid = true)
    {
        return async (query, ct) =>
        {
            // 1) Create embedding for query
            var emb = await embeddingClient.GenerateEmbeddingAsync(query, cancellationToken: ct);
            var queryVector = emb.Value.ToFloats();

            // 2) Build vector query
            var vectorQuery = new VectorizedQuery(queryVector)
            {
                KNearestNeighborsCount = topK,
                Fields = { vectorFieldName }
            };
            var options = new SearchOptions
            {
                Size = topK,
                // Hybrid search: also include lexical query text if desired
                // If hybrid=false, pass "" to SearchAsync and rely on vector only.
                VectorSearch = new VectorSearchOptions()
            };
            options.VectorSearch.Queries.Add(vectorQuery);

            // Only select what you need
            options.Select.Add("content");
            options.Select.Add("title");
            options.Select.Add("url");

            // 3) Run search
            string searchText = hybrid ? query : ""; // hybrid combines text + vector
            Response<SearchResults<SupportChunk>> response =
                await searchClient.SearchAsync<SupportChunk>(searchText, options, ct);

            // 4) Convert to TextSearchProvider results
            var results = new List<TextSearchProvider.TextSearchResult>();

            await foreach (SearchResult<SupportChunk> r in response.Value.GetResultsAsync())
            {
                var doc = r.Document;

                results.Add(new TextSearchProvider.TextSearchResult
                {
                    SourceName = doc.SourceName,
                    SourceLink = doc.SourceLink,
                    Text = doc.Content ?? string.Empty,
                    RawRepresentation = r
                });
            }

            return results;
        };
    }
}

public sealed class SupportChunk
{
    [SimpleField(IsKey = true)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = default!;

    [SearchableField]
    [JsonPropertyName("content")]
    public string Content { get; set; } = default!;

    [SearchableField]
    [JsonPropertyName("title")]
    public string? SourceName { get; set; }

    [SearchableField]
    [JsonPropertyName("url")]
    public string? SourceLink { get; set; }
}
