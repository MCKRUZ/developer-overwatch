using DeveloperOverwatch.Agent.Cli;
using DeveloperOverwatch.Agent.Models;
using DeveloperOverwatch.Agent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var config = AgentConfig.Load(Path.Combine(AgentConfig.DefaultOverwatchDir, "config.json"));

if (args.Length > 0)
    return await CommandDispatcher.RunAsync(args, Console.Out, Console.Error, config, TimeProvider.System);

var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddConsole();
    })
    .ConfigureServices(services =>
    {
        services.AddSingleton(config);
        services.AddSingleton<IAgentModeSource>(_ => new ConfigFileModeSource(config.ConfigPath));
        services.AddSingleton<IEventQueue>(_ => new EventQueue(config.QueueDbPath));
        services.AddSingleton<IUsageStore>(_ => new UsageStore(config.QueueDbPath));
        services.AddSingleton<IPrivacyRedactor>(_ => new PrivacyRedactor(config.RedactionPatterns));
        services.AddSingleton<IDecisionDetector, DecisionDetector>();
        services.AddSingleton<IConversationCapture, ConversationCapture>();
        services.AddSingleton<IConversationCursorStore>(_ => new ConversationCursorStore(config.OverwatchDir));
        services.AddSingleton<IGitReader, GitReader>();

        services.AddHttpClient<IApiClient, ApiClient>(client =>
        {
            client.BaseAddress = new Uri(config.ApiUrl.TrimEnd('/') + "/");
            if (config.ApiKey is not null)
                client.DefaultRequestHeaders.Add("X-Api-Key", config.ApiKey);
        });

        services.AddHostedService<QueueProcessor>();
    })
    .Build();

Console.WriteLine(config.Mode == AgentMode.Local
    ? "Overwatch Agent started — local mode, usage stays on this machine"
    : $"Overwatch Agent started — shipping to {config.ApiUrl}");
Console.WriteLine($"Queue: {config.QueueDbPath}");
Console.WriteLine($"Poll interval: {config.PollIntervalSeconds}s, Batch size: {config.BatchSize}");

await host.RunAsync();
return 0;
