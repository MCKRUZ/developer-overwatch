using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>A tool definition sent on every request, sized in characters.</summary>
internal sealed record LoadedTool(string Name, long Chars);

/// <summary>What one <c>api-request-shape</c> line costs every request: its system prompt and the tools it loads,
/// in characters. Only these sizes are kept, never the text.</summary>
internal sealed record RequestShape(long SystemChars, IReadOnlyList<LoadedTool> Tools)
{
    /// <summary>Stands in for the deferred tools; their names are counted from the deferred listing instead.</summary>
    public const string PlaceholderTool = "DeferredToolPlaceholder";

    private static readonly JsonWriterOptions CompactWriter = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static RequestShape? From(JsonElement line)
    {
        if (!TranscriptJson.TryObject(line, "shape", out var shape))
            return null;

        var systemChars = TranscriptJson.Objects(shape, "system").Sum(block => (long)(TranscriptJson.String(block, "text")?.Length ?? 0));
        var tools = TranscriptJson.Objects(shape, "tools")
            .Where(tool => !TranscriptJson.IsTrue(tool, "defer_loading"))
            .Select(tool => (Name: TranscriptJson.String(tool, "name"), Tool: tool))
            .Where(t => t.Name is { Length: > 0 } && t.Name != PlaceholderTool)
            .Select(t => new LoadedTool(t.Name!, Chars(t.Name!, t.Tool)))
            .ToList();
        return new RequestShape(systemChars, tools);
    }

    /// <summary>Name, description and the input schema as compact JSON. The schema is re-written rather than taken
    /// raw, so the source line's spacing does not count, and with relaxed escaping, so '&lt;' or non-ASCII text is
    /// not inflated into <c>\uXXXX</c>.</summary>
    private static long Chars(string name, JsonElement tool)
    {
        var description = TranscriptJson.String(tool, "description")?.Length ?? 0;
        if (!tool.TryGetProperty("input_schema", out var schema))
            return name.Length + description;

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, CompactWriter))
            schema.WriteTo(writer);
        return name.Length + description + Encoding.UTF8.GetCharCount(buffer.WrittenSpan);
    }
}
