using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ERPApiHub.Application.Observability;

/// <summary>
/// OpenTelemetry configuration for ERP API Hub.
/// FRD refs: §15 (FR-OBS-001~003)
/// Call AddErpHubObservability() from Program.cs to enable.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>
    /// Register OpenTelemetry tracing, metrics, and logging.
    /// Requires NuGet: OpenTelemetry.Extensions.Hosting, OpenTelemetry.Exporter.Console (dev) / OTLP (prod)
    /// </summary>
    public static IServiceCollection AddErpHubObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "ERPApiHub";
        var serviceVersion = configuration["OpenTelemetry:ServiceVersion"] ?? "1.0.0";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new AlwaysOnSampler())
                    .AddSource(ErpHubMetrics.ActivitySource.Name)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = ctx =>
                            !ctx.Request.Path.StartsWithSegments("/health") &&
                            !ctx.Request.Path.StartsWithSegments("/v1/health");
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            activity.SetTag("tenant.id",
                                request.HttpContext.User?.FindFirst("BranchId")?.Value ?? "anonymous");
                        };
                    })
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    });
                }
                else
                {
                    tracing.AddConsoleExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter(ErpHubMetrics.ActivitySource.Name);

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    });
                }
                else
                {
                    metrics.AddConsoleExporter();
                }
            });

        return services;
    }
}