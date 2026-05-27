using DeveloperOverwatch.Agent.Services;
using FluentAssertions;
using Xunit;

namespace DeveloperOverwatch.Agent.Tests.Services;

public class DecisionDetectorTests
{
    private readonly DecisionDetector _sut = new();

    [Fact]
    public void IsOverride_BothReferenceFile_ReturnsTrue()
    {
        var result = _sut.IsOverride(
            lastAssistantToolOutput: "Wrote changes to src/app.cs",
            userNextToolInput: "Edit src/app.cs line 10",
            filePath: "src/app.cs");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsOverride_NullInputs_ReturnsFalse()
    {
        _sut.IsOverride(null, null, null).Should().BeFalse();
        _sut.IsOverride(null, "input", "file.cs").Should().BeFalse();
        _sut.IsOverride("output", null, "file.cs").Should().BeFalse();
        _sut.IsOverride("output", "input", null).Should().BeFalse();
    }

    [Fact]
    public void IsOverride_OnlyOneReferencesFile_ReturnsFalse()
    {
        var result = _sut.IsOverride(
            lastAssistantToolOutput: "Wrote changes to src/app.cs",
            userNextToolInput: "Edit something else entirely",
            filePath: "src/app.cs");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsRedirect_ContainsNo_ReturnsTrue()
    {
        _sut.IsRedirect("No, do it differently").Should().BeTrue();
    }

    [Fact]
    public void IsRedirect_ContainsInstead_ReturnsTrue()
    {
        _sut.IsRedirect("Use a factory instead").Should().BeTrue();
    }

    [Fact]
    public void IsRedirect_ContainsActually_ReturnsTrue()
    {
        _sut.IsRedirect("Actually, let's try another approach").Should().BeTrue();
    }

    [Fact]
    public void IsRedirect_NormalMessage_ReturnsFalse()
    {
        _sut.IsRedirect("Please add a new endpoint for users").Should().BeFalse();
    }

    [Fact]
    public void IsRedirect_NullMessage_ReturnsFalse()
    {
        _sut.IsRedirect(null).Should().BeFalse();
    }

    [Fact]
    public void IsRedirect_WordBoundary_NoFalsePositive()
    {
        _sut.IsRedirect("I cannot believe how fast this is").Should().BeFalse();
    }

    [Fact]
    public void IsManualEdit_DifferentHashes_ReturnsTrue()
    {
        var result = _sut.IsManualEdit(
            beforeHash: "abc123",
            afterHash: "def456",
            sessionStartHash: "abc123");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsManualEdit_SameBeforeAfter_ReturnsFalse()
    {
        var result = _sut.IsManualEdit(
            beforeHash: "abc123",
            afterHash: "abc123",
            sessionStartHash: "abc123");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsManualEdit_NullHash_ReturnsFalse()
    {
        _sut.IsManualEdit(null, "abc", "abc").Should().BeFalse();
        _sut.IsManualEdit("abc", null, "abc").Should().BeFalse();
        _sut.IsManualEdit("abc", "abc", null).Should().BeFalse();
    }

    [Fact]
    public void IsDecisionMoment_AnyHeuristicFires_ReturnsTrue()
    {
        // Only redirect fires (via "actually")
        var result = _sut.IsDecisionMoment(
            lastAssistantToolOutput: null,
            userNextToolInput: null,
            filePath: null,
            userMessage: "Actually, skip that",
            beforeHash: null,
            afterHash: null,
            sessionStartHash: null);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsDecisionMoment_NoneMatch_ReturnsFalse()
    {
        var result = _sut.IsDecisionMoment(
            lastAssistantToolOutput: null,
            userNextToolInput: null,
            filePath: null,
            userMessage: "Please add a new endpoint",
            beforeHash: "same",
            afterHash: "same",
            sessionStartHash: "same");

        result.Should().BeFalse();
    }
}
