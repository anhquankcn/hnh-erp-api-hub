using ERPApiHub.Infrastructure;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Worker.Consumers;
using ERPApiHub.Worker.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8009");
builder.Services.AddErpHubInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ErpIngestionConsumer>();
builder.Services
    .AddHealthChecks()
    .AddDbContextCheck<ErpHubDbContext>("postgres", tags: ["ready"])
    .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: ["ready"]);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "erp-worker", status = "running" }));
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
