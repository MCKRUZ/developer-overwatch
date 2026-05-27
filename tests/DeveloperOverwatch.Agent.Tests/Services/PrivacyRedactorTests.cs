using DeveloperOverwatch.Agent.Services;
using FluentAssertions;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed class PrivacyRedactorTests
{
    private static readonly IReadOnlyList<string> DefaultPatterns =
    [
        @"(?i)(api[_-]?key|token|secret|password|credential|auth)\s*[=:]\s*\S+",
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
    public void ContainsSensitive_WithSecret_ReturnsTrue()
    {
        var input = "password=hunter2";

        _sut.ContainsSensitive(input).Should().BeTrue();
    }

    [Fact]
    public void ContainsSensitive_WithSafeContent_ReturnsFalse()
    {
        var input = "just some normal text";

        _sut.ContainsSensitive(input).Should().BeFalse();
    }
}
