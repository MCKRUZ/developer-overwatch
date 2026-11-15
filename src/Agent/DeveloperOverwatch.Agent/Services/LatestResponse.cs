using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>The context the latest main-thread response was sent with. As in <see cref="TurnBuilder"/>, the lines of
/// one response share a message id and repeat its usage, so the largest value per field is kept. A response with no
/// context, such as a synthetic error message, does not replace an earlier one.</summary>
internal sealed class LatestResponse
{
    private Measure? _current;
    private Measure? _lastWithContext;

    public sealed record Measure(string? Id, string? Model, long Input, long CacheRead, long CacheCreation)
    {
        public long ContextTokens => Input + CacheRead + CacheCreation;
    }

    /// <summary>Null until a response with context has been read.</summary>
    public Measure? Latest => _current is { ContextTokens: > 0 } ? _current : _lastWithContext;

    public void Add(JsonElement line)
    {
        if (!TranscriptJson.TryObject(line, "message", out var message))
            return;

        var id = TranscriptJson.String(message, "id");
        var usage = TranscriptJson.TryObject(message, "usage", out var u) ? u : default;
        long Field(string name) => usage.ValueKind == JsonValueKind.Object ? TranscriptJson.Long(usage, name) ?? 0 : 0;
        var measure = new Measure(id, TranscriptJson.String(message, "model"),
            Field("input_tokens"), Field("cache_read_input_tokens"), Field("cache_creation_input_tokens"));

        if (_current is { } current && id is not null && current.Id == id)
        {
            _current = new Measure(id, current.Model ?? measure.Model, Math.Max(current.Input, measure.Input),
                Math.Max(current.CacheRead, measure.CacheRead), Math.Max(current.CacheCreation, measure.CacheCreation));
            return;
        }

        if (_current is { ContextTokens: > 0 })
            _lastWithContext = _current;
        _current = measure;
    }
}
