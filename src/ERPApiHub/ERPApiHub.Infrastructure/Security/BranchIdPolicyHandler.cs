using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace ERPApiHub.Infrastructure.Security;

public sealed class BranchIdRequirement : IAuthorizationRequirement
{
}

public sealed class BranchIdPolicyHandler(IHttpContextAccessor httpContextAccessor)
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
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
