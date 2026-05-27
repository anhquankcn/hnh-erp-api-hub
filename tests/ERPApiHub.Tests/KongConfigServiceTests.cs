using ERPApiHub.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class KongConfigServiceTests
{
    private readonly Mock<ILogger<KongConfigService>> _logger = new();

    private KongConfigService CreateService() => new(_logger.Object);

    [Fact]
    public async Task ValidateAsync_WhenKongYamlIsValid_ReturnsValidWithNoErrors()
    {
        var service = CreateService();

        var result = await service.ValidateAsync(ValidKongYaml(), CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WhenServicesSectionIsMissing_ReturnsInvalidWithServicesError()
    {
        var yaml = ValidKongYaml().Replace("services:\n", string.Empty);
        var service = CreateService();

        var result = await service.ValidateAsync(yaml, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("services", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_WhenJwtPluginIsMissing_ReturnsInvalidWithJwtError()
    {
        var yaml = ValidKongYaml().Replace("  - name: jwt\n", string.Empty);
        var service = CreateService();

        var result = await service.ValidateAsync(yaml, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("jwt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_WhenRateLimitingPluginIsMissing_ReturnsInvalidWithRateLimitingError()
    {
        var yaml = ValidKongYaml().Replace("  - name: rate-limiting\n    config:\n      minute: 100\n      policy: redis\n      redis_host: redis\n      redis_port: 6379\n", string.Empty);
        var service = CreateService();

        var result = await service.ValidateAsync(yaml, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("rate-limiting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_WhenErpHubClientConsumerIsMissing_ReturnsInvalidWithConsumerError()
    {
        var yaml = ValidKongYaml().Replace("  - username: erphub-client\n", string.Empty);
        var service = CreateService();

        var result = await service.ValidateAsync(yaml, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("erphub-client", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ValidateAsync_WhenYamlHasComments_ReturnsValidWithNoErrors()
    {
        var yaml = """
        # Kong declarative config
        services:
          # ERP API Hub upstream
          - name: erp-api-hub
            url: http://erp-api-hub:8080
          - name: erpnext
            url: http://erpnext:8000

        plugins:
          # Authentication
          - name: jwt
          - name: rate-limiting
            config:
              minute: 100
              policy: redis
              redis_host: redis
              redis_port: 6379
          - name: acl

        consumers:
          # API client
          - username: erphub-client
        """;
        var service = CreateService();

        var result = await service.ValidateAsync(yaml, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_WhenRateLimitingRedisConfigIsMissing_ReturnsInvalidWithRedisError()
    {
        var yaml = ValidKongYaml()
            .Replace("      redis_host: redis\n", string.Empty)
            .Replace("      redis_port: 6379\n", string.Empty);
        var service = CreateService();

        var result = await service.ValidateAsync(yaml, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("redis", StringComparison.OrdinalIgnoreCase));
    }

    private static string ValidKongYaml() => """
        services:
          - name: erp-api-hub
            url: http://erp-api-hub:8080
          - name: erpnext
            url: http://erpnext:8000

        plugins:
          - name: jwt
          - name: rate-limiting
            config:
              minute: 100
              policy: redis
              redis_host: redis
              redis_port: 6379
          - name: acl

        consumers:
          - username: erphub-client
        """;
}
