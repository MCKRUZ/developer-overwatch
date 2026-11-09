using System.Diagnostics.CodeAnalysis;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>The server's length and format limits, applied before sending so nothing is rejected for a
/// value the agent could have fixed. A rejected start would lose its whole session.</summary>
internal static class FieldLimits
{
    public const int ProjectKey = 512;
    public const int MachineName = 255;

    /// <summary>Branch, subagent and tool names.</summary>
    public const int Name = 255;

    public const int TurnContent = 4096;

    public const string UnknownEmail = "unknown@localhost";

    [return: NotNullIfNotNull(nameof(value))]
    public static string? Clamp(string? value, int max) =>
        value is { Length: var length } && length > max ? value[..max] : value;

    /// <summary>The email unchanged, or <see cref="UnknownEmail"/> when the server would reject it. Mirrors
    /// FluentValidation's <c>NotEmpty().EmailAddress()</c>: exactly one '@', with text on both sides.</summary>
    public static string Email(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return UnknownEmail;

        var at = value.IndexOf('@');
        return at > 0 && at < value.Length - 1 && at == value.LastIndexOf('@') ? value : UnknownEmail;
    }
}
