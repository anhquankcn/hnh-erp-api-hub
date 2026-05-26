using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace ERPApiHub.Infrastructure.Security;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKeycloakJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var keycloakOptions = configuration
            .GetSection(KeycloakOptions.SectionName)
            .Get<KeycloakOptions>() ?? new KeycloakOptions();

        if (environment.IsDevelopment())
        {
            keycloakOptions.RequireHttpsMetadata = false;
        }

        services
            .AddOptions<KeycloakOptions>()
            .Bind(configuration.GetSection(KeycloakOptions.SectionName))
            .PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.Authority))
                {
                    options.Authority = KeycloakOptions.DefaultAuthority;
                }

                if (string.IsNullOrWhiteSpace(options.Audience))
                {
                    options.Audience = KeycloakOptions.DefaultAudience;
                }

                if (environment.IsDevelopment())
                {
                    options.RequireHttpsMetadata = false;
                }
            })
            .ValidateDataAnnotations()
            .Validate(options => Uri.TryCreate(options.Authority, UriKind.Absolute, out _), "Keycloak authority must be an absolute URI.")
            .ValidateOnStart();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = keycloakOptions.Authority;
                options.Audience = keycloakOptions.Audience;
                options.RequireHttpsMetadata = keycloakOptions.RequireHttpsMetadata;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = keycloakOptions.Authority,
                    ValidateAudience = true,
                    ValidAudience = keycloakOptions.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorizationHandler, BranchIdPolicyHandler>();
        services.AddAuthorization(options =>
        {
            var branchIdRequirement = new BranchIdRequirement();

            options.AddPolicy(BranchIdPolicyHandler.PolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(branchIdRequirement);
            });

            options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(branchIdRequirement)
                .Build();
        });

        return services;
    }
}
