using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Infrastructure.Security;

public sealed class BranchIdRequirement : IAuthorizationRequirement
{
}

public sealed class BranchIdPolicyHandler(
    IHttpContextAccessor httpContextAccessor,
    IOptions<KeycloakOptions> keycloakOptions)
    : AuthorizationHandler<BranchIdRequirement>
{
    public const string PolicyName = "BranchId";
    public const string ClaimName = "branch_id";
    public const string HttpContextItemKey = "BranchId";

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BranchIdRequirement requirement)
    {
        var branchId = context.User.FindFirst(ClaimName)?.Value;

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            httpContextAccessor.HttpContext?.Items.TryAdd(HttpContextItemKey, branchId);
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (!keycloakOptions.Value.RequireBranchIdClaim &&
            context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
