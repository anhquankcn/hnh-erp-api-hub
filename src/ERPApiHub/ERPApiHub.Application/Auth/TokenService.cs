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
    private const string InvalidTokenReason = "Invalid token";
    private static readonly TimeSpan TokenMetadataTtl = TimeSpan.FromDays(3700);
    private static readonly TimeSpan JwksCacheTtl = TimeSpan.FromMinutes(10);
    private readonly object _jwksLock = new();
    private readonly HttpClient _httpClient;
    private readonly ICacheService _cache;
    private readonly IOptions<KeycloakTokenOptions> _keycloakOptions;
    private readonly ILogger<TokenService> _logger;
    private IReadOnlyList<JwksKey> _jwksKeys = [];
    private DateTimeOffset _jwksExpiresAt;

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

            var headerJson = JsonSerializer.Deserialize<JsonElement>(header);
            var alg = headerJson.TryGetProperty("alg", out var algProp) ? algProp.GetString() : null;

            // Validate signed tokens by default, and always validate when a caller supplied a key.
            if (signingKey is not null || alg is "RS256" or "HS256")
            {
                var data = Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}");
                if (!ValidateSignature(data, signature, headerJson, signingKey))
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

    private bool ValidateSignature(byte[] data, byte[] signature, JsonElement headerJson, byte[]? signingKey)
    {
        var alg = headerJson.TryGetProperty("alg", out var algProp) ? algProp.GetString() : "RS256";
        var kid = headerJson.TryGetProperty("kid", out var kidProp) ? kidProp.GetString() : null;

        if (alg == "HS256")
        {
            var secret = signingKey ?? Encoding.UTF8.GetBytes(_keycloakOptions.Value.ClientSecret);
            if (secret.Length == 0)
                return false;

            using var hmac = new HMACSHA256(secret);
            var computed = hmac.ComputeHash(data);
            return CryptographicOperations.FixedTimeEquals(computed, signature);
        }

        if (alg == "RS256")
        {
            foreach (var key in GetJwksKeys().Where(k => string.IsNullOrWhiteSpace(kid) || k.Kid == kid))
            {
                try
                {
                    using var rsa = RSA.Create();
                    rsa.ImportParameters(new RSAParameters
                    {
                        Modulus = Base64UrlDecode(key.N),
                        Exponent = Base64UrlDecode(key.E)
                    });

                    if (rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        return true;
                }
                catch (Exception ex) when (ex is ArgumentException or CryptographicException or FormatException)
                {
                    _logger.LogWarning(ex, "Invalid JWKS key encountered while validating token signature.");
                }
            }
        }

        return false;
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

    public async Task<ApiTokenIssueResult> GenerateTokenAsync(
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
            TokenLookupHash = HashTokenForLookup(plainToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(command.ExpiryDays),
            CreatedBy = createdBy
        };

        await StoreTokenAsync(record, cancellationToken);
        await AddToIndexAsync(record.Id, cancellationToken);

        _logger.LogInformation("API token {TokenId} created for system {SystemId}", record.Id, record.SystemId);
        return new ApiTokenIssueResult(record, plainToken);
    }

    public async Task<ApiTokenIssueResult?> RotateTokenAsync(
        string id,
        string rotatedBy,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetTokenAsync(id, cancellationToken);
        if (existing is null)
            return null;

        await _cache.RemoveAsync(TokenHashKey(existing.TokenHash), cancellationToken);
        await RemoveTokenLookupAsync(existing, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var plainToken = GeneratePlainToken();
        var rotated = existing with
        {
            Status = ApiTokenStatuses.Active,
            TokenHash = HashToken(plainToken),
            TokenLookupHash = HashTokenForLookup(plainToken),
            RotatedAt = now,
            RevokedAt = null,
            RevokedBy = null,
            UpdatedAt = now,
            UpdatedBy = rotatedBy
        };

        await StoreTokenAsync(rotated, cancellationToken);

        _logger.LogInformation("API token {TokenId} rotated", id);
        return new ApiTokenIssueResult(rotated, plainToken);
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
        await RemoveTokenLookupAsync(existing, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var revoked = existing with
        {
            Status = ApiTokenStatuses.Revoked,
            RevokedAt = now,
            RevokedBy = revokedBy,
            UpdatedAt = now,
            UpdatedBy = revokedBy
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
            return ApiTokenValidationResult.Invalid(InvalidTokenReason);

        var normalizedToken = token.Trim();
        var lookupHash = HashTokenForLookup(normalizedToken);
        var matchedId = await _cache.GetAsync<string>(TokenLookupKey(lookupHash), cancellationToken);
        if (string.IsNullOrWhiteSpace(matchedId))
            return ApiTokenValidationResult.Invalid(InvalidTokenReason);

        var matchedRecord = await GetTokenAsync(matchedId, cancellationToken);
        if (matchedRecord is null ||
            string.IsNullOrWhiteSpace(matchedRecord.TokenHash) ||
            !VerifyTokenHash(normalizedToken, matchedRecord.TokenHash))
        {
            return ApiTokenValidationResult.Invalid(InvalidTokenReason);
        }

        if (matchedRecord.Status.Equals(ApiTokenStatuses.Active, StringComparison.OrdinalIgnoreCase) &&
            matchedRecord.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return new ApiTokenValidationResult(
                true,
                matchedRecord.Id,
                matchedRecord.SystemId,
                matchedRecord.Permissions,
                matchedRecord.ExpiresAt,
                null);
        }

        return ApiTokenValidationResult.Invalid(InvalidTokenReason);
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
        await _cache.SetAsync(TokenKey(token.Id), token, TokenMetadataTtl, cancellationToken);

        if (token.Status.Equals(ApiTokenStatuses.Active, StringComparison.OrdinalIgnoreCase))
        {
            var ttl = token.ExpiresAt - DateTimeOffset.UtcNow;
            if (ttl > TimeSpan.Zero)
            {
                await _cache.SetAsync(TokenHashKey(token.TokenHash), token.Id, ttl, cancellationToken);
                if (!string.IsNullOrWhiteSpace(token.TokenLookupHash))
                {
                    await _cache.SetAsync(TokenLookupKey(token.TokenLookupHash), token.Id, ttl, cancellationToken);
                }
            }
        }
    }

    private async Task RemoveTokenLookupAsync(ApiTokenRecord token, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(token.TokenLookupHash))
        {
            await _cache.RemoveAsync(TokenLookupKey(token.TokenLookupHash), cancellationToken);
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

        return token;
    }

    private static string GeneratePlainToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return $"{TokenPrefix}{Base64UrlEncode(bytes)}";
    }

    private static string HashToken(string token)
    {
        return BCrypt.Net.BCrypt.HashPassword(token, workFactor: 12);
    }

    private static bool VerifyTokenHash(string token, string tokenHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(token, tokenHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    private static string HashTokenForLookup(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Base64UrlEncode(hash);
    }

    private IReadOnlyList<JwksKey> GetJwksKeys()
    {
        if (_jwksExpiresAt > DateTimeOffset.UtcNow)
            return _jwksKeys;

        lock (_jwksLock)
        {
            if (_jwksExpiresAt > DateTimeOffset.UtcNow)
                return _jwksKeys;

            var authority = _keycloakOptions.Value.Authority.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(authority))
                return [];

            var jwksUrl = $"{authority}/protocol/openid-connect/certs";
            var response = _httpClient.GetFromJsonAsync<JsonElement>(jwksUrl).GetAwaiter().GetResult();
            var keys = new List<JwksKey>();

            if (response.TryGetProperty("keys", out var keyArray) && keyArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var key in keyArray.EnumerateArray())
                {
                    var kty = key.TryGetProperty("kty", out var ktyProp) ? ktyProp.GetString() : null;
                    var n = key.TryGetProperty("n", out var nProp) ? nProp.GetString() : null;
                    var e = key.TryGetProperty("e", out var eProp) ? eProp.GetString() : null;

                    if (!string.Equals(kty, "RSA", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(n) ||
                        string.IsNullOrWhiteSpace(e))
                    {
                        continue;
                    }

                    var kid = key.TryGetProperty("kid", out var kidProp) ? kidProp.GetString() : null;
                    keys.Add(new JwksKey(kid, n, e));
                }
            }

            _jwksKeys = keys;
            _jwksExpiresAt = DateTimeOffset.UtcNow.Add(JwksCacheTtl);
            return _jwksKeys;
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string TokenKey(string id) => $"erphub:api-tokens:{id}";

    private static string TokenHashKey(string hash) => $"erphub:api-tokens:hash:{hash}";

    private static string TokenLookupKey(string hash) => $"erphub:api-tokens:lookup:{hash}";

    private sealed record JwksKey(string? Kid, string N, string E);
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

public sealed record ApiTokenIssueResult(ApiTokenRecord Token, string PlainToken);

public sealed record ApiTokenRecord
{
    public string Id { get; init; } = string.Empty;
    public string SystemId { get; init; } = string.Empty;
    public string? Description { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public string Status { get; init; } = ApiTokenStatuses.Active;
    public string TokenHash { get; init; } = string.Empty;
    public string TokenLookupHash { get; init; } = string.Empty;
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
