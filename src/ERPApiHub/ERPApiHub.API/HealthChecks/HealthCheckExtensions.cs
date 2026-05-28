using System.Text.Json;
using ERPApiHub.Infrastructure.ErpNext;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ERPApiHub.API.HealthChecks;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddErpApiHubHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var erpNextOptions = configuration.GetSection(ErpNextOptions.SectionName).Get<ErpNextOptions>() ?? new();

        services.AddHttpClient<ErpNextHealthCheck>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(erpNextOptions.TimeoutSeconds > 0 ? erpNextOptions.TimeoutSeconds : 30);
        });

        services.AddHealthChecks()
            .AddCheck<DbContextHealthCheck>("postgres", tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"])
            .AddCheck<ErpNextHealthCheck>("erpnext", tags: ["ready"]);

        return services;
    }

    public static IEndpointRouteBuilder MapErpApiHubHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthCheckResponseAsync
        }).AllowAnonymous();

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteHealthCheckResponseAsync
        }).AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthCheckResponseAsync
        }).AllowAnonymous();

        return endpoints;
    }

    private static Task WriteHealthCheckResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                duration = entry.Value.Duration.TotalMilliseconds
            })
        };

        return JsonSerializer.SerializeAsync(context.Response.Body, payload, cancellationToken: context.RequestAborted);
    }
}
