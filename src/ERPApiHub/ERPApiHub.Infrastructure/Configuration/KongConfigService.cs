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
            // Simple YAML validation without external packages
            var lines = configYaml.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var yaml = string.Join("\n", lines.Select(l => l.TrimEnd()));

            var hasServices = yaml.Contains("services:", StringComparison.Ordinal);
            var hasErpApiHub = yaml.Contains("name: erp-api-hub", StringComparison.Ordinal);
            var hasErpNext = yaml.Contains("name: erpnext", StringComparison.Ordinal);
            var hasJwt = yaml.Contains("name: jwt", StringComparison.Ordinal);
            var hasRateLimit = yaml.Contains("name: rate-limiting", StringComparison.Ordinal);
            var hasAcl = yaml.Contains("name: acl", StringComparison.Ordinal);
            var hasConsumer = yaml.Contains("username: erphub-client", StringComparison.Ordinal);

            if (!hasServices) errors.Add("No services defined");
            if (!hasErpApiHub) errors.Add("Missing service: erp-api-hub");
            if (!hasErpNext) errors.Add("Missing service: erpnext");
            if (!hasJwt) errors.Add("Missing JWT plugin");
            if (!hasRateLimit) errors.Add("Missing rate-limiting plugin");
            if (!hasAcl) errors.Add("Missing ACL plugin");
            if (!hasConsumer) warnings.Add("Missing consumer: erphub-client");

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
