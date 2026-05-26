using ERPApiHub.Infrastructure.Security;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://0.0.0.0:8888");
builder.Services.AddKeycloakJwtAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks();
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transformBuilderContext =>
    {
        transformBuilderContext.AddRequestTransform(transformContext =>
        {
            var branchId = transformContext.HttpContext.User.FindFirst(BranchIdPolicyHandler.ClaimName)?.Value;

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                transformContext.ProxyRequest.Headers.Remove("X-Branch-Id-Internal");
                transformContext.ProxyRequest.Headers.TryAddWithoutValidation("X-Branch-Id-Internal", branchId);
            }

            return ValueTask.CompletedTask;
        });
    });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { service = "erp-api-hub-gateway", status = "running" }))
    .AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();
app.MapReverseProxy().RequireAuthorization(BranchIdPolicyHandler.PolicyName);

app.Run();
