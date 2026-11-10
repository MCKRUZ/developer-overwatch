namespace DeveloperOverwatch.API.Tests.Delivery;

using System.Net;
using System.Text.Json;

internal enum RealServerFault
{
    CommitThenTimeout,
    ServerError,
    NotFoundWithoutCode,
    CommitThenDie
}

internal sealed class AgentDiedException() : Exception("injected: the agent died after the server committed");

/// <summary>Sits between the agent's HttpClient and the in-memory API. While the run is faulting it injects
/// seeded faults; it records every request's turn count, every status the real server returned, and every
/// item id the server reported rejected to the agent.</summary>
internal sealed class RealServerFaultHandler(int seed, int faultPolls) : DelegatingHandler
{
    private const double FaultRate = 0.4;
    private static readonly RealServerFault[] Faults = Enum.GetValues<RealServerFault>();

    private readonly Random _random = new(seed);

    public int Poll { get; set; }

    public Dictionary<RealServerFault, int> Fired { get; } = [];

    public Dictionary<HttpStatusCode, int> ServerStatuses { get; } = [];

    /// <summary>Each response from the real server other than 200, with its path and body.</summary>
    public List<string> Unexpected { get; } = [];

    public List<int> EventsTurnCounts { get; } = [];

    public HashSet<string> ReportedRejected { get; } = new(StringComparer.Ordinal);

    public int Requests { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests++;
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/events", StringComparison.Ordinal))
            EventsTurnCounts.Add(TurnCount(await request.Content!.ReadAsStringAsync(ct)));

        var fault = Poll <= faultPolls && _random.NextDouble() < FaultRate ? Faults[_random.Next(Faults.Length)] : (RealServerFault?)null;
        if (fault is { } f)
            Fired[f] = Fired.GetValueOrDefault(f) + 1;

        switch (fault)
        {
            case RealServerFault.ServerError:
                return new HttpResponseMessage(HttpStatusCode.InternalServerError) { RequestMessage = request };
            case RealServerFault.NotFoundWithoutCode:
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent(""), RequestMessage = request };
        }

        var response = await ForwardAsync(request, path, ct);
        switch (fault)
        {
            case RealServerFault.CommitThenTimeout:
                response.Dispose();
                throw new TaskCanceledException("injected: the response timed out after the server committed");
            case RealServerFault.CommitThenDie:
                response.Dispose();
                throw new AgentDiedException();
        }

        await RecordRejectedAsync(response, path, ct);
        return response;
    }

    private async Task<HttpResponseMessage> ForwardAsync(HttpRequestMessage request, string path, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        await response.Content.LoadIntoBufferAsync(ct);
        ServerStatuses[response.StatusCode] = ServerStatuses.GetValueOrDefault(response.StatusCode) + 1;
        if (response.StatusCode != HttpStatusCode.OK)
            Unexpected.Add($"poll {Poll} {path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        return response;
    }

    private async Task RecordRejectedAsync(HttpResponseMessage response, string path, CancellationToken ct)
    {
        if (response.StatusCode != HttpStatusCode.OK || !path.EndsWith("/events", StringComparison.Ordinal))
            return;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("results", out var results) && results.TryGetProperty("rejected", out var rejected))
            ReportedRejected.UnionWith(rejected.EnumerateArray().Select(r => r.GetProperty("id").GetString()!));
    }

    private static int TurnCount(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("conversationTurns", out var turns) ? turns.GetArrayLength() : 0;
    }
}
