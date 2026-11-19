namespace DeveloperOverwatch.Application.Queries.GetSessionContext;

using System.Text.Json;
using DeveloperOverwatch.Domain.Common;
using DeveloperOverwatch.Domain.Entities;
using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Interfaces;
using DeveloperOverwatch.Domain.Metering;
using MediatR;

public sealed class GetSessionContextQueryHandler : IRequestHandler<GetSessionContextQuery, Result<SessionContextView>>
{
    private readonly ISessionInsightRepository _repository;
    private readonly IModelContextWindowProvider _windows;

    public GetSessionContextQueryHandler(ISessionInsightRepository repository, IModelContextWindowProvider windows)
    {
        _repository = repository;
        _windows = windows;
    }

    public async Task<Result<SessionContextView>> Handle(GetSessionContextQuery request, CancellationToken cancellationToken)
    {
        var header = await _repository.GetContextHeaderAsync(request.SessionId, cancellationToken);
        if (header is null)
            return Result<SessionContextView>.Fail($"Session {request.SessionId} not found");

        var current = await _repository.GetLatestMainThreadContextAsync(request.SessionId, cancellationToken);
        var snapshot = await _repository.GetContextSnapshotAsync(request.SessionId, cancellationToken);
        var currentWindow = current is null ? (long?)null : _windows.ContextWindowTokensFor(current.Model);
        var peakModel = header.PeakContextModel ?? current?.Model;
        var peakWindow = header.PeakContextTokens is null ? (long?)null : _windows.ContextWindowTokensFor(peakModel);

        return Result<SessionContextView>.Success(new SessionContextView(
            _windows.AsOf,
            current?.Model,
            current?.ContextTokens,
            currentWindow,
            current is null ? null : ContextFill.Of(current.ContextTokens, currentWindow!.Value),
            header.PeakContextTokens,
            header.PeakContextTokens is null ? null : peakModel,
            peakWindow,
            header.PeakContextTokens is long peak ? ContextFill.Of(peak, peakWindow!.Value) : null,
            header.ToolErrorCount,
            header.LastToolErrorName,
            header.LastToolErrorAt,
            snapshot is null ? null : ToBreakout(snapshot),
            snapshot is null ? null : UnusedTools(snapshot)));
    }

    private ContextBreakoutView ToBreakout(SessionContextSnapshot snapshot)
    {
        var window = _windows.ContextWindowTokensFor(snapshot.Model);
        return new ContextBreakoutView(
            snapshot.TakenAt,
            snapshot.ClaudeCodeVersion,
            snapshot.Model,
            snapshot.MeasuredContextTokens,
            window,
            ContextFill.Of(snapshot.MeasuredContextTokens, window),
            snapshot.Categories
                .OrderBy(c => c.Category == ContextCategory.Conversation)
                .ThenByDescending(c => c.Tokens)
                .ThenBy(c => c.Category)
                .ThenBy(c => c.Server, StringComparer.Ordinal)
                .Select(c => new ContextCategoryView(CamelCase(c.Category), c.Server, c.Tokens))
                .ToList(),
            snapshot.Compactions
                .OrderByDescending(c => c.At)
                .Select(c => new ContextCompactionView(c.At, c.Trigger, c.PreTokens, c.PostTokens))
                .ToList());
    }

    private static IReadOnlyList<UnusedToolGroupView> UnusedTools(SessionContextSnapshot snapshot) =>
        snapshot.Tools
            .Where(t => !t.Used)
            .OrderByDescending(t => t.TokensPerRequest)
            .ThenBy(t => t.Group, StringComparer.Ordinal)
            .Select(t => new UnusedToolGroupView(t.Group, t.IsMcp, t.ToolCount, t.TokensPerRequest))
            .ToList();

    // The API writes enums in PascalCase; the dashboard and the Agent both spell categories in camelCase.
    private static string CamelCase(ContextCategory category) =>
        JsonNamingPolicy.CamelCase.ConvertName(category.ToString());
}
