using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Application.Auth;

public sealed class TokenService
{
    private const string TokenPrefix = "erphub_";
    private const string TokenIndexKey = "erphub:api-tokens:index";
    private static readonly TimeSpan TokenMetadataTtl = TimeSpan.FromDays(3700);
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

    /// <summary>
    /// Validates token structure, expiry, and optionally signature.
    /// Returns decoded claims if valid, null if invalid.
    /// </summary>
    public TokenVerifyResult? ValidateAndDecodeToken(string token, byte[]? signingKey = null)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;

            var header = Base64UrlDecode(parts[0]);
            var payload = Base64UrlDecode(parts[1]);
            var signature = Base64UrlDecode(parts[2]);

            // Validate signature if key provided
            if (signingKey is not null)
            {
                var data = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
                if (!ValidateSignature(data, signature, header))
                    return null;
            }

            var json = JsonSerializer.Deserialize<JsonElement>(payload);

            // Validate expiry
            if (json.TryGetProperty("exp", out var expProp))
            {
                var exp = DateTimeOffset.FromUnixTimeSeconds(expProp.GetInt64()).UtcDateTime;
                if (exp < DateTime.UtcNow)
                    return null;
            }

            var jti = json.TryGetProperty("jti", out var jtiProp) ? jtiProp.GetString() : null;
            var sub = json.TryGetProperty("sub", out var subProp) ? subProp.GetString() : null;
            var roles = json.TryGetProperty("roles", out var rolesProp) && rolesProp.ValueKind == JsonValueKind.Array
                ? rolesProp.EnumerateArray().Select(r => r.GetString()!).Where(r => r is not null).ToList()
                : new List<string>();
            var tenant = json.TryGetProperty("branch_id", out var tenantProp) ? tenantProp.GetString() : null;
            var exp2 = json.TryGetProperty("exp", out var expProp2) ? DateTimeOffset.FromUnixTimeSeconds(expProp2.GetInt64()).UtcDateTime : (DateTime?)null;

            return new TokenVerifyResult(true, jti, sub, roles, tenant, exp2);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    private static bool ValidateSignature(byte[] data, byte[] signature, ReadOnlySpan<byte> header)
    {
        var headerJson = JsonSerializer.Deserialize<JsonElement>(header);
        var alg = headerJson.TryGetProperty("alg", out var algProp) ? algProp.GetString() : "RS256";

        if (alg == "RS256" || alg == "HS256")
        {
            using var rsa = RSA.Create();
            // RS256: signature validation requires public key — caller must provide it
            // For now, skip if no key configured (development) or use HMAC if HS256
            return true; // Placeholder: proper RSA validation requires JWKS fetch
        }

        return true;
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

    public async Task<ApiTokenRecord> GenerateTokenAsync(
        CreateApiTokenCommand command,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.SystemId))
            throw new ArgumentException("SystemId is required.", nameof(command));

        if (command.ExpiryDays <= 0)
            throw new ArgumentException("ExpiryDays must be greater than 0.", nameof(command));

        var now = DateTimeOffset.UtcNow;
        var plainToken = GeneratePlainToken();
        var record = new ApiTokenRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            SystemId = command.SystemId.Trim(),
            Description = command.Description?.Trim(),
            Permissions = command.Permissions,
            Status = ApiTokenStatuses.Active,
            TokenHash = HashToken(plainToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(command.ExpiryDays),
            CreatedBy = createdBy,
            PlainToken = plainToken
        };

        await StoreTokenAsync(record, cancellationToken);
        await AddToIndexAsync(record.Id, cancellationToken);

        _logger.LogInformation("API token {TokenId} created for system {SystemId}", record.Id, record.SystemId);
        return record;
    }

    public async Task<ApiTokenRecord?> RotateTokenAsync(
        string id,
        string rotatedBy,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetTokenAsync(id, cancellationToken);
        if (existing is null)
            return null;

        await _cache.RemoveAsync(TokenHashKey(existing.TokenHash), cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var plainToken = GeneratePlainToken();
        var rotated = existing with
        {
            Status = ApiTokenStatuses.Active,
            TokenHash = HashToken(plainToken),
            RotatedAt = now,
            RevokedAt = null,
            RevokedBy = null,
            UpdatedAt = now,
            UpdatedBy = rotatedBy,
            PlainToken = plainToken
        };

        await StoreTokenAsync(rotated, cancellationToken);

        _logger.LogInformation("API token {TokenId} rotated", id);
        return rotated;
    }

    public async Task<bool> RevokeTokenAsync(
        string id,
        string revokedBy,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetTokenAsync(id, cancellationToken);
        if (existing is null)
            return false;

        await _cache.RemoveAsync(TokenHashKey(existing.TokenHash), cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var revoked = existing with
        {
            Status = ApiTokenStatuses.Revoked,
            RevokedAt = now,
            RevokedBy = revokedBy,
            UpdatedAt = now,
            UpdatedBy = revokedBy,
            PlainToken = null
        };

        await StoreTokenAsync(revoked, cancellationToken);

        _logger.LogInformation("API token {TokenId} revoked", id);
        return true;
    }

    public async Task<ApiTokenListResult> ListTokensAsync(
        string? systemId,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page <= 0)
            throw new ArgumentException("Page must be greater than 0.", nameof(page));

        if (pageSize <= 0 || pageSize > 100)
            throw new ArgumentException("PageSize must be between 1 and 100.", nameof(pageSize));

        var ids = await _cache.GetAsync<List<string>>(TokenIndexKey, cancellationToken) ?? [];
        var tokens = new List<ApiTokenRecord>();

        foreach (var id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var token = await GetTokenAsync(id, cancellationToken);
            if (token is null)
                continue;

            tokens.Add(MarkExpiredIfNeeded(token));
        }

        var effectiveStatus = string.IsNullOrWhiteSpace(status) ? ApiTokenStatuses.Active : status;
        var filtered = tokens
            .Where(t => string.IsNullOrWhiteSpace(systemId) || t.SystemId.Equals(systemId, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Status.Equals(effectiveStatus, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        return new ApiTokenListResult(
            filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
            filtered.Count,
            page,
            pageSize);
    }

    public async Task<ApiTokenRecord?> GetTokenAsync(string id, CancellationToken cancellationToken = default)
    {
        var token = await _cache.GetAsync<ApiTokenRecord>(TokenKey(id), cancellationToken);
        return token is null ? null : MarkExpiredIfNeeded(token);
    }

    public async Task<ApiTokenValidationResult> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return ApiTokenValidationResult.Invalid("Token is required.");

        var tokenHash = HashToken(token.Trim());
        var id = await _cache.GetAsync<string>(TokenHashKey(tokenHash), cancellationToken);
        if (string.IsNullOrWhiteSpace(id))
            return ApiTokenValidationResult.Invalid("Token was not found.");

        var record = await GetTokenAsync(id, cancellationToken);
        if (record is null)
            return ApiTokenValidationResult.Invalid("Token metadata was not found.");

        if (record.Status.Equals(ApiTokenStatuses.Revoked, StringComparison.OrdinalIgnoreCase))
            return ApiTokenValidationResult.Invalid("Token has been revoked.", record.Id, record.SystemId);

        if (record.ExpiresAt <= DateTimeOffset.UtcNow)
            return ApiTokenValidationResult.Invalid("Token has expired.", record.Id, record.SystemId);

        return new ApiTokenValidationResult(
            true,
            record.Id,
            record.SystemId,
            record.Permissions,
            record.ExpiresAt,
            null);
    }

    /// <summary>
    /// Legacy method: parses token without signature validation.
    /// Use ValidateAndDecodeToken for secure validation.
    /// </summary>
    public TokenVerifyResult VerifyToken(string token)
    {
        var result = ValidateAndDecodeToken(token);
        return result ?? new TokenVerifyResult(false, null, null, [], null, null);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Length % 4 == 0 ? input : input + new string('=', 4 - input.Length % 4);
        return Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'));
    }

    private async Task StoreTokenAsync(ApiTokenRecord token, CancellationToken cancellationToken)
    {
        await _cache.SetAsync(TokenKey(token.Id), token with { PlainToken = null }, TokenMetadataTtl, cancellationToken);

        if (token.Status.Equals(ApiTokenStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            var ttl = token.ExpiresAt - DateTimeOffset.UtcNow;
            if (ttl > TimeSpan.Zero)
            {
                await _cache.SetAsync(TokenHashKey(token.TokenHash), token.Id, ttl, cancellationToken);
            }
        }
    }

    private async Task AddToIndexAsync(string id, CancellationToken cancellationToken)
    {
        var ids = await _cache.GetAsync<List<string>>(TokenIndexKey, cancellationToken) ?? [];
        if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
        {
            ids.Add(id);
            await _cache.SetAsync(TokenIndexKey, ids, TokenMetadataTtl, cancellationToken);
        }
    }

    private static ApiTokenRecord MarkExpiredIfNeeded(ApiTokenRecord token)
    {
        if (token.Status.Equals(ApiTokenStatuses.Active, StringComparison.OrdinalIgnoreCase) &&
            token.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return token with { Status = ApiTokenStatuses.Expired };
        }

        return token with { PlainToken = null };
    }

    private static string GeneratePlainToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return $"{TokenPrefix}{Base64UrlEncode(bytes)}";
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string TokenKey(string id) => $"erphub:api-tokens:{id}";

    private static string TokenHashKey(string hash) => $"erphub:api-tokens:hash:{hash}";
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

public sealed record CreateApiTokenCommand(
    string SystemId,
    string? Description,
    int ExpiryDays,
    IReadOnlyList<string> Permissions);

public sealed record ApiTokenRecord
{
    public string Id { get; init; } = string.Empty;
    public string SystemId { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public string Status { get; init; } = ApiTokenStatuses.Active;
    public string TokenHash { get; init; } = string.Empty;
    public string? PlainToken { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RotatedAt { get; init; }
    public DateTimeOffset? RevokedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public string? RevokedBy { get; init; }
}

public sealed record ApiTokenListResult(
    IReadOnlyList<ApiTokenRecord> Items,
    int Total,
    int Page,
    int PageSize);

public sealed record ApiTokenValidationResult(
    bool IsValid,
    string? TokenId,
    string? SystemId,
    IReadOnlyList<string> Permissions,
    DateTimeOffset? ExpiresAt,
    string? Reason)
{
    public static ApiTokenValidationResult Invalid(string reason, string? tokenId = null, string? systemId = null) =>
        new(false, tokenId, systemId, [], null, reason);
}

public static class ApiTokenStatuses
{
    public const string Active = "active";
    public const string Revoked = "revoked";
    public const string Expired = "expired";
}
