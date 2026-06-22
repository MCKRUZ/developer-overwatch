namespace DeveloperOverwatch.Application.Tests.Governance;

using DeveloperOverwatch.Infrastructure.Governance;
using FluentAssertions;
using Xunit;

public class JuryVoteAggregatorTests
{
    private static PanelistVerdict Violated(params string[] messages) =>
        new(Responded: true, Violated: true,
            Findings: messages.Select(m => new SemanticFinding(m, "evidence")).ToArray());

    private static PanelistVerdict Clean() => new(Responded: true, Violated: false, Findings: []);

    private static PanelistVerdict NoResponse() => new(Responded: false, Violated: false, Findings: []);

    [Fact]
    public void Aggregate_AllAgreeViolated_IsConsensusAndViolated()
    {
        var result = JuryVoteAggregator.Aggregate(
            [Violated("issue"), Violated("issue"), Violated("issue")], consensusThreshold: 0.5);

        result.Violated.Should().BeTrue();
        result.Bucket.Should().Be(ConsensusBucket.Consensus);
        result.ViolatedVotes.Should().Be(3);
        result.Responded.Should().Be(3);
    }

    [Fact]
    public void Aggregate_AllAgreeClean_IsConsensusAndNotViolated()
    {
        var result = JuryVoteAggregator.Aggregate([Clean(), Clean(), Clean()], consensusThreshold: 0.5);

        result.Violated.Should().BeFalse();
        result.Bucket.Should().Be(ConsensusBucket.Consensus);
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_MajorityViolated_RecordsViolation_OneDissenterIgnored()
    {
        // 2 of 3 flag — the single dissenting judge cannot suppress the finding.
        var result = JuryVoteAggregator.Aggregate(
            [Violated("real issue"), Violated("real issue"), Clean()], consensusThreshold: 0.5);

        result.Violated.Should().BeTrue();
        result.Bucket.Should().Be(ConsensusBucket.Split);
        result.ViolatedVotes.Should().Be(2);
    }

    [Fact]
    public void Aggregate_MinorityViolated_BelowThreshold_NoViolation()
    {
        // 1 of 3 flags — one over-eager judge cannot create a false positive.
        var result = JuryVoteAggregator.Aggregate(
            [Violated("noise"), Clean(), Clean()], consensusThreshold: 0.5);

        result.Violated.Should().BeFalse();
        result.Findings.Should().BeEmpty();
        result.Bucket.Should().Be(ConsensusBucket.Split);
    }

    [Fact]
    public void Aggregate_EvenSplit_IsConflictBucket()
    {
        var result = JuryVoteAggregator.Aggregate(
            [Violated("issue"), Clean()], consensusThreshold: 0.5);

        result.Bucket.Should().Be(ConsensusBucket.Conflict);
        result.Violated.Should().BeTrue("half meets the 0.5 threshold");
    }

    [Fact]
    public void Aggregate_FailedPanelistsExcludedFromVote()
    {
        // Only responders count toward the denominator: 2 responded, both violated → consensus.
        var result = JuryVoteAggregator.Aggregate(
            [Violated("issue"), Violated("issue"), NoResponse()], consensusThreshold: 0.5);

        result.Responded.Should().Be(2);
        result.Panelists.Should().Be(3);
        result.Violated.Should().BeTrue();
        result.Bucket.Should().Be(ConsensusBucket.Consensus);
    }

    [Fact]
    public void Aggregate_AllPanelistsFailed_NotViolated()
    {
        var result = JuryVoteAggregator.Aggregate(
            [NoResponse(), NoResponse()], consensusThreshold: 0.5);

        result.Violated.Should().BeFalse();
        result.Responded.Should().Be(0);
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_DuplicateFindingsAcrossJudges_AreDeduped()
    {
        var result = JuryVoteAggregator.Aggregate(
            [Violated("same problem"), Violated("SAME problem"), Violated("other")], consensusThreshold: 0.5);

        result.Findings.Should().HaveCount(2, "case-insensitive duplicate messages collapse to one");
    }

    [Fact]
    public void Aggregate_StricterThreshold_RequiresMoreVotes()
    {
        // 2 of 3 = 0.66; a 0.75 threshold rejects it.
        var result = JuryVoteAggregator.Aggregate(
            [Violated("issue"), Violated("issue"), Clean()], consensusThreshold: 0.75);

        result.Violated.Should().BeFalse();
    }
}
