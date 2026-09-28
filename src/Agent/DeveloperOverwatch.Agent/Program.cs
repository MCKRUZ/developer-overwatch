using System.Text.Json;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var config = LoadConfig();

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddConsole();
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton(config);
        services.AddSingleton<IEventQueue>(_ => new EventQueue(config.QueueDbPath));
        services.AddSingleton<IPrivacyRedactor>(_ => new PrivacyRedactor(config.RedactionPatterns));
        services.AddSingleton<IDecisionDetector, DecisionDetector>();
        services.AddSingleton<IConversationCapture, ConversationCapture>();
        services.AddSingleton<IConversationCursorStore>(_ => new ConversationCursorStore(config.OverwatchDir));
        services.AddSingleton<IGitFallbackWriter, GitFallbackWriter>();

        services.AddHttpClient<IApiClient, ApiClient>(client =>
        {
            client.BaseAddress = new Uri(config.ApiUrl.TrimEnd('/') + "/");
            if (config.ApiKey is not null)
                client.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey);
        });

        services.AddHostedService<QueueProcessor>();
    })
    .Build();

Console.WriteLine($"Overwatch Agent started — shipping to {config.ApiUrl}");
Console.WriteLine($"Queue: {config.QueueDbPath}");
Console.WriteLine($"Poll interval: {config.PollIntervalSeconds}s, Batch size: {config.BatchSize}");

await host.RunAsync();

static AgentConfig LoadConfig()
{
    var configPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".overwatch", "config.json");

    if (!File.Exists(configPath))
        return new AgentConfig();

    try
    {
        var json = File.ReadAllText(configPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<AgentConfig>(json, options) ?? new AgentConfig();
    }
    catch
    {
        return new AgentConfig();
    }
}
