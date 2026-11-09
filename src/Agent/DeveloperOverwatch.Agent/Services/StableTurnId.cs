using System.Security.Cryptography;
using System.Text;

namespace DeveloperOverwatch.Agent.Services;

/// <summary>
/// Derives a conversation turn's id from the transcript content it was read from, so a retry, a
/// re-read, or a resumed session's copied history always produces the same id — which is how the
/// server tells a resend from new usage. A random id per send would count the same tokens again.
/// </summary>
internal static class StableTurnId
{
    public static string From(string sourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceId));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }
}
