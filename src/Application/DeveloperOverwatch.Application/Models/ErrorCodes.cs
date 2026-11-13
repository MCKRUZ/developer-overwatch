namespace DeveloperOverwatch.Application.Models;

/// <summary>The <see cref="Domain.Common.Result{T}.Error"/> values a caller branches on. Compare by equality: the
/// API maps each to its status code.</summary>
public static class ErrorCodes
{
    /// <summary>No item has the requested id.</summary>
    public const string NotFound = "not_found";

    /// <summary>The item would break a one-per-key rule, such as one budget per subject and period.</summary>
    public const string AlreadyExists = "already_exists";
}
