using ERPApiHub.API.DTOs.Webhooks;
using ERPApiHub.Application.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERPApiHub.API.Controllers;

[ApiController]
[Route("api/v1/webhooks/subscriptions")]
[Authorize(Policy = "api-hub:admin")]
public sealed class WebhookSubscriptionController(WebhookSubscriptionService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(WebhookSubscriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WebhookSubscriptionResponse>> Create(
        [FromBody] CreateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var subscription = await service.CreateAsync(
            request.SystemId,
            request.EventTypes,
            request.WebhookUrl,
            request.Secret,
            tenantId,
            cancellationToken);

        var response = WebhookSubscriptionResponse.FromEntity(subscription);
        return Created($"/api/v1/webhooks/subscriptions/{response.SubscriptionId}", response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookSubscriptionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WebhookSubscriptionResponse>>> List(CancellationToken cancellationToken)
    {
        var subscriptions = await service.ListByTenantAsync(GetTenantId(), cancellationToken);
        return Ok(subscriptions.Select(WebhookSubscriptionResponse.FromEntity).ToList());
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(WebhookSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WebhookSubscriptionResponse>> Get(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        var subscription = await service.GetByIdAsync(id, cancellationToken);
        return subscription is null
            ? NotFound(new { error = "Subscription not found" })
            : Ok(WebhookSubscriptionResponse.FromEntity(subscription));
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(WebhookSubscriptionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WebhookSubscriptionResponse>> Update(
        [FromRoute] string id,
        [FromBody] UpdateWebhookSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var subscription = await service.UpdateAsync(
                id,
                request.EventTypes,
                request.WebhookUrl,
                request.Secret,
                cancellationToken);

            return Ok(WebhookSubscriptionResponse.FromEntity(subscription));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Subscription not found" });
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "Subscription not found" });
        }
    }

    private string GetTenantId() =>
        User.FindFirst("BranchId")?.Value
        ?? User.FindFirst("tenant_id")?.Value
        ?? User.FindFirst("tenantId")?.Value
        ?? "unknown";
}
