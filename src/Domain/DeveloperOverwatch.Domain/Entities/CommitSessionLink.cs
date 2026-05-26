namespace DeveloperOverwatch.Domain.Entities;

public class CommitSessionLink
{
    public string Id { get; set; } = string.Empty;
    public string CommitSha { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string Repo { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public string? LinkMethod { get; set; }

    public Session? Session { get; set; }
}
