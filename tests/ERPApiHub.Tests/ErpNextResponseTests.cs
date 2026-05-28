using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class ErpNextResponseTests
{
    [Fact]
    public void ErpNextResponse_WhenSuccess_HasDataAndStatusCode200()
    {
        var data = JsonDocument.Parse("{\"name\":\"CUST-001\"}").RootElement;
        var response = new ErpNextResponse<JsonElement>(data, 200, null);

        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.Data);
        Assert.Null(response.Message);
    }

    [Fact]
    public void ErpNextResponse_WhenNotFound_HasMessage()
    {
        var response = new ErpNextResponse<JsonElement>(default, 404, "Not found");

        Assert.Equal(404, response.StatusCode);
        Assert.Null(response.Data);
        Assert.Equal("Not found", response.Message);
    }

    [Fact]
    public void ErpEventEnvelope_HasAllRequiredFields()
    {
        var payload = JsonDocument.Parse("{\"doctype\":\"Customer\"}").RootElement;
        var envelope = new ErpEventEnvelope(
            "evt-001",
            "erphub.ingestion.Customer.created",
            "ERPApiHub",
            "corr-001",
            DateTimeOffset.UtcNow,
            "1",
            payload);

        Assert.Equal("evt-001", envelope.EventId);
        Assert.Equal("erphub.ingestion.Customer.created", envelope.EventType);
        Assert.Equal("ERPApiHub", envelope.Source);
        Assert.Equal("1", envelope.Version);
    }
}