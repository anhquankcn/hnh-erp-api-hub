using ERPApiHub.Application.Abstractions;
using ERPApiHub.Infrastructure.Data;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
        services.AddScoped<IErpNextClient, NoOpErpNextClient>();

        return services;
    }
}
