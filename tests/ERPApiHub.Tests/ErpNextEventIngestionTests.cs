using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Webhooks;
using Moq;
using Xunit;

namespace ERPApiHub.Tests;

public class ErpNextEventIngestionTests
{
    [Fact]
    public void ValidateSignature_EmptySecret_ReturnsFalse()
    {
        var service = CreateService(sharedSecret: "");
        var result = service.ValidateSignature("{}", "sha256=abc");
        Assert.False(result);
    }

    [Fact]
    public void ValidateSignature_EmptySecretWithExplicitSkip_ReturnsTrue()
    {
        var service = CreateService(sharedSecret: "", skipSignatureValidation: true);
        var result = service.ValidateSignature("{}", "sha256=abc");
        Assert.True(result);
    }

    [Fact]
    public void ValidateSignature_InvalidHeaderFormat_ReturnsFalse()
    {
        var service = CreateService(sharedSecret: "test-secret");
        var result = service.ValidateSignature("{}", "invalid-format");
        Assert.False(result);
    }

    private static ErpNextEventIngestionService CreateService(
        string sharedSecret = "test-secret",
        bool skipSignatureValidation = false)
    {
        var repository = Mock.Of<IErpHubRepository>();
        var messageBus = Mock.Of<IMessageBus>();
        var eventOptions = new ErpNextEventOptions
        {
            SharedSecret = sharedSecret,
            MaxClockSkewSeconds = 300,
            Enabled = true,
            SkipSignatureValidation = skipSignatureValidation
        };
        var logger = Mock.Of<Microsoft.Extensions.Logging.ILogger<ErpNextEventIngestionService>>();

        return new ErpNextEventIngestionService(
            repository,
            messageBus,
            Microsoft.Extensions.Options.Options.Create(eventOptions),
            logger);
    }
}
