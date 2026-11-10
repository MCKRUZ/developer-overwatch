using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace DeveloperOverwatch.Agent.Tests.Services;

public sealed partial class QueueProcessorPollTests
{
    internal sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _bodies = new();

        public Func<string, string, HttpStatusCode> Respond { get; set; } = (_, _) => HttpStatusCode.OK;

        /// <summary>When set and it returns a reply for a request's path and body, that status and body are sent
        /// instead of <see cref="Respond"/>'s: for a body the server writes, such as <c>{"code":"session_not_found"}</c>
        /// on a 404 or a start's duplicate outcome.</summary>
        public Func<string, string, (HttpStatusCode Status, string Body)?>? Reply { get; set; }

        public IReadOnlyList<string> Bodies => _bodies.ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            _bodies.Enqueue(body);
            var path = request.RequestUri!.AbsolutePath;
            var (status, reply) = Reply?.Invoke(path, body) ?? Default(path, body);
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }

        private (HttpStatusCode, string) Default(string path, string body)
        {
            var status = Respond(path, body);
            return (status, status == HttpStatusCode.OK ? OkReply(path, body) : "{}");
        }

        /// <summary>What the server answers: a start or end names its outcome, and <c>/events</c> lists
        /// every item it was sent as accepted.</summary>
        private static string OkReply(string path, string body)
        {
            using var doc = JsonDocument.Parse(body);
            if (path == "/api/sessions/start")
                return JsonSerializer.Serialize(new { sessionId = doc.RootElement.GetProperty("sessionId").GetString(), outcome = "accepted" });
            if (path.EndsWith("/end", StringComparison.Ordinal))
                return JsonSerializer.Serialize(new { sessionId = path.Split('/')[3], outcome = "accepted" });

            var accepted = new[] { "conversationTurns", "toolExecutions", "fileChanges", "toolFailures" }
                .Where(list => doc.RootElement.TryGetProperty(list, out _))
                .SelectMany(list => doc.RootElement.GetProperty(list).EnumerateArray())
                .Select(item => item.GetProperty("id").GetString())
                .ToList();
            return JsonSerializer.Serialize(new
            {
                eventsIngested = accepted.Count,
                results = new { accepted, duplicate = Array.Empty<string>(), rejected = Array.Empty<object>() }
            });
        }
    }
}
