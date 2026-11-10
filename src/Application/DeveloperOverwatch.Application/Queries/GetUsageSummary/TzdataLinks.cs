using System.Collections.Frozen;

namespace DeveloperOverwatch.Application.Queries.GetUsageSummary;

/// <summary>Old zone names (Asia/Calcutta, Europe/Kiev, US/Eastern) mapped to the zones they link to, read from the
/// link lines of the host's tzdata.zi. Debian, and Ubuntu from noble on, ship those names' zone files only in
/// tzdata-legacy, but tzdata.zi in the base tzdata package still lists every link, so a host without tzdata-legacy
/// can resolve them. Hosts without the file (Windows, macOS) get an empty map; ICU resolves the names there.</summary>
internal static class TzdataLinks
{
    private const string DefaultZoneDirectory = "/usr/share/zoneinfo";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LazyInstalled = new(Load);

    public static IReadOnlyDictionary<string, string> Installed => LazyInstalled.Value;

    /// <summary>Link name to target, from lines of the form <c>L TARGET LINK-NAME</c>. Other lines are ignored.</summary>
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields is ["L", var target, var name])
                links[name] = target;
        }
        return links.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // .NET reads zone files from TZDIR when it is set, so the link list must come from the same directory.
    private static IReadOnlyDictionary<string, string> Load()
    {
        var directory = Environment.GetEnvironmentVariable("TZDIR") ?? DefaultZoneDirectory;
        var path = Path.Combine(directory, "tzdata.zi");
        return File.Exists(path) ? Parse(File.ReadLines(path)) : FrozenDictionary<string, string>.Empty;
    }
}
