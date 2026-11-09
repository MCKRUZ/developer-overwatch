using DeveloperOverwatch.Agent.Services;
using FluentAssertions;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class PrivacyRedactorTests
{
    private static readonly IReadOnlyList<string> DefaultPatterns =
    [
        @"(?i)\b(api[\s_-]?key|tokens?|secrets?|passwords?|credentials?|auth)\b\s*[=:]?\s*(?=\S*\d)[A-Za-z0-9_\-\.\/+]{8,}",
        @"(?i)bearer\s+[A-Za-z0-9\-._~+/]+=*",
        @"sk-[A-Za-z0-9]{20,}",
        @"ghp_[A-Za-z0-9]{36}",
        @"-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----[\s\S]*?-----END",
    ];

    private readonly PrivacyRedactor _sut = new(DefaultPatterns);

    [Fact]
    public void Redact_ApiKey_ReplacesWithRedacted()
    {
        var input = "config: api_key=secret123 rest";

        var result = _sut.Redact(input);

        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("secret123");
    }

    [Fact]
    public void Redact_BearerToken_ReplacesWithRedacted()
    {
        var input = "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.sig";

        var result = _sut.Redact(input);

        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("eyJhbGciOiJIUzI1NiJ9");
    }

    [Fact]
    public void Redact_GitHubToken_ReplacesWithRedacted()
    {
        var input = "Using token ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghij for auth";

        var result = _sut.Redact(input);

        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("ghp_");
    }

    [Fact]
    public void Redact_OpenAiKey_ReplacesWithRedacted()
    {
        var input = "OPENAI_API_KEY=sk-abcdefghijklmnopqrstuvwxyz1234567890";

        var result = _sut.Redact(input);

        result.Should().Contain("[REDACTED]");
        result.Should().NotContain("sk-abcdefghijklmnopqrstuvwxyz1234567890");
    }

    [Fact]
    public void Redact_NoSensitiveContent_ReturnsOriginal()
    {
        var input = "just a normal log line with no secrets";

        var result = _sut.Redact(input);

        result.Should().Be(input);
    }

    [Fact]
    public void Redact_EmptyString_ReturnsEmpty()
    {
        var result = _sut.Redact(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Redact_NullString_ReturnsNull()
    {
        var result = _sut.Redact(null!);

        result.Should().BeNull();
    }

    [Fact]
    public void Redact_JsonPayload_RedactsOnlyStringLeafValues_WithoutCorruptingStructure()
    {
        // Regression test: a whole-string regex pass over compact JSON is unsafe — the
        // unbounded `\S+` in the password pattern has no whitespace to stop it, so it swallows
        // through the closing quote/brace/comma of the current field and into the next turn's
        // JSON, merging two separate conversation turns into one with the wrong role surviving.
        var input = "{\"conversationTurns\":[" +
            "{\"id\":\"t1\",\"role\":\"user\",\"content\":\"here's my password: Efrain123!\",\"turnIndex\":1}," +
            "{\"id\":\"t2\",\"role\":\"assistant\",\"content\":\"I won't store that.\",\"turnIndex\":2}" +
            "]}";

        var result = _sut.Redact(input);

        using var doc = System.Text.Json.JsonDocument.Parse(result);
        var turns = doc.RootElement.GetProperty("conversationTurns");

        turns.GetArrayLength().Should().Be(2);
        turns[0].GetProperty("role").GetString().Should().Be("user");
        turns[0].GetProperty("content").GetString().Should().Contain("[REDACTED]");
        turns[0].GetProperty("content").GetString().Should().NotContain("Efrain123!");
        turns[1].GetProperty("role").GetString().Should().Be("assistant");
        turns[1].GetProperty("content").GetString().Should().Be("I won't store that.");
    }

    [Fact]
    public void ContainsSensitive_WithSecret_ReturnsTrue()
    {
        var input = "password=hunter2222";

        _sut.ContainsSensitive(input).Should().BeTrue();
    }

    [Fact]
    public void ContainsSensitive_WithSafeContent_ReturnsFalse()
    {
        var input = "just some normal text";

        _sut.ContainsSensitive(input).Should().BeFalse();
    }

    [Fact]
    public void Redact_HeaderStyleSecretWithSpaceSeparator_IsRedacted()
    {
        // HTTP-header-style secrets ("x-api-key <value>") use a space, not `=`/`:` — the
        // original pattern required `=`/`:` and let these through uncaught.
        var input = "x-api-key 28e09c001e0c4e56b0e3fa67032799f6 should be right header";

        var result = _sut.Redact(input);

        result.Should().NotContain("28e09c001e0c4e56b0e3fa67032799f6");
        _sut.ContainsSensitive(input).Should().BeTrue();
    }

    [Fact]
    public void ContainsSensitive_ShortWordAfterKeyword_ReturnsFalse()
    {
        // The value must be >=8 chars so ordinary sentences aren't flagged as secrets.
        var input = "the api key policy states this";

        _sut.ContainsSensitive(input).Should().BeFalse();
    }

    [Fact]
    public void Redact_ApiKeyWithSpaceConnectorAndDigits_IsRedacted()
    {
        // "api key" written as two words with a space is at least as common as "api-key"/
        // "api_key" — the connector must tolerate a space too, not just `_`/`-`.
        var input = "here's my api key 123456713";

        var result = _sut.Redact(input);

        result.Should().NotContain("123456713");
    }

    [Theory]
    [InlineData("Now let's update ClaudeLlmClient to support Managed Identity auth alongside the existing static-key path.")]
    [InlineData("check policies/default.yaml's no-secrets-in-conversation pattern")]
    [InlineData("the semantic secrets-detection policy")]
    public void ContainsSensitive_OrdinaryProseNearKeywords_ReturnsFalse(string input)
    {
        // Regression: an earlier version of this pattern flagged ordinary English words/compound
        // terms following "auth"/"secret" (no digits in the "value") as if they were credentials.
        _sut.ContainsSensitive(input).Should().BeFalse();
    }

    [Fact]
    public void ContainsSensitive_AuthAsSubstringOfOrdinaryWord_ReturnsFalse()
    {
        // \b on the keyword must stop "auth" from matching inside "authorization"/"author" etc.
        var input = "the Authorization header must be present on every request";

        _sut.ContainsSensitive(input).Should().BeFalse();
    }
}
