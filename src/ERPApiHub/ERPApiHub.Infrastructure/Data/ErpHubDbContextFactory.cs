using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERPApiHub.Infrastructure.Data;

public sealed class ErpHubDbContextFactory : IDesignTimeDbContextFactory<ErpHubDbContext>
{
    public ErpHubDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5452;Database=erphub_api_db;Username=erphub;Password=erphub";

        var options = new DbContextOptionsBuilder<ErpHubDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ErpHubDbContext(options);
    }
}
