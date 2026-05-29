using System.Text.Json;
using ERPApiHub.Application.HealthChecks;
using ERPApiHub.Application.Polling;
using ERPApiHub.Infrastructure;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.Health;
using ERPApiHub.Worker.Consumers;
using ERPApiHub.Worker.Workers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8009");
builder.Services.AddErpHubInfrastructure(builder.Configuration);
builder.Services.Configure<TenantHealthCheckOptions>(builder.Configuration.GetSection(TenantHealthCheckOptions.SectionName));
builder.Services.Configure<LinkFieldValidationOptions>(
    builder.Configuration.GetSection(LinkFieldValidationOptions.SectionName));
builder.Services.Configure<PollingOptions>(builder.Configuration.GetSection(PollingOptions.SectionName));
builder.Services.AddSingleton<DoctypePollingRegistry>();
builder.Services.AddScoped<TenantHealthCheckService>();
builder.Services.AddScoped<LinkFieldValidator>();
builder.Services.AddHostedService<ErpIngestionConsumer>();
builder.Services.AddHostedService<PollingWorker>();
builder.Services.AddHostedService<TenantHealthCheckWorker>();
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<ErpHubDbContext>("postgres", tags: ["ready", "startup"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "erp-worker", status = "running" }));
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = WriteHealthCheckResponseAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthCheckResponseAsync
});
app.MapHealthChecks("/health/startup", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("startup"),
    ResponseWriter = WriteHealthCheckResponseAsync
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthCheckResponseAsync
});

app.Run();

static Task WriteHealthCheckResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var payload = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Status.ToString())
    };

    return JsonSerializer.SerializeAsync(context.Response.Body, payload);
}
