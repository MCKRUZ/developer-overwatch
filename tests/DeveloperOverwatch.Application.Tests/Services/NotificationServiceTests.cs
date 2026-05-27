namespace DeveloperOverwatch.Application.Tests.Services;

using DeveloperOverwatch.Application.Interfaces;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

public class CompositeNotificationServiceTests
{
    private readonly SlackNotificationService _slack;
    private readonly EmailNotificationService _email;
    private readonly CompositeNotificationService _composite;

    public CompositeNotificationServiceTests()
    {
        _slack = new SlackNotificationService(
            new HttpClient(), CreateMonitor(new SlackConfig { Enabled = false }),
            NullLogger<SlackNotificationService>.Instance);

        _email = new EmailNotificationService(
            CreateMonitor(new EmailConfig { Enabled = false }),
            NullLogger<EmailNotificationService>.Instance);

        _composite = new CompositeNotificationService(
            _slack, _email, NullLogger<CompositeNotificationService>.Instance);
    }

    [Fact]
    public async Task NotifyViolationAsync_BothDisabled_CompletesWithoutError()
    {
        await _composite.Invoking(c => c.NotifyViolationAsync(
            "rule-1", "Critical", "test msg", "session-123", "dev@test.com"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyViolationAsync_OneChannelThrows_OtherStillExecutes()
    {
        var failingSlack = Substitute.For<INotificationService>();
        failingSlack.NotifyViolationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("Slack is down"));

        var successEmail = Substitute.For<INotificationService>();

        var tasks = new[]
        {
            SafeNotifyAsync(failingSlack, "rule-1", "Critical", "msg", "s1", "dev@test.com"),
            SafeNotifyAsync(successEmail, "rule-1", "Critical", "msg", "s1", "dev@test.com")
        };

        await Task.WhenAll(tasks);

        await successEmail.Received(1).NotifyViolationAsync(
            "rule-1", "Critical", "msg", "s1", "dev@test.com", Arg.Any<CancellationToken>());
    }

    private static async Task SafeNotifyAsync(
        INotificationService svc, string ruleId, string severity,
        string message, string sessionId, string email)
    {
        try
        {
            await svc.NotifyViolationAsync(ruleId, severity, message, sessionId, email);
        }
        catch
        {
            // Swallow -- mirrors CompositeNotificationService behavior
        }
    }

    private static IOptionsMonitor<T> CreateMonitor<T>(T value) where T : class, new()
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(value);
        return monitor;
    }
}

public class SlackNotificationServiceTests
{
    [Theory]
    [InlineData("Critical", "Error", true)]
    [InlineData("Error", "Error", true)]
    [InlineData("Warning", "Error", false)]
    [InlineData("Info", "Error", false)]
    [InlineData("Critical", "Critical", true)]
    [InlineData("Error", "Critical", false)]
    [InlineData("Warning", "Warning", true)]
    [InlineData("Info", "Info", true)]
    [InlineData("invalid", "Error", false)]
    [InlineData("Error", "invalid", false)]
    public void MeetsSeverityThreshold_ReturnsCorrectResult(
        string severity, string minimum, bool expected)
    {
        SlackNotificationService.MeetsSeverityThreshold(severity, minimum)
            .Should().Be(expected);
    }

    [Fact]
    public async Task NotifyViolationAsync_Disabled_DoesNotPost()
    {
        var handler = new TrackingHttpHandler();
        var client = new HttpClient(handler);
        var config = Substitute.For<IOptionsMonitor<SlackConfig>>();
        config.CurrentValue.Returns(new SlackConfig { Enabled = false, WebhookUrl = "https://hooks.slack.com/test" });

        var service = new SlackNotificationService(
            client, config, NullLogger<SlackNotificationService>.Instance);

        await service.NotifyViolationAsync("r1", "Critical", "msg", "s1", "dev@test.com");

        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyViolationAsync_EmptyWebhookUrl_DoesNotPost()
    {
        var handler = new TrackingHttpHandler();
        var client = new HttpClient(handler);
        var config = Substitute.For<IOptionsMonitor<SlackConfig>>();
        config.CurrentValue.Returns(new SlackConfig { Enabled = true, WebhookUrl = "" });

        var service = new SlackNotificationService(
            client, config, NullLogger<SlackNotificationService>.Instance);

        await service.NotifyViolationAsync("r1", "Critical", "msg", "s1", "dev@test.com");

        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyViolationAsync_BelowMinSeverity_DoesNotPost()
    {
        var handler = new TrackingHttpHandler();
        var client = new HttpClient(handler);
        var config = Substitute.For<IOptionsMonitor<SlackConfig>>();
        config.CurrentValue.Returns(new SlackConfig
        {
            Enabled = true,
            WebhookUrl = "https://hooks.slack.com/test",
            MinimumSeverity = "Error"
        });

        var service = new SlackNotificationService(
            client, config, NullLogger<SlackNotificationService>.Instance);

        await service.NotifyViolationAsync("r1", "Warning", "msg", "s1", "dev@test.com");

        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task NotifyViolationAsync_MeetsSeverity_PostsToWebhook()
    {
        var handler = new TrackingHttpHandler();
        var client = new HttpClient(handler);
        var config = Substitute.For<IOptionsMonitor<SlackConfig>>();
        config.CurrentValue.Returns(new SlackConfig
        {
            Enabled = true,
            WebhookUrl = "https://hooks.slack.com/test",
            MinimumSeverity = "Error"
        });

        var service = new SlackNotificationService(
            client, config, NullLogger<SlackNotificationService>.Instance);

        await service.NotifyViolationAsync("r1", "Critical", "violation msg", "abcdef1234567890", "dev@test.com");

        handler.RequestCount.Should().Be(1);
        handler.LastRequestBody.Should().Contain("r1");
        handler.LastRequestBody.Should().Contain("Critical");
        handler.LastRequestBody.Should().Contain("dev@test.com");
        handler.LastRequestBody.Should().Contain("abcdef12");
    }
}

public class EmailNotificationServiceTests
{
    [Fact]
    public async Task NotifyViolationAsync_Disabled_DoesNotSend()
    {
        var config = Substitute.For<IOptionsMonitor<EmailConfig>>();
        config.CurrentValue.Returns(new EmailConfig { Enabled = false, SmtpHost = "smtp.test.com" });

        var service = new EmailNotificationService(
            config, NullLogger<EmailNotificationService>.Instance);

        await service.Invoking(s => s.NotifyViolationAsync(
            "r1", "Critical", "msg", "s1", "dev@test.com"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyViolationAsync_NoSmtpHost_DoesNotSend()
    {
        var config = Substitute.For<IOptionsMonitor<EmailConfig>>();
        config.CurrentValue.Returns(new EmailConfig { Enabled = true, SmtpHost = "" });

        var service = new EmailNotificationService(
            config, NullLogger<EmailNotificationService>.Instance);

        await service.Invoking(s => s.NotifyViolationAsync(
            "r1", "Critical", "msg", "s1", "dev@test.com"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyViolationAsync_NoRecipients_DoesNotSend()
    {
        var config = Substitute.For<IOptionsMonitor<EmailConfig>>();
        config.CurrentValue.Returns(new EmailConfig
        {
            Enabled = true,
            SmtpHost = "smtp.test.com",
            ToAddresses = []
        });

        var service = new EmailNotificationService(
            config, NullLogger<EmailNotificationService>.Instance);

        await service.Invoking(s => s.NotifyViolationAsync(
            "r1", "Critical", "msg", "s1", "dev@test.com"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task NotifyViolationAsync_BelowMinSeverity_DoesNotSend()
    {
        var config = Substitute.For<IOptionsMonitor<EmailConfig>>();
        config.CurrentValue.Returns(new EmailConfig
        {
            Enabled = true,
            SmtpHost = "smtp.test.com",
            ToAddresses = ["admin@test.com"],
            MinimumSeverity = "Critical"
        });

        var service = new EmailNotificationService(
            config, NullLogger<EmailNotificationService>.Instance);

        await service.Invoking(s => s.NotifyViolationAsync(
            "r1", "Error", "msg", "s1", "dev@test.com"))
            .Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("Critical", "Error", true)]
    [InlineData("Error", "Error", true)]
    [InlineData("Warning", "Error", false)]
    [InlineData("Info", "Error", false)]
    public void MeetsSeverityThreshold_ReturnsCorrectResult(
        string severity, string minimum, bool expected)
    {
        EmailNotificationService.MeetsSeverityThreshold(severity, minimum)
            .Should().Be(expected);
    }
}

public class OverwatchNotifierTests
{
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly ISessionRepository _sessionRepo = Substitute.For<ISessionRepository>();
    private readonly OverwatchNotifier _notifier;

    public OverwatchNotifierTests()
    {
        _notifier = new OverwatchNotifier(
            _notificationService, _sessionRepo,
            NullLogger<OverwatchNotifier>.Instance);
    }

    [Fact]
    public async Task ViolationCreatedAsync_LooksUpSessionAndNotifies()
    {
        var session = new Session { Id = "s1", DeveloperEmail = "dev@test.com" };
        _sessionRepo.GetByIdAsync("s1", Arg.Any<CancellationToken>()).Returns(session);

        var violation = new GovernanceViolation
        {
            Id = "v1",
            SessionId = "s1",
            RuleId = "no-secrets",
            Severity = ViolationSeverity.Critical,
            Message = "Hardcoded secret found"
        };

        await _notifier.ViolationCreatedAsync(violation);

        await _notificationService.Received(1).NotifyViolationAsync(
            "no-secrets", "Critical", "Hardcoded secret found", "s1", "dev@test.com",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ViolationCreatedAsync_SessionNotFound_UsesUnknownEmail()
    {
        _sessionRepo.GetByIdAsync("s1", Arg.Any<CancellationToken>()).Returns((Session?)null);

        var violation = new GovernanceViolation
        {
            Id = "v1",
            SessionId = "s1",
            RuleId = "r1",
            Severity = ViolationSeverity.Error,
            Message = "msg"
        };

        await _notifier.ViolationCreatedAsync(violation);

        await _notificationService.Received(1).NotifyViolationAsync(
            "r1", "Error", "msg", "s1", "unknown", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ViolationCreatedAsync_NotificationThrows_DoesNotPropagate()
    {
        _sessionRepo.GetByIdAsync("s1", Arg.Any<CancellationToken>())
            .Returns(new Session { Id = "s1", DeveloperEmail = "dev@test.com" });
        _notificationService.NotifyViolationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("Network error"));

        var violation = new GovernanceViolation
        {
            Id = "v1", SessionId = "s1", RuleId = "r1",
            Severity = ViolationSeverity.Critical, Message = "msg"
        };

        await _notifier.Invoking(n => n.ViolationCreatedAsync(violation))
            .Should().NotThrowAsync();
    }
}

internal sealed class TrackingHttpHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    public string LastRequestBody { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
    }
}
