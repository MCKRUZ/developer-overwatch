namespace DeveloperOverwatch.Application.Queries.GetSessionPrompts;

using System.Text.RegularExpressions;

/// <summary>A prompt's text as a one-line label for a table row.</summary>
internal static partial class PromptLabel
{
    public const int MaxLength = 80;
    private const string Ellipsis = "…";

    /// <summary>Whitespace collapsed to single spaces and trimmed, then cut to <see cref="MaxLength"/> characters
    /// with an ellipsis when longer; null when nothing is left.</summary>
    public static string? From(string? text)
    {
        if (text is null)
            return null;

        var collapsed = Whitespace().Replace(text, " ").Trim();
        if (collapsed.Length == 0)
            return null;
        if (collapsed.Length <= MaxLength)
            return collapsed;

        var cut = MaxLength - Ellipsis.Length;
        if (char.IsHighSurrogate(collapsed[cut - 1]))
            cut--;
        return collapsed[..cut] + Ellipsis;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
