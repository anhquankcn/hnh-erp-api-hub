using System.Text;
using System.Text.Json;
using ERPApiHub.Domain.Entities;
using ERPApiHub.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetUlid;

namespace ERPApiHub.Application.Webhooks;

public sealed class WebhookDeliveryService
{
    private readonly ErpHubDbContext _dbContext;
    private readonly WebhookSignatureService _signatureService;
    private readonly IDataProtector _dataProtector;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookDeliveryService> _logger;

    public WebhookDeliveryService(
        ErpHubDbContext dbContext,
        WebhookSignatureService signatureService,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDeliveryService> logger)
    {
        _dbContext = dbContext;
        _signatureService = signatureService;
        _dataProtector = dataProtectionProvider.CreateProtector("WebhookSecret");
        _httpClient = httpClientFactory.CreateClient("WebhookDelivery");
        _logger = logger;
    }

    public async Task DeliverAsync(WebhookSubscription subscription, string payload, CancellationToken ct)
    {
        byte[]? secret = null;
        if (subscription.SecretEncrypted is not null)
        {
            try
            {
                secret = _dataProtector.Unprotect(subscription.SecretEncrypted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt secret for subscription {SubId}", subscription.SubscriptionId);
            }
        }

        var signature = secret is not null ? _signatureService.ComputeSignature(payload, secret) : "";
        var deliveryId = Ulid.NewUlid().ToString();

        var delivery = new WebhookDelivery
        {
            DeliveryId = deliveryId,
            SubscriptionId = subscription.SubscriptionId,
            Status = "pending",
            AttemptedAt = DateTimeOffset.UtcNow
        };

        _dbContext.WebhookDeliveries.Add(delivery);

        var maxRetries = 3;
        var retryDelay = TimeSpan.FromSeconds(2);

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, subscription.WebhookUrl);
                request.Headers.Add("X-ERP-Hub-Signature", signature);
                request.Headers.Add("X-ERP-Hub-Delivery-Id", deliveryId);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request, ct);
                delivery.HttpStatus = (int)response.StatusCode;
                delivery.ResponseBody = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    delivery.Status = "delivered";
                    delivery.NextRetryAt = null;
                    _logger.LogInformation("Webhook delivered to {Url} (HTTP {Status})", subscription.WebhookUrl, (int)response.StatusCode);
                    break;
                }

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    // Client error — don't retry
                    delivery.Status = "failed";
                    delivery.NextRetryAt = null;
                    _logger.LogWarning("Webhook client error {Status} for {Url}", (int)response.StatusCode, subscription.WebhookUrl);
                    break;
                }

                // Server error — retry
                delivery.Status = "retrying";
                delivery.NextRetryAt = DateTimeOffset.UtcNow + retryDelay * (attempt + 1);
                _logger.LogWarning("Webhook server error {Status}, retry {Attempt}/{Max}", (int)response.StatusCode, attempt + 1, maxRetries);
            }
            catch (Exception ex)
            {
                delivery.Status = "retrying";
                delivery.NextRetryAt = DateTimeOffset.UtcNow + retryDelay * (attempt + 1);
                _logger.LogWarning(ex, "Webhook delivery exception, retry {Attempt}/{Max}", attempt + 1, maxRetries);
            }

            if (attempt < maxRetries - 1)
            {
                await Task.Delay(retryDelay * (attempt + 1), ct);
            }
        }

        if (delivery.Status == "retrying")
        {
            delivery.Status = "failed";
            delivery.NextRetryAt = null;
            _logger.LogError("Webhook delivery failed after {Max} retries for subscription {SubId}", maxRetries, subscription.SubscriptionId);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<List<WebhookDelivery>> ListDeliveriesAsync(string subscriptionId, CancellationToken ct)
    {
        return await _dbContext.WebhookDeliveries
            .Where(d => d.SubscriptionId == subscriptionId)
            .OrderByDescending(d => d.AttemptedAt)
            .Take(100)
            .ToListAsync(ct);
    }
}