using System.Diagnostics;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain;
using ERPApiHub.Domain.Entities;
using ERPApiHub.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Application.HealthChecks;

public sealed class TenantHealthCheckService
{
    private const string ExchangeName = "erphub.events";
    private const string RoutingKey = "tenant.health.alert";
    private const string HealthResourcePath = "DocType?limit_page_length=1";

    private readonly IErpHubRepository _repository;
    private readonly IErpNextClient _erpNextClient;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<TenantHealthCheckService> _logger;
    private readonly TenantHealthCheckOptions _options;

    public TenantHealthCheckService(
        IErpHubRepository repository,
        IErpNextClient erpNextClient,
        IMessageBus messageBus,
        ILogger<TenantHealthCheckService> logger)
        : this(repository, erpNextClient, messageBus, Options.Create(new TenantHealthCheckOptions()), logger)
    {
    }

    public TenantHealthCheckService(
        IErpHubRepository repository,
        IErpNextClient erpNextClient,
        IMessageBus messageBus,
        IOptions<TenantHealthCheckOptions> options,
        ILogger<TenantHealthCheckService> logger)
    {
        _repository = repository;
        _erpNextClient = erpNextClient;
        _messageBus = messageBus;
        _options = options.Value;
        _logger = logger;
    }

    public async Task CheckAllTenantsHealthAsync(CancellationToken ct)
    {
        var tenants = await _repository.ListTenantRegistriesAsync(ct);
        var activeTenants = tenants.Where(tenant => tenant.IsActive).ToArray();

        var healthyCount = 0;
        var degradedCount = 0;
        var unhealthyCount = 0;

        foreach (var tenant in activeTenants)
        {
            var previousStatus = tenant.HealthStatus;
            var result = await CheckTenantHealthAsync(tenant, ct);

            switch (result.Status)
            {
                case TenantHealthStatuses.Healthy:
                    healthyCount++;
                    break;
                case TenantHealthStatuses.Degraded:
                    degradedCount++;
                    break;
                default:
                    unhealthyCount++;
                    break;
            }

            if (ShouldPublishAlert(result.Status))
            {
                await PublishAlertAsync(result, previousStatus, ct);
            }
        }

        _logger.LogInformation(
            "Tenant health check completed. Total={TotalCount}, Healthy={HealthyCount}, Degraded={DegradedCount}, Unhealthy={UnhealthyCount}",
            activeTenants.Length,
            healthyCount,
            degradedCount,
            unhealthyCount);
    }

    public async Task<TenantHealthResult> CheckTenantHealthAsync(string tenantId, CancellationToken ct)
    {
        var tenant = await _repository.GetTenantRegistryAsync(tenantId, ct);
        if (tenant is null)
        {
            return new TenantHealthResult
            {
                TenantId = tenantId,
                Status = TenantHealthStatuses.Unhealthy,
                ErrorMessage = $"Tenant {tenantId} was not found.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var previousStatus = tenant.HealthStatus;
        var result = await CheckTenantHealthAsync(tenant, ct);

        if (ShouldPublishAlert(result.Status))
        {
            await PublishAlertAsync(result, previousStatus, ct);
        }

        return result;
    }

    private async Task<TenantHealthResult> CheckTenantHealthAsync(TenantRegistry tenant, CancellationToken ct)
    {
        var checkedAt = DateTimeOffset.UtcNow;

        if (!tenant.IsActive)
        {
            var inactiveResult = new TenantHealthResult
            {
                TenantId = tenant.TenantId,
                Status = TenantHealthStatuses.Unhealthy,
                ErrorMessage = "Tenant is inactive.",
                CheckedAt = checkedAt
            };

            await _repository.UpdateTenantHealthAsync(tenant.TenantId, inactiveResult.Status, checkedAt, ct);
            return inactiveResult;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.Timeout);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _erpNextClient.GetAsync<JsonElement>(HealthResourcePath, tenant.TenantId, timeoutCts.Token);
            stopwatch.Stop();

            var result = BuildResult(tenant.TenantId, response, stopwatch.Elapsed, checkedAt);
            await _repository.UpdateTenantHealthAsync(tenant.TenantId, result.Status, checkedAt, ct);

            _logger.LogInformation(
                "Tenant {TenantId} health check returned {Status} in {ResponseTimeMs}ms.",
                tenant.TenantId,
                result.Status,
                result.ResponseTime?.TotalMilliseconds);

            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            var result = new TenantHealthResult
            {
                TenantId = tenant.TenantId,
                Status = TenantHealthStatuses.Unhealthy,
                ResponseTime = stopwatch.Elapsed,
                ErrorMessage = $"ERPNext health check timed out after {_options.Timeout.TotalSeconds:N0}s.",
                CheckedAt = checkedAt
            };

            await _repository.UpdateTenantHealthAsync(tenant.TenantId, result.Status, checkedAt, ct);
            _logger.LogWarning("Tenant {TenantId} health check timed out.", tenant.TenantId);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var result = new TenantHealthResult
            {
                TenantId = tenant.TenantId,
                Status = TenantHealthStatuses.Unhealthy,
                ResponseTime = stopwatch.Elapsed,
                ErrorMessage = ex.Message,
                CheckedAt = checkedAt
            };

            await _repository.UpdateTenantHealthAsync(tenant.TenantId, result.Status, checkedAt, ct);
            _logger.LogWarning(ex, "Tenant {TenantId} health check failed.", tenant.TenantId);
            return result;
        }
    }

    private TenantHealthResult BuildResult(
        string tenantId,
        ErpNextResponse<JsonElement> response,
        TimeSpan responseTime,
        DateTimeOffset checkedAt)
    {
        if (!response.IsSuccessStatusCode)
        {
            return new TenantHealthResult
            {
                TenantId = tenantId,
                Status = TenantHealthStatuses.Unhealthy,
                ResponseTime = responseTime,
                ErrorMessage = response.Message ?? $"ERPNext returned HTTP {response.StatusCode}.",
                CheckedAt = checkedAt
            };
        }

        return new TenantHealthResult
        {
            TenantId = tenantId,
            Status = responseTime > _options.DegradedThreshold
                ? TenantHealthStatuses.Degraded
                : TenantHealthStatuses.Healthy,
            ResponseTime = responseTime,
            CheckedAt = checkedAt
        };
    }

    private bool ShouldPublishAlert(string status) =>
        _options.AlertOn.Any(alertStatus => string.Equals(alertStatus, status, StringComparison.OrdinalIgnoreCase));

    private async Task PublishAlertAsync(TenantHealthResult result, string? previousStatus, CancellationToken ct)
    {
        var alert = new TenantHealthAlertEvent
        {
            EventId = UlidGenerator.Generate(),
            TenantId = result.TenantId,
            Status = result.Status,
            PreviousStatus = previousStatus,
            ResponseTime = result.ResponseTime,
            ErrorMessage = result.ErrorMessage,
            AlertedAt = DateTimeOffset.UtcNow,
            CorrelationId = result.TenantId
        };

        await _messageBus.PublishAsync(ExchangeName, RoutingKey, alert, ct);

        _logger.LogWarning(
            "Published tenant health alert for tenant {TenantId}. PreviousStatus={PreviousStatus}, Status={Status}",
            result.TenantId,
            previousStatus,
            result.Status);
    }
}
