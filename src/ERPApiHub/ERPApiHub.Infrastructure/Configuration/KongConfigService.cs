using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Infrastructure.Configuration;

public sealed class KongConfigService
{
    private readonly ILogger<KongConfigService> _logger;

    public KongConfigService(ILogger<KongConfigService> logger)
    {
        _logger = logger;
    }

    public KongValidationResult ValidateConfig(string configYaml)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            // Parse YAML line-by-line, filter comments and empty lines
            var lines = configYaml.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd())
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                .ToList();

            var yaml = string.Join("\n", lines);

            // Structural checks — look for top-level keys (not inside comments)
            var hasServices = Regex.IsMatch(yaml, @"^services:\s*$", RegexOptions.Multiline);
            var hasPlugins = Regex.IsMatch(yaml, @"^plugins:\s*$", RegexOptions.Multiline);
            var hasConsumers = Regex.IsMatch(yaml, @"^consumers:\s*$", RegexOptions.Multiline);

            // Entity checks with context awareness (match specific YAML key-value patterns)
            var hasErpApiHub = lines.Any(l => Regex.IsMatch(l, @"^\s+-?\s*name:\s+erp-api-hub\s*$"));
            var hasErpNext = lines.Any(l => Regex.IsMatch(l, @"^\s+-?\s*name:\s+erpnext\s*$"));
            var hasJwt = lines.Any(l => Regex.IsMatch(l, @"^\s+-?\s*name:\s+jwt\s*$"));
            var hasRateLimit = lines.Any(l => Regex.IsMatch(l, @"^\s+-?\s*name:\s+rate-limiting\s*$"));
            var hasAcl = lines.Any(l => Regex.IsMatch(l, @"^\s+-?\s*name:\s+acl\s*$"));
            var hasConsumer = lines.Any(l => Regex.IsMatch(l, @"^\s+-?\s*username:\s+erphub-client\s*$"));

            if (!hasServices) errors.Add("No services defined");
            if (!hasErpApiHub) errors.Add("Missing service: erp-api-hub");
            if (!hasErpNext) errors.Add("Missing service: erpnext");
            if (!hasJwt) errors.Add("Missing JWT plugin");
            if (!hasRateLimit) errors.Add("Missing rate-limiting plugin");
            if (!hasAcl) errors.Add("Missing ACL plugin");
            if (!hasConsumer) warnings.Add("Missing consumer: erphub-client");

            // Section warnings
            if (!hasPlugins) warnings.Add("No plugins: section found");
            if (!hasConsumers) warnings.Add("No consumers: section found");

            // Validate Redis config for rate-limiting
            var redisHostMatch = Regex.Match(yaml, @"redis_host:\s*(\S+)", RegexOptions.Multiline);
            var redisPortMatch = Regex.Match(yaml, @"redis_port:\s*(\d+)", RegexOptions.Multiline);
            if (hasRateLimit && (!redisHostMatch.Success || !redisPortMatch.Success))
            {
                warnings.Add("Rate-limiting plugin may be missing Redis configuration");
            }

            if (errors.Count == 0)
            {
                _logger.LogInformation("Kong config validation passed");
                return new KongValidationResult(true, [], warnings);
            }

            _logger.LogWarning("Kong config validation failed: {Errors}", string.Join(", ", errors));
            return new KongValidationResult(false, errors, warnings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kong config parse failed");
            return new KongValidationResult(false, ["Config parse error: " + ex.Message], warnings);
        }
    }
}

public sealed record KongValidationResult(bool IsValid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);
