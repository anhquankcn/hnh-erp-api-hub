using System.Security.Claims;
using ERPApiHub.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class BranchIdPolicyHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenBranchIdClaimExists_SucceedsAndStoresBranchId()
    {
        var httpContext = new DefaultHttpContext();
        var requirement = new BranchIdRequirement();
        var authContext = new AuthorizationHandlerContext(
            [requirement],
            CreateUser((BranchIdPolicyHandler.ClaimName, "SGN")),
            resource: null);
        var handler = CreateHandler(httpContext);

        await handler.HandleAsync(authContext);

        Assert.True(authContext.HasSucceeded);
        Assert.Equal("SGN", httpContext.Items[BranchIdPolicyHandler.HttpContextItemKey]);
    }

    [Fact]
    public async Task HandleAsync_WhenBranchIdClaimMissingAndSoftPolicy_SucceedsForAuthenticatedUser()
    {
        var requirement = new BranchIdRequirement();
        var authContext = new AuthorizationHandlerContext(
            [requirement],
            CreateUser(),
            resource: null);
        var handler = CreateHandler(new DefaultHttpContext());

        await handler.HandleAsync(authContext);

        Assert.True(authContext.HasSucceeded);
    }

    [Fact]
    public async Task HandleAsync_WhenBranchIdClaimMissingAndStrictPolicy_DoesNotSucceed()
    {
        var requirement = new BranchIdRequirement();
        var authContext = new AuthorizationHandlerContext(
            [requirement],
            CreateUser(),
            resource: null);
        var handler = CreateHandler(
            new DefaultHttpContext(),
            new KeycloakOptions { RequireBranchIdClaim = true });

        await handler.HandleAsync(authContext);

        Assert.False(authContext.HasSucceeded);
    }

    private static BranchIdPolicyHandler CreateHandler(
        HttpContext httpContext,
        KeycloakOptions? options = null)
    {
        return new BranchIdPolicyHandler(
            new HttpContextAccessor { HttpContext = httpContext },
            Options.Create(options ?? new KeycloakOptions()));
    }

    private static ClaimsPrincipal CreateUser(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(claim => new Claim(claim.Type, claim.Value)),
            authenticationType: "test");

        return new ClaimsPrincipal(identity);
    }
}
