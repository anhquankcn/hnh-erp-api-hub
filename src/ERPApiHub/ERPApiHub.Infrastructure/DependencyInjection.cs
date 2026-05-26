using ERPApiHub.Application.Abstractions;
using ERPApiHub.Infrastructure.Caching;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        var redisConnectionString = BuildRedisConnectionString(configuration);
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddScoped<IRedisCacheService, RedisCacheService>();

        services
            .AddHealthChecks()
            .AddRedis(redisConnectionString, name: "redis", tags: ["ready", "startup"]);

        services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
        services.AddScoped<IErpNextClient, NoOpErpNextClient>();

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