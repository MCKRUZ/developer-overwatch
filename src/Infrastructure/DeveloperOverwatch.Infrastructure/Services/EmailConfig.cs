namespace DeveloperOverwatch.Infrastructure.Services;

public sealed class EmailConfig
{
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string FromAddress { get; set; } = string.Empty;
    public string[] ToAddresses { get; set; } = [];
    public bool Enabled { get; set; }
    public bool UseSsl { get; set; } = true;
    public string MinimumSeverity { get; set; } = "Critical";
}
