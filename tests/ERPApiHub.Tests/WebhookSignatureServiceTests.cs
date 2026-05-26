using System.Text;
using ERPApiHub.Application.Webhooks;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class WebhookSignatureServiceTests
{
    private readonly WebhookSignatureService _service = new();

    [Fact]
    public void ComputeSignature_ReturnsSha256Prefix()
    {
        var secret = Encoding.UTF8.GetBytes("my-secret-key");
        var result = _service.ComputeSignature("{\"event\":\"test\"}", secret);

        Assert.StartsWith("sha256=", result);
        Assert.True(result.Length > 10);
    }

    [Fact]
    public void ComputeSignature_SameInputSameOutput()
    {
        var secret = Encoding.UTF8.GetBytes("secret");
        var payload = "{\"test\":true}";

        var sig1 = _service.ComputeSignature(payload, secret);
        var sig2 = _service.ComputeSignature(payload, secret);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void ComputeSignature_DifferentPayloadDifferentSignature()
    {
        var secret = Encoding.UTF8.GetBytes("secret");

        var sig1 = _service.ComputeSignature("{\"a\":1}", secret);
        var sig2 = _service.ComputeSignature("{\"a\":2}", secret);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void ValidateSignature_WhenValid_ReturnsTrue()
    {
        var secret = Encoding.UTF8.GetBytes("my-secret");
        var payload = "{\"event\":\"order.created\"}";
        var signature = _service.ComputeSignature(payload, secret);

        Assert.True(_service.ValidateSignature(payload, secret, signature));
    }

    [Fact]
    public void ValidateSignature_WhenWrongSecret_ReturnsFalse()
    {
        var secret = Encoding.UTF8.GetBytes("correct-secret");
        var wrongSecret = Encoding.UTF8.GetBytes("wrong-secret");
        var payload = "{\"event\":\"test\"}";
        var signature = _service.ComputeSignature(payload, secret);

        Assert.False(_service.ValidateSignature(payload, wrongSecret, signature));
    }

    [Fact]
    public void ValidateSignature_WhenTamperedPayload_ReturnsFalse()
    {
        var secret = Encoding.UTF8.GetBytes("secret");
        var signature = _service.ComputeSignature("{\"amount\":100}", secret);

        Assert.False(_service.ValidateSignature("{\"amount\":999}", secret, signature));
    }
}