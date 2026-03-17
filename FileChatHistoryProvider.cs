using System.Text.Json;

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

public sealed class FileChatHistoryProvider : ChatHistoryProvider
{
    private readonly string _historyDir;
    private readonly ProviderSessionState<State> _sessionState;

    public FileChatHistoryProvider(string historyDir)
    {
        _historyDir = historyDir;

        _sessionState = new ProviderSessionState<State>(
            _ => new State(),
            GetType().Name);
    }

    public override string StateKey => _sessionState.StateKey;

    public void AttachSessionId(AgentSession session, string sessionId)
    {
        var state = _sessionState.GetOrInitializeState(session);
        state.SessionId = sessionId;
        state.FilePath = Path.Combine(_historyDir, $"{sessionId}.history.json");

        Directory.CreateDirectory(_historyDir);

        if (File.Exists(state.FilePath))
        {
            File.Delete(state.FilePath);
        }

        _sessionState.SaveState(session, state);
    }

    protected override async ValueTask<IEnumerable<ChatMessage>> InvokingCoreAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var state = _sessionState.GetOrInitializeState(context.Session);

        if (!state.Loaded && state.FilePath is not null)
        {
            state.Messages = await LoadAsync(state.FilePath);
            state.Loaded = true;
        }

        var stamped = state.Messages.Select(m =>
            m.WithAgentRequestMessageSource(
                AgentRequestMessageSourceType.ChatHistory,
                GetType().FullName!));

        return stamped.Concat(context.RequestMessages);
    }

    protected override async ValueTask InvokedCoreAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.InvokeException is not null)
            return;

        var state = _sessionState.GetOrInitializeState(context.Session);

        var filtered = context.RequestMessages.Where(m =>
            m.GetAgentRequestMessageSourceType() !=
            AgentRequestMessageSourceType.ChatHistory);

        var allNew = filtered.Concat(context.ResponseMessages ?? [])
                             .ToList();

        state.Messages.AddRange(allNew);

        if (state.FilePath is not null)
            await SaveAsync(state.FilePath, state.Messages);
    }

    private static async Task<List<ChatMessage>> LoadAsync(string path)
    {
        if (!File.Exists(path))
            return new();

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<ChatMessage>>(json) ?? new();
    }

    private static async Task SaveAsync(string path, List<ChatMessage> msgs)
    {
        var json = JsonSerializer.Serialize(msgs,
            new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(path, json);
    }

    private sealed class State
    {
        public string? SessionId { get; set; }
        public string? FilePath { get; set; }
        public bool Loaded { get; set; }
        public List<ChatMessage> Messages { get; set; } = new();
    }
}
