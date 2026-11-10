namespace DeveloperOverwatch.Agent.Services;

/// <summary>Which sessions sit out the next poll after a transient failure. Each session backs off on its own,
/// 2^n seconds after its n-th failure in a row, capped at a minute, so one session the server keeps failing
/// never slows the others. Counted in polls, not wall time: a session is retried on the first poll at least
/// its backoff after the failure. Held in memory; a restart starts every session afresh.</summary>
internal sealed class SessionBackoff(int pollIntervalSeconds)
{
    private const double MaxBackoffSeconds = 60;

    private readonly Dictionary<string, (int Failures, long RetryAtPoll)> _sessions = new(StringComparer.Ordinal);
    private readonly int _pollIntervalSeconds = Math.Max(1, pollIntervalSeconds);
    private long _poll;

    /// <summary>Starts the next poll and returns the sessions still waiting out their backoff in it. A session
    /// not retried for a maximum backoff past its retry poll has nothing left to send (swept or purged), so it
    /// is forgotten; if it was only crowded out, it merely starts its backoff afresh.</summary>
    public IReadOnlyCollection<string> NextPoll()
    {
        _poll++;
        var forgetBefore = _poll - (long)Math.Ceiling(MaxBackoffSeconds / _pollIntervalSeconds);
        foreach (var gone in _sessions.Where(s => s.Value.RetryAtPoll < forgetBefore).Select(s => s.Key).ToList())
            _sessions.Remove(gone);
        return _sessions.Where(s => s.Value.RetryAtPoll > _poll).Select(s => s.Key).ToList();
    }

    public void Failed(string sessionId)
    {
        var failures = (_sessions.TryGetValue(sessionId, out var state) ? state.Failures : 0) + 1;
        var seconds = Math.Min(Math.Pow(2, failures), MaxBackoffSeconds);
        var pollsToWait = (long)Math.Ceiling(seconds / _pollIntervalSeconds);
        _sessions[sessionId] = (failures, _poll + pollsToWait);
    }

    public void Succeeded(string sessionId) => _sessions.Remove(sessionId);

    /// <summary>How many sessions are remembered as failing.</summary>
    public int Tracked => _sessions.Count;
}
