using System.Text.Json.Nodes;

namespace ERPApiHub.Application.RateLimiting;

public sealed class KongConfigGenerator
{
    public JsonObject Generate(RateLimitOptions options)
    {
        var services = new JsonArray();

        foreach (EndpointType endpointType in Enum.GetValues<EndpointType>())
        {
            if (endpointType == EndpointType.Other)
            {
                continue;
            }

            services.Add(new JsonObject
            {
                ["name"] = $"erp-api-hub-{endpointType.ToString().ToLowerInvariant()}",
                ["plugins"] = BuildTierPlugins(options, endpointType)
            });
        }

        return new JsonObject
        {
            ["_format_version"] = "3.0",
            ["services"] = services
        };
    }

    private static JsonArray BuildTierPlugins(RateLimitOptions options, EndpointType endpointType)
    {
        var plugins = new JsonArray();

        foreach (RateLimitTier tier in Enum.GetValues<RateLimitTier>())
        {
            plugins.Add(new JsonObject
            {
                ["name"] = "rate-limiting",
                ["enabled"] = options.Enabled,
                ["tags"] = new JsonArray(tier.ToString(), endpointType.ToString()),
                ["config"] = new JsonObject
                {
                    ["minute"] = options.GetEffectiveLimit(tier, endpointType),
                    ["policy"] = "redis",
                    ["limit_by"] = "consumer",
                    ["redis_database"] = 0,
                    ["hide_client_headers"] = false
                }
            });
        }

        return plugins;
    }
}
