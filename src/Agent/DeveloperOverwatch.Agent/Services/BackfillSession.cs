namespace DeveloperOverwatch.Agent.Services;

/// <summary>The Overwatch session id a Claude session is recorded under.</summary>
/// <param name="Mapped">True when the id came from the hooks' mapping file, false when it was derived.</param>
internal sealed record BackfillSession(string Id, bool Mapped);
