using System.Net.Http.Json;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Application.Auth;

public sealed class TokenService
{
    private readonly HttpClient _httpClient;
    private readonly ICacheService _cache;
    private readonly IOptions<KeycloakTokenOptions> _keycloakOptions;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        IHttpClientFactory httpClientFactory,
        ICacheService cache,
        IOptions<KeycloakTokenOptions> keycloakOptions,
        ILogger<TokenService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _cache = cache;
        _keycloakOptions = keycloakOptions;
        _logger = logger;
    }

    public async Task<TokenRefreshResult?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var options = _keycloakOptions.Value;
        var url = $"{options.Authority}/protocol/openid-connect/token";

        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["refresh_token"] = refreshToken
        };

        var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(formData), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Keycloak refresh failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var accessToken = content.GetProperty("access_token").GetString();
        var newRefreshToken = content.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

        return new TokenRefreshResult(accessToken!, newRefreshToken);
    }

    public async Task RevokeTokenAsync(string jti, TimeSpan remainingLifetime, CancellationToken cancellationToken = default)
    {
        await _cache.SetAsync($"erphub:token-blacklist:{jti}", true, remainingLifetime, cancellationToken);
        _logger.LogInformation("Token {Jti} blacklisted", jti);
    }

    public Task<bool> IsTokenBlacklistedAsync(string jti, CancellationToken cancellationToken = default)
        => _cache.ExistsAsync($"erphub:token-blacklist:{jti}", cancellationToken);

    public TokenVerifyResult VerifyToken(string token)
    {
        try
        {
            // Manual JWT parsing without System.IdentityModel.Tokens.Jwt
            var parts = token.Split('.');
            if (parts.Length != 3) return new TokenVerifyResult(false, null, null, [], null, null);

            var payload = Base64UrlDecode(parts[1]);
            var json = JsonSerializer.Deserialize<JsonElement>(payload);

            var jti = json.TryGetProperty("jti", out var jtiProp) ? jtiProp.GetString() : null;
            var sub = json.TryGetProperty("sub", out var subProp) ? subProp.GetString() : null;
            var roles = json.TryGetProperty("roles", out var rolesProp) && rolesProp.ValueKind == JsonValueKind.Array
                ? rolesProp.EnumerateArray().Select(r => r.GetString()!).Where(r => r is not null).ToList()
                : new List<string>();
            var tenant = json.TryGetProperty("branch_id", out var tenantProp) ? tenantProp.GetString() : null;
            var exp = json.TryGetProperty("exp", out var expProp) ? DateTimeOffset.FromUnixTimeSeconds(expProp.GetInt64()).UtcDateTime : (DateTime?)null;

            return new TokenVerifyResult(true, jti, sub, roles, tenant, exp);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token verification failed");
            return new TokenVerifyResult(false, null, null, [], null, null);
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Length % 4 == 0 ? input : input + new string('=', 4 - input.Length % 4);
        return Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'));
    }
}

public sealed record TokenRefreshResult(string AccessToken, string? RefreshToken);
public sealed record TokenVerifyResult(
    bool IsValid,
    string? Jti,
    string? Sub,
    IReadOnlyList<string> Roles,
    string? Tenant,
    DateTime? Expiry);

public sealed class KeycloakTokenOptions
{
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
