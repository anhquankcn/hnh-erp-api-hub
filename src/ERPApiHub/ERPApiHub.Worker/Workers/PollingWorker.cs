using System.Globalization;
using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Polling;
using ERPApiHub.Domain;
using ERPApiHub.Domain.Events;
using ERPApiHub.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Worker.Workers;

public sealed class PollingWorker(
    IServiceScopeFactory scopeFactory,
    DoctypePollingRegistry registry,
    IOptions<PollingOptions> pollingOptions,
    IOptions<RabbitMqOptions> rabbitMqOptions,
    ILogger<PollingWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, DateTimeOffset> _nextPollAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly PollingOptions _pollingOptions = pollingOptions.Value;
    private readonly RabbitMqOptions _rabbitMqOptions = rabbitMqOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_pollingOptions.Enabled)
        {
            logger.LogInformation("ERPNext polling fallback worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(_pollingOptions.SchedulerInterval);
        logger.LogInformation(
            "ERPNext polling fallback worker started with scheduler interval {IntervalSeconds}s.",
            _pollingOptions.SchedulerInterval.TotalSeconds);

        await RunOnceAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IErpHubRepository>();
        var tenants = await repository.ListTenantRegistriesAsync(cancellationToken);
        var activeTenants = tenants
            .Where(tenant => tenant.IsActive && string.Equals(tenant.HealthStatus, "active", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var doctypes = registry.GetActiveDoctypes();

        foreach (var tenant in activeTenants)
        {
            foreach (var doctype in doctypes)
            {
                var scheduleKey = $"{tenant.TenantId}:{doctype.Doctype}";
                if (_nextPollAt.TryGetValue(scheduleKey, out var nextPollAt) && nextPollAt > now)
                {
                    continue;
                }

                try
                {
                    var rateLimited = await PollDoctypeAsync(tenant.TenantId, doctype, scope.ServiceProvider, cancellationToken);
                    _nextPollAt[scheduleKey] = DateTimeOffset.UtcNow + (rateLimited
                        ? _pollingOptions.Backoff.RateLimitDelay
                        : doctype.Interval);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _nextPollAt[scheduleKey] = DateTimeOffset.UtcNow + doctype.Interval;
                    logger.LogError(
                        ex,
                        "ERPNext polling failed for tenant {TenantId} doctype {Doctype}. Cursor was not updated.",
                        tenant.TenantId,
                        doctype.Doctype);
                }
            }
        }
    }

    private async Task<bool> PollDoctypeAsync(
        string tenantId,
        DoctypePollingRegistration doctype,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var cache = serviceProvider.GetRequiredService<ICacheService>();
        var erpNextClient = serviceProvider.GetRequiredService<IErpNextClient>();
        var messageBus = serviceProvider.GetRequiredService<IMessageBus>();

        var cursorKey = BuildCursorKey(tenantId, doctype.Doctype);
        var cursor = await cache.GetAsync<string>(cursorKey, cancellationToken);
        var effectiveCursor = string.IsNullOrWhiteSpace(cursor)
            ? DateTimeOffset.UtcNow.Subtract(_pollingOptions.InitialCursorLookback).ToString("O")
            : cursor;

        var resourcePath = BuildResourcePath(doctype, effectiveCursor);
        var response = await erpNextClient.GetAsync<JsonElement>(resourcePath, tenantId, cancellationToken);

        if (response.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            logger.LogWarning(
                "ERPNext rate limited polling for tenant {TenantId} doctype {Doctype}. Backing off for {BackoffSeconds}s.",
                tenantId,
                doctype.Doctype,
                _pollingOptions.Backoff.RateLimitDelay.TotalSeconds);
            return true;
        }

        if (!response.IsSuccessStatusCode || response.Data.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException($"ERPNext polling query failed with status {response.StatusCode}: {response.Message}");
        }

        var changes = ParseChanges(response.Data, doctype).ToArray();
        var nextCursor = TryParseCursor(effectiveCursor, out var parsedCursor)
            ? parsedCursor
            : DateTimeOffset.UtcNow.Subtract(_pollingOptions.InitialCursorLookback);

        foreach (var change in changes)
        {
            var changeEvent = new ErpNextChangeEvent(
                doctype.Doctype,
                change.Name,
                change.ModifiedTimestamp,
                tenantId);

            await PublishChangeAsync(messageBus, doctype.Doctype, changeEvent, cancellationToken);
            if (change.ModifiedTimestamp > nextCursor)
            {
                nextCursor = change.ModifiedTimestamp;
            }
        }

        if (changes.Length > 0)
        {
            await cache.SetAsync(cursorKey, nextCursor.ToString("O"), _pollingOptions.CursorTtl, cancellationToken);
        }

        logger.LogInformation(
            "ERPNext polling completed for tenant {TenantId} doctype {Doctype}. Published {ChangeCount} changes.",
            tenantId,
            doctype.Doctype,
            changes.Length);

        return false;
    }

    private async Task PublishChangeAsync(
        IMessageBus messageBus,
        string doctype,
        ErpNextChangeEvent changeEvent,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToElement(changeEvent, JsonOptions);
        var routingKey = $"erphub.ingestion.{doctype}.polling";
        var envelope = new ErpEventEnvelope(
            CreateEventId(),
            routingKey,
            "ERPApiHub",
            changeEvent.TenantId,
            DateTimeOffset.UtcNow,
            "1",
            payload);

        await messageBus.PublishAsync(_rabbitMqOptions.ExchangeName, routingKey, envelope, cancellationToken);
    }

    private static IEnumerable<PollingChange> ParseChanges(JsonElement response, DoctypePollingRegistration doctype)
    {
        if (!TryGetDataArray(response, out var dataArray))
        {
            yield break;
        }

        foreach (var item in dataArray.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameElement)
                || nameElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                continue;
            }

            if (!item.TryGetProperty(doctype.LastCursorField, out var modifiedElement)
                || modifiedElement.ValueKind != JsonValueKind.String
                || !TryParseCursor(modifiedElement.GetString(), out var modifiedTimestamp))
            {
                continue;
            }

            yield return new PollingChange(nameElement.GetString()!, modifiedTimestamp);
        }
    }

    private static bool TryGetDataArray(JsonElement response, out JsonElement dataArray)
    {
        if (response.ValueKind == JsonValueKind.Object
            && response.TryGetProperty("data", out dataArray)
            && dataArray.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (response.ValueKind == JsonValueKind.Array)
        {
            dataArray = response;
            return true;
        }

        dataArray = default;
        return false;
    }

    private static string BuildResourcePath(DoctypePollingRegistration doctype, string cursor)
    {
        var filters = JsonSerializer.Serialize(new[] { new[] { doctype.LastCursorField, ">", cursor } });
        var fields = JsonSerializer.Serialize(new[] { "name", doctype.LastCursorField });

        return $"{Uri.EscapeDataString(doctype.Doctype)}"
            + $"?filters={Uri.EscapeDataString(filters)}"
            + $"&fields={Uri.EscapeDataString(fields)}"
            + $"&order_by={Uri.EscapeDataString($"{doctype.LastCursorField} asc")}"
            + "&limit_page_length=100";
    }

    private static string BuildCursorKey(string tenantId, string doctype) =>
        $"erphub:polling:{tenantId}:{doctype}:cursor";

    private static bool TryParseCursor(string? value, out DateTimeOffset cursor)
    {
        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out cursor))
        {
            return true;
        }

        if (DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var dateTime))
        {
            cursor = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
            return true;
        }

        return false;
    }

    private static string CreateEventId()
    {
        var id = UlidGenerator.Generate();
        return id.Length == 26 ? id : id[..26];
    }

    private sealed record PollingChange(string Name, DateTimeOffset ModifiedTimestamp);
}
