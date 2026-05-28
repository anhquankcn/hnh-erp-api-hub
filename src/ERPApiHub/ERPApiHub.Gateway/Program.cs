using ERPApiHub.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8888");
builder.Services.AddKeycloakJwtAuthentication(builder.Configuration, builder.Environment);

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transformBuilderContext =>
    {
        transformBuilderContext.AddRequestTransform(transformContext =>
        {
            transformContext.ProxyRequest.Headers.Remove("X-Branch-Id-Internal");

            var branchId = transformContext.HttpContext.User.FindFirst(BranchIdPolicyHandler.ClaimName)?.Value;
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Branch-Id-Internal", branchId);
            }

            if (!transformContext.ProxyRequest.Headers.Contains("X-Correlation-ID"))
            {
                var correlationId = transformContext.HttpContext.TraceIdentifier;
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
            }

            return ValueTask.CompletedTask;
        });
    });

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "erp-api-hub-gateway", status = "running" }))
    .AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();
app.MapHealthChecks("/health/ready")
    .AllowAnonymous();
app.MapReverseProxy().RequireAuthorization(BranchIdPolicyHandler.PolicyName);

app.Run();