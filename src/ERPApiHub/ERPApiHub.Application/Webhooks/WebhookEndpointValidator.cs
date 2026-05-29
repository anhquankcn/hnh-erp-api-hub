using System.Net;
using System.Net.Sockets;

namespace ERPApiHub.Application.Webhooks;

public sealed class WebhookEndpointValidator
{
    public async Task<WebhookEndpointValidationResult> ValidateAsync(
        string webhookUrl,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
        {
            return WebhookEndpointValidationResult.Invalid("Webhook URL must be absolute.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return WebhookEndpointValidationResult.Invalid("Webhook URL must use HTTPS.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            return WebhookEndpointValidationResult.Invalid("Webhook URL host is required.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
        }
        catch (SocketException)
        {
            return WebhookEndpointValidationResult.Invalid("Webhook URL host could not be resolved.");
        }

        if (addresses.Length == 0)
        {
            return WebhookEndpointValidationResult.Invalid("Webhook URL host could not be resolved.");
        }

        if (addresses.Any(IsPrivateOrReserved))
        {
            return WebhookEndpointValidationResult.Invalid("Webhook URL resolves to a private or reserved IP address.");
        }

        return WebhookEndpointValidationResult.Valid();
    }

    private static bool IsPrivateOrReserved(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6Multicast
                || address.IsIPv6SiteLocal
                || IsIPv6UniqueLocal(address);
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 0
            || bytes[0] == 10
            || bytes[0] == 127
            || bytes[0] == 169 && bytes[1] == 254
            || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31
            || bytes[0] == 192 && bytes[1] == 168;
    }

    private static bool IsIPv6UniqueLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return (bytes[0] & 0xfe) == 0xfc;
    }
}

public sealed record WebhookEndpointValidationResult(bool IsValid, string? Error)
{
    public static WebhookEndpointValidationResult Valid() => new(true, null);

    public static WebhookEndpointValidationResult Invalid(string error) => new(false, error);
}
