using ERPApiHub.API.DTOs.Webhooks;
using ERPApiHub.Application.Webhooks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERPApiHub.API.Controllers;

[ApiController]
[Route("api/v1/webhooks/deliveries")]
[Authorize(Policy = "api-hub:admin")]
public sealed class WebhookDeliveryController(WebhookDeliveryService service) : ControllerBase
{
    [HttpGet("{subscriptionId}")]
    [ProducesResponseType(typeof(IReadOnlyList<WebhookDeliveryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WebhookDeliveryResponse>>> ListBySubscription(
        [FromRoute] string subscriptionId,
        CancellationToken cancellationToken)
    {
        var deliveries = await service.ListDeliveriesAsync(subscriptionId, cancellationToken);
        return Ok(deliveries.Select(WebhookDeliveryResponse.FromEntity).ToList());
    }
}
