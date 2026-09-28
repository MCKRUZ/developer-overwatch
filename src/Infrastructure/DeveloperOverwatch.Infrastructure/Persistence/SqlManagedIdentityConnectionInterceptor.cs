namespace DeveloperOverwatch.Infrastructure.Persistence;

using System.Data.Common;
using Azure.Core;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
/// Attaches an Azure AD access token to every SqlConnection EF Core opens, in place of a
/// credential embedded in the connection string. Deliberately does NOT rely on SqlClient's own
/// built-in "Authentication=Active Directory Managed Identity" connection-string mode — a sibling
/// project (epr-agentic) found the equivalent ODBC driver mode only queries classic Azure VM
/// IMDS, which Azure Container Apps never exposes (ACA exposes IDENTITY_ENDPOINT instead), so the
/// driver-internal path can silently hang to timeout instead of authenticating. Acquiring the
/// token ourselves via Azure.Identity and setting it directly sidesteps that assumption entirely.
/// </summary>
public sealed class SqlManagedIdentityConnectionInterceptor(TokenCredential credential) : DbConnectionInterceptor
{
    private static readonly string[] SqlScope = ["https://database.windows.net/.default"];

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqlConnection sqlConnection)
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(SqlScope), cancellationToken);
            sqlConnection.AccessToken = token.Token;
        }

        return result;
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (connection is SqlConnection sqlConnection)
        {
            var token = credential.GetToken(new TokenRequestContext(SqlScope), default);
            sqlConnection.AccessToken = token.Token;
        }

        return result;
    }
}
