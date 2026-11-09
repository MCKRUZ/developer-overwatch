namespace DeveloperOverwatch.API.Tests.Configuration;

using Microsoft.Extensions.Configuration;

/// <summary>Loads the API project's real appsettings.json from the source tree, not a bin copy.</summary>
internal static class RepoAppSettings
{
    public static IConfiguration Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeveloperOverwatch.slnx")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not find DeveloperOverwatch.slnx above the test output folder.");

        var path = Path.Combine(dir.FullName, "src", "API", "DeveloperOverwatch.API", "appsettings.json");
        return new ConfigurationBuilder().AddJsonFile(path, optional: false).Build();
    }
}
