using ERPApiHub.Application.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace ERPApiHub.Tests;

public class RateLimitServiceTests
{
    private static RateLimitOptions CreateOptions() => new()
    {
        Enabled = true,
        WindowSeconds = 60,
        DefaultTier = RateLimitTier.TIER_3,
        Tiers = new Dictionary<string, TierConfig>
        {
            ["TIER_1"] = new() { RequestsPerMinute = 10000, BurstMultiplier = 2 },
            ["TIER_2"] = new() { RequestsPerMinute = 1000, BurstMultiplier = 2 },
            ["TIER_3"] = new() { RequestsPerMinute = 100, BurstMultiplier = 2 }
        },
        EndpointReduction = new Dictionary<string, double>
        {
            ["Ingestion"] = 0.50,
            ["Query"] = 1.00,
            ["WebhookManagement"] = 0.10,
            ["Admin"] = 0.05,
            ["Other"] = 1.00
        }
    };

    [Fact]
    public void GetEffectiveLimit_IngestionT3_Returns50()
    {
        var options = CreateOptions();
        var effectiveLimit = options.GetEffectiveLimit(RateLimitTier.TIER_3, EndpointType.Ingestion);
        Assert.Equal(50, effectiveLimit); // 100 * 0.50
    }

    [Fact]
    public void GetEffectiveLimit_QueryT3_Returns100()
    {
        var options = CreateOptions();
        var effectiveLimit = options.GetEffectiveLimit(RateLimitTier.TIER_3, EndpointType.Query);
        Assert.Equal(100, effectiveLimit); // 100 * 1.00
    }

    [Fact]
    public void GetEffectiveLimit_AdminT2_Returns50()
    {
        var options = CreateOptions();
        var effectiveLimit = options.GetEffectiveLimit(RateLimitTier.TIER_2, EndpointType.Admin);
        Assert.Equal(50, effectiveLimit); // 1000 * 0.05
    }

    [Fact]
    public void GetEffectiveLimit_WebhookT1_Returns1000()
    {
        var options = CreateOptions();
        var effectiveLimit = options.GetEffectiveLimit(RateLimitTier.TIER_1, EndpointType.WebhookManagement);
        Assert.Equal(1000, effectiveLimit); // 10000 * 0.10
    }

    [Fact]
    public void GetBurstCapacity_T3_Returns200()
    {
        var options = CreateOptions();
        var burst = options.GetBurstCapacity(RateLimitTier.TIER_3);
        Assert.Equal(200, burst); // 100 * 2
    }

    [Fact]
    public void ResolveTier_ValidString_ReturnsTier()
    {
        var options = CreateOptions();
        var optionsWrapper = new OptionsWrapper<RateLimitOptions>(options);

        var service = new RateLimitService(
            Mock.Of<IConnectionMultiplexer>(),
            optionsWrapper,
            Mock.Of<ILogger<RateLimitService>>());

        Assert.Equal(RateLimitTier.TIER_1, service.ResolveTier("TIER_1"));
        Assert.Equal(RateLimitTier.TIER_2, service.ResolveTier("TIER_2"));
        Assert.Equal(RateLimitTier.TIER_3, service.ResolveTier("TIER_3"));
    }

    [Fact]
    public void ResolveTier_InvalidString_ReturnsDefault()
    {
        var options = CreateOptions();
        var optionsWrapper = new OptionsWrapper<RateLimitOptions>(options);

        var service = new RateLimitService(
            Mock.Of<IConnectionMultiplexer>(),
            optionsWrapper,
            Mock.Of<ILogger<RateLimitService>>());

        Assert.Equal(RateLimitTier.TIER_3, service.ResolveTier("unknown"));
        Assert.Equal(RateLimitTier.TIER_3, service.ResolveTier(""));
    }

    [Fact]
    public void RateLimitResult_Allowed_HasCorrectValues()
    {
        var result = new RateLimitResult
        {
            IsAllowed = true,
            Limit = 100,
            Remaining = 95,
            ResetInSeconds = 45
        };

        Assert.True(result.IsAllowed);
        Assert.Equal(100, result.Limit);
        Assert.Equal(95, result.Remaining);
        Assert.Equal(45, result.ResetInSeconds);
    }

    [Fact]
    public void EndpointType_Classification()
    {
        Assert.Equal(0, (int)EndpointType.Ingestion);
        Assert.Equal(1, (int)EndpointType.Query);
        Assert.Equal(2, (int)EndpointType.WebhookManagement);
        Assert.Equal(3, (int)EndpointType.Admin);
        Assert.Equal(4, (int)EndpointType.Other);
    }
}
