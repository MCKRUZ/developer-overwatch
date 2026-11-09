using DeveloperOverwatch.Agent.Services;

namespace DeveloperOverwatch.Agent.Cli;

/// <summary>What <see cref="BackfillCommand"/> reads from and writes to; tests point these at temp folders.</summary>
/// <param name="ProjectsDir">Claude Code's projects folder, holding <c>&lt;encoded-folder&gt;/&lt;session-id&gt;.jsonl</c>.</param>
public sealed record BackfillServices(
    IUsageStore Usage,
    IEventQueue Queue,
    IConversationCursorStore Cursors,
    IConversationCapture Capture,
    IGitRemoteReader Git,
    string ProjectsDir)
{
    public static string DefaultProjectsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
}
