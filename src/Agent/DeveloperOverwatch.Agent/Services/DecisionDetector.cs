using System.Text.RegularExpressions;

namespace DeveloperOverwatch.Agent.Services;

public interface IDecisionDetector
{
    bool IsOverride(string? lastAssistantToolOutput, string? userNextToolInput, string? filePath);
    bool IsRedirect(string? userMessage);
    bool IsManualEdit(string? beforeHash, string? afterHash, string? sessionStartHash);
    bool IsDecisionMoment(string? lastAssistantToolOutput, string? userNextToolInput, string? filePath,
        string? userMessage, string? beforeHash, string? afterHash, string? sessionStartHash);
}

public sealed partial class DecisionDetector : IDecisionDetector
{
    [GeneratedRegex(
        @"\b(no|instead|don't|actually|change\s+this\s+to|not\s+that|wrong|undo|revert|go\s+back)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex RedirectPattern();

    public bool IsOverride(string? lastAssistantToolOutput, string? userNextToolInput, string? filePath)
    {
        if (string.IsNullOrEmpty(lastAssistantToolOutput) || string.IsNullOrEmpty(userNextToolInput) || string.IsNullOrEmpty(filePath))
            return false;

        return lastAssistantToolOutput.Contains(filePath, StringComparison.OrdinalIgnoreCase)
            && userNextToolInput.Contains(filePath, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsRedirect(string? userMessage)
    {
        if (string.IsNullOrEmpty(userMessage))
            return false;

        return RedirectPattern().IsMatch(userMessage);
    }

    public bool IsManualEdit(string? beforeHash, string? afterHash, string? sessionStartHash)
    {
        if (beforeHash is null || afterHash is null || sessionStartHash is null)
            return false;

        return !string.Equals(afterHash, beforeHash, StringComparison.Ordinal)
            && !string.Equals(afterHash, sessionStartHash, StringComparison.Ordinal);
    }

    public bool IsDecisionMoment(string? lastAssistantToolOutput, string? userNextToolInput, string? filePath,
        string? userMessage, string? beforeHash, string? afterHash, string? sessionStartHash)
    {
        return IsOverride(lastAssistantToolOutput, userNextToolInput, filePath)
            || IsRedirect(userMessage)
            || IsManualEdit(beforeHash, afterHash, sessionStartHash);
    }
}
