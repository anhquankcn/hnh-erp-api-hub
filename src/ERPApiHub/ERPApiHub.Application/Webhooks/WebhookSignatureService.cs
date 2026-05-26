using System.Security.Cryptography;
using System.Text;

namespace ERPApiHub.Application.Webhooks;

public sealed class WebhookSignatureService
{
    public string ComputeSignature(string payload, byte[] secret)
    {
        using var hmac = new HMACSHA256(secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public bool ValidateSignature(string payload, byte[] secret, string signature)
    {
        var expected = ComputeSignature(payload, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }
}