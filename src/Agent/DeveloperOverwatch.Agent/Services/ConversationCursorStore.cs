using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>
/// Tracks, per session, how many transcript lines have already been captured and shipped —
/// so repeated Stop-hook triggers only send newly-appeared conversation turns.
/// </summary>
public interface IConversationCursorStore
{
    Task<int> GetAsync(string sessionId, CancellationToken ct);
    Task SetAsync(string sessionId, int lineCount, CancellationToken ct);
}

public sealed class ConversationCursorStore : IConversationCursorStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ConversationCursorStore(string overwatchDir)
    {
        _path = Path.Combine(overwatchDir, "conversation-cursors.json");
    }

    public async Task<int> GetAsync(string sessionId, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var cursors = await LoadAsync(ct);
            return cursors.GetValueOrDefault(sessionId, 0);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync(string sessionId, int lineCount, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var cursors = await LoadAsync(ct);
            cursors[sessionId] = lineCount;
            await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(cursors), ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Dictionary<string, int>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(_path, ct);
            return JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
