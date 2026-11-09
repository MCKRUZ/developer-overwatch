using System.Text.RegularExpressions;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>
/// A stable, credential-free name for the project a session ran in, so sessions from different
/// clones and machines group together. The git remote is normalized to <c>host/owner/repo</c>;
/// with no usable remote it falls back to the repository folder's name.
/// </summary>
public static partial class ProjectKey
{
    public static string? From(string? remoteUrl, string? repoPath) =>
        FromRemote(remoteUrl) ?? FolderName(repoPath);

    [GeneratedRegex("^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase)]
    private static partial Regex Scheme();

    private static string? FromRemote(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return null;

        var url = StripFrom(StripFrom(remoteUrl.Trim(), '#'), '?');
        var scheme = Scheme().Match(url);
        var parts = scheme.Success ? SplitUrl(url[scheme.Length..]) : SplitScp(url);
        if (parts is not var (authority, path))
            return null;

        // Userinfo (user, user:token, git) never leaves the machine; neither does a port.
        var host = authority[(authority.LastIndexOf('@') + 1)..];
        if (host.LastIndexOf(':') is var colon and >= 0)
            host = host[..colon];

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4].Trim('/');

        // GitHub, GitLab and Azure DevOps paths are case-insensitive, so Acme/Widget and
        // acme/widget are one project and must get one key.
        return host.Length == 0 || path.Length == 0 ? null : $"{host}/{path}".ToLowerInvariant();
    }

    // scheme://authority/path
    private static (string Authority, string Path)? SplitUrl(string rest)
    {
        var slash = rest.IndexOf('/');
        return slash < 0 ? (rest, "") : (rest[..slash], rest[(slash + 1)..]);
    }

    // scp-like [user@]host:owner/repo. Anything else without a scheme is a local path.
    private static (string Authority, string Path)? SplitScp(string url)
    {
        var firstSlash = url.IndexOf('/');
        var authorityEnd = firstSlash < 0 ? url.Length : firstSlash;
        var at = url.LastIndexOf('@', Math.Max(authorityEnd - 1, 0));
        var colon = url.IndexOf(':', at + 1);
        // colon <= 1 also rejects a Windows drive path such as C:/repos/x.
        if (colon <= at + 1 || colon > authorityEnd || colon <= 1)
            return null;

        return (url[..colon], url[(colon + 1)..]);
    }

    private static string StripFrom(string value, char marker) =>
        value.IndexOf(marker) is var index and >= 0 ? value[..index] : value;

    private static string? FolderName(string? repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            return null;

        var name = repoPath.Trim().TrimEnd('/', '\\').Split('/', '\\')[^1];
        return name.Length > 0 ? name : null;
    }
}
