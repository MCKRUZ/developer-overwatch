namespace DeveloperOverwatch.Infrastructure.Governance;

using DeveloperOverwatch.Domain.Enums;
using DeveloperOverwatch.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public sealed class PolicyLoader
{
    private readonly GovernanceConfig _config;
    private readonly ILogger<PolicyLoader> _logger;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public PolicyLoader(IOptions<GovernanceConfig> config, ILogger<PolicyLoader> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public IReadOnlyList<GovernancePolicy> LoadPolicies()
    {
        if (!Directory.Exists(_config.PoliciesPath))
        {
            _logger.LogDebug("Policies directory {Path} not found, returning empty", _config.PoliciesPath);
            return [];
        }

        var files = Directory.GetFiles(_config.PoliciesPath, "*.yaml")
            .Concat(Directory.GetFiles(_config.PoliciesPath, "*.yml"))
            .ToArray();

        if (files.Length == 0)
            return [];

        var policies = new List<GovernancePolicy>();

        foreach (var file in files)
        {
            try
            {
                var yaml = File.ReadAllText(file);
                var doc = Deserializer.Deserialize<PolicyFileDto>(yaml);
                if (doc?.Policies is null) continue;

                foreach (var dto in doc.Policies)
                {
                    policies.Add(new GovernancePolicy
                    {
                        Id = dto.Id,
                        Name = dto.Name,
                        Severity = Enum.Parse<ViolationSeverity>(dto.Severity, ignoreCase: true),
                        Type = Enum.Parse<PolicyType>(dto.Type, ignoreCase: true),
                        Scan = dto.Scan.Select(s => Enum.Parse<ScanTarget>(s, ignoreCase: true)).ToList(),
                        Patterns = dto.Patterns ?? [],
                        ExcludePaths = dto.ExcludePaths ?? [],
                        Rule = dto.Rule
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse policy file {File}", file);
            }
        }

        _logger.LogInformation("Loaded {Count} governance policies from {Dir}", policies.Count, _config.PoliciesPath);
        return policies;
    }

    private sealed class PolicyFileDto
    {
        public List<PolicyDto>? Policies { get; set; }
    }

    private sealed class PolicyDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<string> Scan { get; set; } = [];
        public List<string>? Patterns { get; set; }
        public List<string>? ExcludePaths { get; set; }
        public string? Rule { get; set; }
    }
}
