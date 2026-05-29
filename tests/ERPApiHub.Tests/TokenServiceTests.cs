using System.Net;
using System.Text;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class TokenServiceTests
{
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IHttpClientFactory> _httpClientFactory = new();
    private readonly Mock<ILogger<TokenService>> _logger = new();

    private static IOptions<KeycloakTokenOptions> CreateOptions() => Options.Create(new KeycloakTokenOptions
    {
        Authority = "https://keycloak.example.com/realms/erp",
        ClientId = "erp-api-hub",
        ClientSecret = "secret"
    });

    private TokenService CreateService(HttpResponseMessage? response = null, Func<HttpRequestMessage, CancellationToken, Task>? onRequest = null)
    {
        var handler = new StubHttpMessageHandler(response ?? new HttpResponseMessage(HttpStatusCode.OK), onRequest);
        var httpClient = new HttpClient(handler);

        _httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(httpClient);
        _httpClientFactory.Setup(x => x.CreateClient()).Returns(httpClient);

        return new TokenService(
            _httpClientFactory.Object,
            _cache.Object,
            CreateOptions(),
            _logger.Object);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenKeycloakReturnsSuccess_ReturnsTokens()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent(new
            {
                access_token = "access-token",
                refresh_token = "refresh-token"
            })
        };

        var service = CreateService(response, async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://keycloak.example.com/realms/erp/protocol/openid-connect/token", request.RequestUri!.ToString());

            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("grant_type=refresh_token", form);
            Assert.Contains("client_id=erp-api-hub", form);
            Assert.Contains("client_secret=secret", form);
            Assert.Contains("refresh_token=old-refresh-token", form);
        });

        var result = await service.RefreshTokenAsync("old-refresh-token", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
    }

    [Fact]
    public async Task RefreshTokenAsync_WhenKeycloakReturnsFailure_ReturnsNull()
    {
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest);
        var service = CreateService(response);

        var result = await service.RefreshTokenAsync("old-refresh-token", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task RevokeTokenAsync_SetsBlacklistKeyWithRemainingLifetime()
    {
        var service = CreateService();
        var ttl = TimeSpan.FromMinutes(12);

        await service.RevokeTokenAsync("jti-123", ttl, CancellationToken.None);

        _cache.Verify(x => x.SetAsync(
            "erphub:token-blacklist:jti-123",
            true,
            ttl,
            CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task GenerateTokenAsync_ReturnsPlainTokenOnceAndCachesMetadataOnly()
    {
        ApiTokenRecord? cachedRecord = null;
        _cache.Setup(x => x.SetAsync(
                It.Is<string>(key => key.StartsWith("erphub:api-tokens:", StringComparison.Ordinal)),
                It.IsAny<ApiTokenRecord>(),
                It.IsAny<TimeSpan?>(),
                CancellationToken.None))
            .Callback<string, ApiTokenRecord, TimeSpan?, CancellationToken>((_, record, _, _) => cachedRecord = record)
            .Returns(Task.CompletedTask);

        _cache.Setup(x => x.GetAsync<List<string>>("erphub:api-tokens:index", CancellationToken.None))
            .ReturnsAsync([]);

        var service = CreateService();

        var result = await service.GenerateTokenAsync(
            new CreateApiTokenCommand("erpnext", "ERPNext", 30, ["read"]),
            "tester",
            CancellationToken.None);

        Assert.StartsWith("erphub_", result.PlainToken, StringComparison.Ordinal);
        Assert.Equal(result.Token.Id, cachedRecord?.Id);
        Assert.DoesNotContain(
            typeof(ApiTokenRecord).GetProperties(),
            property => property.Name.Equals("PlainToken", StringComparison.Ordinal));

        var cachedJson = JsonSerializer.Serialize(cachedRecord);
        Assert.DoesNotContain(result.PlainToken, cachedJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateTokenAsync_WhenTokenIsValid_ReturnsTokenMetadata()
    {
        var record = CreateApiTokenRecord("token-1", "shared-secret", ApiTokenStatuses.Active, DateTimeOffset.UtcNow.AddDays(1));
        SetupTokenIndex(record);

        var service = CreateService();

        var result = await service.ValidateTokenAsync("shared-secret", CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal("token-1", result.TokenId);
        Assert.Equal("erpnext", result.SystemId);
        Assert.Equal(["read"], result.Permissions);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData(ApiTokenStatuses.Revoked, 1)]
    [InlineData(ApiTokenStatuses.Active, -1)]
    public async Task ValidateTokenAsync_WhenMatchingTokenCannotBeUsed_ReturnsUnifiedInvalidTokenReason(
        string status,
        int expiryDays)
    {
        var record = CreateApiTokenRecord("token-1", "shared-secret", status, DateTimeOffset.UtcNow.AddDays(expiryDays));
        SetupTokenIndex(record);

        var service = CreateService();

        var result = await service.ValidateTokenAsync("shared-secret", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("Invalid token", result.Reason);
        Assert.Null(result.TokenId);
        Assert.Null(result.SystemId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing-token")]
    public async Task ValidateTokenAsync_WhenTokenIsMissingOrUnknown_ReturnsUnifiedInvalidTokenReason(string token)
    {
        _cache.Setup(x => x.GetAsync<List<string>>("erphub:api-tokens:index", CancellationToken.None))
            .ReturnsAsync([]);

        var service = CreateService();

        var result = await service.ValidateTokenAsync(token, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("Invalid token", result.Reason);
        Assert.Null(result.TokenId);
        Assert.Null(result.SystemId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IsTokenBlacklistedAsync_ReturnsCacheExistenceResult(bool exists)
    {
        _cache.Setup(x => x.ExistsAsync("erphub:token-blacklist:jti-123", CancellationToken.None))
            .ReturnsAsync(exists);

        var service = CreateService();

        var result = await service.IsTokenBlacklistedAsync("jti-123", CancellationToken.None);

        Assert.Equal(exists, result);
    }

    [Fact]
    public void ValidateAndDecodeToken_WhenTokenIsValid_ReturnsClaims()
    {
        var expiry = DateTimeOffset.UtcNow.AddMinutes(30);
        var token = CreateJwt(new
        {
            jti = "jti-123",
            sub = "user-123",
            roles = new[] { "Admin", "Ingestion" },
            branch_id = "SGN",
            exp = expiry.ToUnixTimeSeconds()
        });

        var service = CreateService();

        var result = service.ValidateAndDecodeToken(token);

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal("jti-123", result.Jti);
        Assert.Equal("user-123", result.Sub);
        Assert.Equal(new[] { "Admin", "Ingestion" }, result.Roles);
        Assert.Equal("SGN", result.Tenant);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(expiry.ToUnixTimeSeconds()).UtcDateTime, result.Expiry);
    }

    [Fact]
    public void ValidateAndDecodeToken_WhenTokenIsExpired_ReturnsNull()
    {
        var token = CreateJwt(new
        {
            jti = "jti-123",
            sub = "user-123",
            exp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds()
        });

        var service = CreateService();

        var result = service.ValidateAndDecodeToken(token);

        Assert.Null(result);
    }

    [Fact]
    public void ValidateAndDecodeToken_WhenTokenIsMalformed_ReturnsNull()
    {
        var service = CreateService();

        var result = service.ValidateAndDecodeToken("not-a-jwt");

        Assert.Null(result);
    }

    [Fact]
    public void ValidateAndDecodeToken_ExtractsExpectedClaims()
    {
        var expiry = DateTimeOffset.UtcNow.AddHours(1);
        var token = CreateJwt(new
        {
            jti = "claim-jti",
            sub = "claim-sub",
            roles = new[] { "Reader", "Writer" },
            branch_id = "HAN",
            exp = expiry.ToUnixTimeSeconds()
        });

        var service = CreateService();

        var result = service.ValidateAndDecodeToken(token);

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Equal("claim-jti", result.Jti);
        Assert.Equal("claim-sub", result.Sub);
        Assert.Equal(new[] { "Reader", "Writer" }, result.Roles);
        Assert.Equal("HAN", result.Tenant);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(expiry.ToUnixTimeSeconds()).UtcDateTime, result.Expiry);
    }

    private static StringContent JsonContent<T>(T value)
        => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static string CreateJwt<T>(T payload)
    {
        var header = new { alg = "none", typ = "JWT" };
        return string.Join(".", Base64UrlEncode(header), Base64UrlEncode(payload), Base64UrlEncode(Array.Empty<byte>()));
    }

    private static string Base64UrlEncode<T>(T value)
        => Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(value));

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ApiTokenRecord CreateApiTokenRecord(
        string id,
        string plainToken,
        string status,
        DateTimeOffset expiresAt) => new()
    {
        Id = id,
        SystemId = "erpnext",
        Permissions = ["read"],
        Status = status,
        TokenHash = BCrypt.Net.BCrypt.HashPassword(plainToken, workFactor: 4),
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        ExpiresAt = expiresAt,
        CreatedBy = "tester"
    };

    private void SetupTokenIndex(params ApiTokenRecord[] records)
    {
        _cache.Setup(x => x.GetAsync<List<string>>("erphub:api-tokens:index", CancellationToken.None))
            .ReturnsAsync(records.Select(record => record.Id).ToList());

        foreach (var record in records)
        {
            _cache.Setup(x => x.GetAsync<ApiTokenRecord>($"erphub:api-tokens:{record.Id}", CancellationToken.None))
                .ReturnsAsync(record);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        private readonly Func<HttpRequestMessage, CancellationToken, Task>? _onRequest;

        public StubHttpMessageHandler(HttpResponseMessage response, Func<HttpRequestMessage, CancellationToken, Task>? onRequest)
        {
            _response = response;
            _onRequest = onRequest;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_onRequest is not null)
            {
                await _onRequest(request, cancellationToken);
            }

            return _response;
        }
    }
}
