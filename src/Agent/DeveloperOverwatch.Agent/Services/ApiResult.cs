namespace DeveloperOverwatch.Agent.Services;

/// <summary>How a whole POST ended.</summary>
public enum PostStatus
{
    /// <summary>200: the server took the request. A start or end was accepted or was a duplicate; an
    /// <c>/events</c> POST reports each item's own outcome in <see cref="ApiResult.Items"/>.</summary>
    Ok,

    /// <summary>400: the server refused the request as invalid. That is a verdict on the request, not on each
    /// item: a request of several items is split and sent again; one item refused alone is rejected.</summary>
    Rejected,

    /// <summary>404 with code <c>session_not_found</c>: the session is not on the server and never will be.</summary>
    SessionNotFound,

    /// <summary>413: the request is over the server's size limit. A request of several items is split and
    /// sent again; one item too large alone is held and retried, never rejected.</summary>
    TooLarge,

    /// <summary>Anything else (network error, timeout, 5xx, 401/403, a 404 without the code): retried later.</summary>
    Transient
}

/// <summary>The server's answer for one item of an <c>/events</c> POST.</summary>
public enum ItemOutcome
{
    Accepted,
    Duplicate,
    Rejected
}

/// <param name="Rejected">Each rejected id, with the server's reason.</param>
public sealed record ItemResults(
    IReadOnlySet<string> Accepted, IReadOnlySet<string> Duplicate, IReadOnlyDictionary<string, string> Rejected);

/// <summary>How a POST to the server ended, and for an <c>/events</c> POST, how each item in it ended. A
/// status other than <see cref="PostStatus.Ok"/> covers the whole request; see <see cref="HalvingPoster"/>
/// for how it becomes each item's outcome.</summary>
/// <param name="Items">Null when the 200 lists no results: a start, an end, or a server older than
/// per-item results. Every item then counts as accepted.</param>
/// <param name="Reason">Why the server refused the request.</param>
public sealed record ApiResult(PostStatus Status, ItemResults? Items = null, string? Reason = null)
{
    public static readonly ApiResult Success = new(PostStatus.Ok);
    public static readonly ApiResult Failed = new(PostStatus.Transient);
    public static readonly ApiResult NotFound = new(PostStatus.SessionNotFound);
    public static readonly ApiResult Rejected = new(PostStatus.Rejected);

    /// <summary>The item's outcome in a 200. Null when the POST did not succeed, or when the server's
    /// results leave the item out: it stays pending and is sent again.</summary>
    public ItemOutcome? OutcomeOf(string id)
    {
        if (Status != PostStatus.Ok)
            return null;
        if (Items is null || Items.Accepted.Contains(id))
            return ItemOutcome.Accepted;
        if (Items.Duplicate.Contains(id))
            return ItemOutcome.Duplicate;
        return Items.Rejected.ContainsKey(id) ? ItemOutcome.Rejected : null;
    }

    public string? RejectionReason(string id) => Items?.Rejected.GetValueOrDefault(id) ?? Reason;
}
