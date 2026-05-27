namespace DeveloperOverwatch.Infrastructure.Services;

public sealed class SlackConfig
{
    public string WebhookUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string MinimumSeverity { get; set; } = "Error";
}
