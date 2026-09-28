namespace DeveloperOverwatch.Infrastructure.Persistence;

public sealed class DatabaseConfig
{
    /// <summary>Use an Azure AD bearer token (via the Container App's own Managed Identity)
    /// instead of a credential embedded in <c>Database:ConnectionString</c>. The connection
    /// string should omit User Id/Password when this is true — just Server/Database/Encrypt.
    /// Requires the identity to be provisioned as an external-provider SQL user with the roles
    /// this app needs (db_datareader/db_datawriter, plus db_ddladmin since migrations run
    /// in-process on startup).</summary>
    public bool UseManagedIdentityAuth { get; init; }

    /// <summary>Client ID of a user-assigned managed identity. Leave null to use the
    /// system-assigned identity (or local dev credentials via DefaultAzureCredential).</summary>
    public string? ManagedIdentityClientId { get; init; }
}
