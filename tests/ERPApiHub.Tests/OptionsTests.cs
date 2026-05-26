using ERPApiHub.Infrastructure.Caching;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class RedisOptionsTests
{
    [Fact]
    public void RedisOptions_HasDefaultValues()
    {
        var options = new RedisOptions();
        Assert.Equal("localhost:6379", options.ConnectionString);
        Assert.Null(options.Password);
        Assert.Equal("erphub:", options.InstanceName);
        Assert.Equal(TimeSpan.FromMinutes(5), options.DefaultTtl);
    }

    [Fact]
    public void RabbitMqOptions_HasDefaultExchange()
    {
        var options = new RabbitMqOptions();
        Assert.Equal("1stopshop_event_bus", options.ExchangeName);
    }
}