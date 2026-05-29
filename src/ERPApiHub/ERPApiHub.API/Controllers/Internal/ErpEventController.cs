using System.Net;
using ERPApiHub.Application.Webhooks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ERPApiHub.API.Controllers.Internal;

[ApiController]
[Route("internal/v1/events")]
public sealed class ErpEventController(
    ErpNextEventIngestionService ingestionService,
    IOptions<ErpNextEventOptions> options,
    ILogger<ErpEventController> logger) : ControllerBase
{
    [HttpPost("ingest")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Ingest(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            return NotFound();
        }

        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (!IsAllowedSource(remoteIp, options.Value.AllowedIpRanges))
        {
            logger.LogWarning("Rejected ERP event ingest from disallowed IP {RemoteIp}", remoteIp);
            return Forbid();
        }

        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        var signatureHeader = Request.Headers["X-ERP-Hub-Signature-256"].FirstOrDefault() ?? string.Empty;
        var timestampHeader = Request.Headers["X-ERP-Hub-Timestamp"].FirstOrDefault() ?? string.Empty;

        if (!ingestionService.ValidateSignature(rawBody, signatureHeader, timestampHeader))
        {
            return Unauthorized(new { error = "Invalid signature" });
        }

        System.Text.Json.JsonElement root;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(rawBody);
            root = document.RootElement.Clone();
        }
        catch (System.Text.Json.JsonException)
        {
            return BadRequest(new { error = "Invalid JSON payload" });
        }

        var eventType = root.TryGetProperty("eventType", out var eventTypeElement)
            && eventTypeElement.ValueKind == System.Text.Json.JsonValueKind.String
            && !string.IsNullOrWhiteSpace(eventTypeElement.GetString())
                ? eventTypeElement.GetString()!
                : string.Empty;

        var result = await ingestionService.IngestEventAsync(
            eventType,
            rawBody,
            signatureHeader,
            timestampHeader,
            cancellationToken);

        if (!result.Success)
        {
            return BadRequest(new { error = result.Error });
        }

        return Accepted(new { status = "accepted", eventId = result.EventId });
    }

    private static bool IsAllowedSource(IPAddress? remoteIp, IReadOnlyList<string> allowedRanges)
    {
        if (remoteIp is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(remoteIp)
            && (allowedRanges.Count == 0
                || allowedRanges.Any(range => range is "127.0.0.1/32" or "::1/128")))
        {
            return true;
        }

        return allowedRanges.Any(range => MatchesRange(remoteIp, range));
    }

    private static bool MatchesRange(IPAddress address, string range)
    {
        if (IPAddress.TryParse(range, out var exact))
        {
            return Normalize(address).Equals(Normalize(exact));
        }

        var parts = range.Split('/', 2);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var network)
            || !int.TryParse(parts[1], out var prefixLength))
        {
            return false;
        }

        var normalizedAddress = Normalize(address);
        var normalizedNetwork = Normalize(network);
        var addressBytes = normalizedAddress.GetAddressBytes();
        var networkBytes = normalizedNetwork.GetAddressBytes();

        if (addressBytes.Length != networkBytes.Length || prefixLength < 0 || prefixLength > addressBytes.Length * 8)
        {
            return false;
        }

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (addressBytes[i] != networkBytes[i])
            {
                return false;
            }
        }

        if (remainingBits == 0)
        {
            return true;
        }

        var mask = (byte)(0xff << (8 - remainingBits));
        return (addressBytes[fullBytes] & mask) == (networkBytes[fullBytes] & mask);
    }

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
