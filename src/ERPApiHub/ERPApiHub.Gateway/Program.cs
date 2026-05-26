using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8888");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.Audience = builder.Configuration["Authentication:Audience"];
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", true);
    });

builder.Services
    .AddAuthorizationBuilder()
    .AddPolicy("InternalGateway", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("BranchId");
    });

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transformBuilderContext =>
    {
        transformBuilderContext.AddRequestTransform(transformContext =>
        {
            transformContext.ProxyRequest.Headers.Remove("X-Branch-Id-Internal");

            var branchId = transformContext.HttpContext.User.FindFirst("BranchId")?.Value;
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

app.MapGet("/", () => Results.Ok(new { service = "erp-api-hub-gateway", status = "running" }));
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready");
app.MapReverseProxy().RequireAuthorization("InternalGateway");

app.Run();
