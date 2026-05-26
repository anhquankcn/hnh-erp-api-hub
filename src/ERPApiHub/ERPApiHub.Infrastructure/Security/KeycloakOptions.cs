using System.ComponentModel.DataAnnotations;

namespace ERPApiHub.Infrastructure.Security;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";
    public const string DefaultAuthority = "http://localhost:8080/realms/HNHTravel-SGN";
    public const string DefaultAudience = "erp-api-hub";

    [Required]
    public string Authority { get; set; } = DefaultAuthority;

    [Required]
    public string Audience { get; set; } = DefaultAudience;

    public bool RequireHttpsMetadata { get; set; } = true;
}
