using System.Text.Json;
using ERPApiHub.Application.Webhooks;
using Xunit;

namespace ERPApiHub.Tests;

public class ErpNextEventIngestionTests
{
    [Fact]
    public void ValidateEnvelope_ValidJson_ReturnsTrue()
    {
        var service = CreateService();

        var envelope = JsonDocument.Parse("""
            {
                "eventId": "01HGS4KXDN3E5M2AY000000000",
                "eventType": "Sales Invoice",
                "source": "erpnext",
                "correlationId": "01HGS4KXDN3E5M2AY000000001",
                "timestamp": "2024-01-15T10:30:00Z",
                "payload": { "doctype": "Sales Invoice", "name": "SINV-001" }
            }
            """).RootElement;

        var (isValid, error) = service.ValidateEnvelope(envelope);
        Assert.True(isValid);
        Assert.Null(error);
    }

    [Fact]
    public void ValidateEnvelope_MissingEventId_ReturnsFalse()
    {
        var service = CreateService();

        var envelope = JsonDocument.Parse("""
            {
                "eventType": "Sales Invoice",
                "source": "erpnext",
                "correlationId": "correlation-123",
                "timestamp": "2024-01-15T10:30:00Z",
                "payload": {}
            }
            """).RootElement;

        var (isValid, error) = service.ValidateEnvelope(envelope);
        Assert.False(isValid);
        Assert.Contains("eventId", error);
    }

    [Fact]
    public void ValidateEnvelope_ShortEventId_ReturnsFalse()
    {
        var service = CreateService();

        var envelope = JsonDocument.Parse("""
            {
                "eventId": "short-id",
                "eventType": "Sales Invoice",
                "source": "erpnext",
                "correlationId": "correlation-123",
                "timestamp": "2024-01-15T10:30:00Z",
                "payload": {}
            }
            """).RootElement;

        var (isValid, error) = service.ValidateEnvelope(envelope);
        Assert.False(isValid);
        Assert.Contains("26-character", error);
    }

    [Fact]
    public void ValidateEnvelope_MissingEventType_ReturnsFalse()
    {
        var service = CreateService();

        var envelope = JsonDocument.Parse("""
            {
                "eventId": "01HGS4KXDN3E5M2AY000000000",
                "source": "erpnext",
                "correlationId": "correlation-123",
                "timestamp": "2024-01-15T10:30:00Z",
                "payload": {}
            }
            """).RootElement;

        var (isValid, error) = service.ValidateEnvelope(envelope);
        Assert.False(isValid);
        Assert.Contains("eventType", error);
    }

    [Fact]
    public void ValidateEnvelope_MissingSource_ReturnsFalse()
    {
        var service = CreateService();

        var envelope = JsonDocument.Parse("""
            {
                "eventId": "01HGS4KXDN3E5M2AY000000000",
                "eventType": "Sales Invoice",
                "correlationId": "correlation-123",
                "timestamp": "2024-01-15T10:30:00Z",
                "payload": {}
            }
            """).RootElement;

        var (isValid, error) = service.ValidateEnvelope(envelope);
        Assert.False(isValid);
        Assert.Contains("source", error);
    }

    [Fact]
    public void ValidateEnvelope_MissingCorrelationId_ReturnsFalse()
    {
        var service = CreateService();

        var envelope = JsonDocument.Parse("""
            {
                "eventId": "01HGS4KXDN3E5M2AY000000000",
                "eventType": "Sales Invoice",
                "source": "erpnext",
                "timestamp": "2024-01-15T10:30:00Z",
                "payload": {}
            }
            """).RootElement;

        var (isValid, error) = service.ValidateEnvelope(envelope);
        Assert.False(isValid);
        Assert.Contains("correlationId", error);
    }

    [Fact]
    public void ValidateEnvelope_MissingTimestamp_ReturnsFalse()
    {
        var service = CreateService();

        var envelope = JsonDocument.Parse("""
            {
                "eventId": "01HGS4KXDN3E5M2AY000000000",
                "eventType": "Sales Invoice",
                "source": "erpnext",
                "correlationId": "correlation-123",
                "payload": {}
            }
            """).RootElement;

        var (isValid, error) = service.ValidateEnvelope(envelope);
        Assert.False(isValid);
        Assert.Contains("timestamp", error);
    }

    [Fact]
    public void ValidateEnvelope_MissingPayload_ReturnsFalse()
    {
        var service = CreateService();

        var envelope = JsonDocument.Parse("""
            {
                "eventId": "01HGS4KXDN3E5M2AY000000000",
                "eventType": "Sales Invoice",
                "source": "erpnext",
                "correlationId": "correlation-123",
                "timestamp": "2024-01-15T10:30:00Z"
            }
            """).RootElement;

        var (isValid, error) = service.ValidateEnvelope(envelope);
        Assert.False(isValid);
        Assert.Contains("payload", error);
    }

    [Fact]
    public void ValidateSignature_EmptySecret_SkipsValidation()
    {
        // When shared secret is empty, validation is skipped (returns true)
        var service = CreateService(sharedSecret: "");
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

    private static ErpNextEventIngestionService CreateService(string sharedSecret = "test-secret")
    {
        var dbContext = Mock.Of<ERPApiHub.Infrastructure.Data.ErpHubDbContext>();
        var connectionFactory = Mock.Of<ERPApiHub.Infrastructure.Messaging.IRabbitMqConnectionFactory>();
        var rabbitMqOptions = new ERPApiHub.Infrastructure.Messaging.RabbitMqOptions
        {
            ExchangeName = "1stopshop_event_bus"
        };
        var eventOptions = new ErpNextEventOptions
        {
            SharedSecret = sharedSecret,
            MaxClockSkewSeconds = 300,
            Enabled = true
        };
        var logger = Mock.Of<Microsoft.Extensions.Logging.ILogger<ErpNextEventIngestionService>>();

        return new ErpNextEventIngestionService(
            dbContext,
            connectionFactory,
            Microsoft.Extensions.Options.Options.Create(rabbitMqOptions),
            Microsoft.Extensions.Options.Options.Create(eventOptions),
            logger);
    }
}