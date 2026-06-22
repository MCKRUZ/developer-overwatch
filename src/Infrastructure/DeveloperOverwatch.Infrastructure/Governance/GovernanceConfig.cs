namespace DeveloperOverwatch.Infrastructure.Governance;

public sealed class GovernanceConfig
{
    public string PoliciesPath { get; init; } = "policies";
    public int DriftWindowHours { get; init; } = 72;
    public int DriftThreshold { get; init; } = 3;
    public double DriftAlpha { get; init; } = 0.3;
    public int DriftBucketHours { get; init; } = 4;
    public string? LlmApiUrl { get; init; }
    public string? LlmApiKey { get; init; }
    public string LlmModel { get; init; } = "claude-sonnet-4-6";
    public int LlmMaxTokens { get; init; } = 1024;
    public int LlmTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Number of independent LLM judges to poll for each semantic rule. <c>1</c> (default) is a
    /// single judge — identical to the pre-jury behavior. Values &gt; 1 form a panel whose votes
    /// are reduced by majority (see <see cref="SemanticConsensusThreshold"/>), cutting the false
    /// positives of any one judge. Ignored when <see cref="SemanticJuryPersonas"/> is set.
    /// </summary>
    public int SemanticJurySize { get; init; } = 1;

    /// <summary>
    /// Optional reviewer "lenses": one judge per persona, each appended to the auditor system
    /// prompt. When non-empty, the panel size equals the persona count and
    /// <see cref="SemanticJurySize"/> is ignored. Empty/null ⇒ identical judges.
    /// </summary>
    public IReadOnlyList<string>? SemanticJuryPersonas { get; init; }

    /// <summary>
    /// Fraction of responding judges that must flag a rule before a violation is recorded.
    /// Default <c>0.5</c> (simple majority). Only applies when the panel has more than one judge.
    /// </summary>
    public double SemanticConsensusThreshold { get; init; } = 0.5;
}
