using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DeveloperOverwatch.Agent.Services;

public interface IPrivacyRedactor
{
    string Redact(string input);
    bool ContainsSensitive(string input);
}

public sealed class PrivacyRedactor : IPrivacyRedactor
{
    private const string Replacement = "[REDACTED]";
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<Regex> _patterns;

    public PrivacyRedactor(IReadOnlyList<string> patterns)
    {
        var compiled = new Regex[patterns.Count];
        for (var i = 0; i < patterns.Count; i++)
            compiled[i] = new Regex(patterns[i], RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout);

        _patterns = compiled;
    }

    /// <summary>
    /// Redacts sensitive substrings. When <paramref name="input"/> is a JSON document, redaction
    /// runs only inside string leaf values (each redacted independently, then re-serialized) —
    /// never as a whole-string regex pass. A whole-string pass on compact (no-whitespace) JSON is
    /// unsafe: a pattern like `password\s*[=:]\s*\S+` has an unbounded `\S+` term that, with no
    /// whitespace to stop it, swallows through the closing quote/brace/comma of the current field
    /// and into subsequent JSON structure — corrupting or merging adjacent objects (observed:
    /// merging two separate conversation turns into one, with the wrong turn's role surviving).
    /// Falls back to whole-string regex redaction for non-JSON input.
    /// </summary>
    public string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        try
        {
            using var doc = JsonDocument.Parse(input);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                RedactElement(doc.RootElement, writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return RedactRaw(input);
        }
    }

    private void RedactElement(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    RedactElement(property.Value, writer);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    RedactElement(item, writer);
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(RedactRaw(element.GetString() ?? string.Empty));
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private string RedactRaw(string input)
    {
        var result = input;
        foreach (var pattern in _patterns)
        {
            try
            {
                result = pattern.Replace(result, Replacement);
            }
            catch (RegexMatchTimeoutException)
            {
                // Pattern took too long — skip it rather than crash the pipeline
            }
        }

        return result;
    }

    public bool ContainsSensitive(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        foreach (var pattern in _patterns)
        {
            try
            {
                if (pattern.IsMatch(input))
                    return true;
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }

        return false;
    }
}
