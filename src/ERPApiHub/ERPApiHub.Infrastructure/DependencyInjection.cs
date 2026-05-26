using ERPApiHub.Application.Abstractions;
using ERPApiHub.Infrastructure.Caching;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.ErpNext;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using StackExchange.Redis;

namespace ERPApiHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddErpHubInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? configuration["ConnectionStrings:Default"]
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured. Set it in appsettings, user secrets, or environment variables.");

        services.AddDbContext<ErpHubDbContext>(options => options.UseNpgsql(connectionString));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<ErpNextOptions>(configuration.GetSection(ErpNextOptions.SectionName));

        var redisConnectionString = BuildRedisConnectionString(configuration);
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddScoped<IRedisCacheService, RedisCacheService>();

        // ERPNext HTTP Client with Polly retry
        var erpNextOptions = configuration.GetSection(ErpNextOptions.SectionName).Get<ErpNextOptions>() ?? new();
        services.AddHttpClient<IErpNextClient, ErpNextHttpClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(erpNextOptions.TimeoutSeconds);
        })
        .AddResilienceHandler("erpnext-retry", builder =>
        {
            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = erpNextOptions.RetryCount,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .HandleResult(response => (int)response.StatusCode >= 500)
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutException>(),
                Delay = TimeSpan.FromSeconds(2),
            });
        });

        services.AddDataProtection();
        services.AddMemoryCache();

        services
            .AddHealthChecks()
            .AddRedis(redisConnectionString, name: "redis", tags: ["ready", "startup"]);

        services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();

        return services;
    }

    private static string BuildRedisConnectionString(IConfiguration configuration)
    {
        var redisConnectionString = configuration["Redis:ConnectionString"] ?? "localhost:6379";
        var redisPassword = configuration["Redis:Password"];

        if (!string.IsNullOrWhiteSpace(redisPassword)
            && !redisConnectionString.Contains("password=", StringComparison.OrdinalIgnoreCase))
        {
            redisConnectionString = $"{redisConnectionString},password={redisPassword}";
        }

        if (!redisConnectionString.Contains("abortConnect=", StringComparison.OrdinalIgnoreCase))
        {
            redisConnectionString = $"{redisConnectionString},abortConnect=false";
        }

        return redisConnectionString;
    }
}