using ERPApiHub.Application.HealthChecks;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Worker.Workers;

public sealed class TenantHealthCheckWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<TenantHealthCheckOptions> options,
    ILogger<TenantHealthCheckWorker> logger) : BackgroundService
{
    private readonly TenantHealthCheckOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Tenant health check worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(_options.Interval);
        logger.LogInformation(
            "Tenant health check worker started with interval {IntervalMinutes} minutes.",
            _options.Interval.TotalMinutes);

        await RunOnceAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<TenantHealthCheckService>();
            await service.CheckAllTenantsHealthAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tenant health check failed.");
        }
    }
}
