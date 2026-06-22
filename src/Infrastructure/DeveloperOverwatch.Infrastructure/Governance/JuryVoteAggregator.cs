namespace DeveloperOverwatch.Infrastructure.Governance;

/// <summary>How divided the panel was on whether a rule was violated.</summary>
public enum ConsensusBucket
{
    /// <summary>Unanimous (all judges agreed either way).</summary>
    Consensus,

    /// <summary>A clear majority, but not unanimous.</summary>
    Split,

    /// <summary>Near-even — a human should review.</summary>
    Conflict
}

/// <summary>A single semantic finding extracted from one judge's response.</summary>
public sealed record SemanticFinding(string Message, string? Evidence);

/// <summary>One judge's verdict on a rule. Non-responders (call failed or unparseable) are excluded from voting.</summary>
public sealed record PanelistVerdict(bool Responded, bool Violated, IReadOnlyList<SemanticFinding> Findings);

/// <summary>The reduced verdict of the whole panel.</summary>
public sealed record JuryConsensus(
    bool Violated,
    int Responded,
    int Panelists,
    int ViolatedVotes,
    ConsensusBucket Bucket,
    IReadOnlyList<SemanticFinding> Findings);

/// <summary>
/// Pure, stateless reduction of a panel of <see cref="PanelistVerdict"/>s to a single
/// <see cref="JuryConsensus"/> by majority vote. No I/O — exhaustively unit-testable.
/// </summary>
/// <remarks>
/// Only responders sway the vote (the "robust to one bad judge" property). A rule is recorded
/// as violated when the fraction of responders flagging it meets the consensus threshold.
/// Findings are merged from the judges who voted "violated" and de-duplicated by message.
/// </remarks>
public static class JuryVoteAggregator
{
    public static JuryConsensus Aggregate(
        IReadOnlyList<PanelistVerdict> verdicts, double consensusThreshold)
    {
        ArgumentNullException.ThrowIfNull(verdicts);

        var panelists = verdicts.Count;
        var responders = verdicts.Where(v => v.Responded).ToArray();
        var responded = responders.Length;

        if (responded == 0)
            return new JuryConsensus(false, 0, panelists, 0, ConsensusBucket.Conflict, []);

        var violatedVotes = responders.Count(v => v.Violated);
        var violated = (double)violatedVotes / responded >= consensusThreshold;
        var bucket = Bucketize(violatedVotes, responded);

        var findings = violated
            ? responders
                .Where(v => v.Violated)
                .SelectMany(v => v.Findings)
                .GroupBy(f => f.Message, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToArray()
            : [];

        return new JuryConsensus(violated, responded, panelists, violatedVotes, bucket, findings);
    }

    private static ConsensusBucket Bucketize(int violatedVotes, int responded)
    {
        if (violatedVotes == 0 || violatedVotes == responded)
            return ConsensusBucket.Consensus;

        var ratio = (double)violatedVotes / responded;
        return ratio is >= 0.4 and <= 0.6 ? ConsensusBucket.Conflict : ConsensusBucket.Split;
    }
}
