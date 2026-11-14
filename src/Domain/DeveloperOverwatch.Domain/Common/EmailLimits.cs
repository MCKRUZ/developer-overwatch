namespace DeveloperOverwatch.Domain.Common;

/// <summary>Bounds on stored email addresses, shared by the database columns and the request validators.</summary>
public static class EmailLimits
{
    /// <summary>The longest email address SMTP allows (64-character local part, @, 255-character domain).</summary>
    public const int MaxLength = 320;
}
