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

    public string Redact(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

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
