using System.Text.Json;

using Microsoft.Agents.AI;

public static class FileSessionStore
{
    public static string BaseDir { get; set; } = Path.Combine(AppContext.BaseDirectory, "store", "sessions");

    public static string PathFor(string sessionId)
        => System.IO.Path.Combine(BaseDir, $"{sessionId}.session.json");

    public static async Task SaveAsync(
    string sessionId,
    AgentSession session,
    AIAgent agent)
    {
        Directory.CreateDirectory(BaseDir);

        var serialized = await agent.SerializeSessionAsync(session);

        var wrapper = new PersistedSession(sessionId, serialized);

        var json = JsonSerializer.Serialize(wrapper,
            new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(PathFor(sessionId), json);
    }

    public static async Task<(string SessionId, AgentSession Session)?>
       LoadAsync(string sessionId, AIAgent agent)
    {
        var path = PathFor(sessionId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);

        var wrapper = JsonSerializer.Deserialize<PersistedSession>(json);
        if (wrapper is null)
            return null;

        var session =
            await agent.DeserializeSessionAsync(wrapper.SerializedSession);

        return (wrapper.SessionId, session);
    }

    public static IEnumerable<string> ListSessions()
    {
        Directory.CreateDirectory(BaseDir);

        foreach (var file in Directory.EnumerateFiles(BaseDir, "*.session.json"))
            yield return Path.GetFileNameWithoutExtension(file).Replace(".session","");
    }
}

public record PersistedSession(
    string SessionId,
    JsonElement SerializedSession
);
