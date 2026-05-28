using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Validation;
using ERPApiHub.Domain.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace ERPApiHub.Tests;

public sealed class LinkFieldValidatorTests
{
    private readonly Mock<IErpNextClient> _erpNextClientMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<ILogger<LinkFieldValidator>> _loggerMock = new();
    private readonly LinkFieldValidationOptions _options = new()
    {
        Enabled = true,
        TimeoutSeconds = 5,
        FailOnInvalid = false,
        ExcludedDoctypes = ["User", "Role Profile"]
    };

    private LinkFieldValidator CreateValidator()
    {
        return new LinkFieldValidator(
            _erpNextClientMock.Object,
            _cacheMock.Object,
            Options.Create(_options),
            _loggerMock.Object);
    }

    [Fact]
    public async Task ValidateAsync_WhenAllLinksValid_ReturnsIsValidTrue()
    {
        // Arrange
        var validator = CreateValidator();
        var schemaResponse = new ErpResponse<JsonElement>
        {
            IsSuccessStatusCode = true,
            Data = JsonSerializer.Deserialize<JsonElement>("""{"data": [{"fieldname": "customer", "options": "Customer"}]}""")
        };

        var recordResponse = new ErpResponse<JsonElement>
        {
            IsSuccessStatusCode = true,
            Data = JsonSerializer.Deserialize<JsonElement>("""{"name": "CUST-001"}""")
        };

        _erpNextClientMock
            .Setup(c => c.GetAsync<JsonElement>(
                It.Is<string>(s => s.Contains("DocField")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemaResponse);

        _erpNextClientMock
            .Setup(c => c.GetAsync<JsonElement>(
                It.Is<string>(s => s.Contains("Customer")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(recordResponse);

        var fields = new Dictionary<string, object?> { ["customer"] = "CUST-001" };

        // Act
        var result = await validator.ValidateAsync("tenant-1", "Sales Invoice", fields, default);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.InvalidFields);
        Assert.Single(result.ValidFields);
        Assert.Equal("customer", result.ValidFields[0].FieldName);
    }

    [Fact]
    public async Task ValidateAsync_WhenLinkMissing_ReturnsInvalidField()
    {
        // Arrange
        var validator = CreateValidator();
        var schemaResponse = new ErpResponse<JsonElement>
        {
            IsSuccessStatusCode = true,
            Data = JsonSerializer.Deserialize<JsonElement>(
                """{"data": [{"fieldname": "customer", "options": "Customer"}]}""")
        };

        var recordResponse = new ErpResponse<JsonElement>
        {
            IsSuccessStatusCode = false,
            Data = default
        };

        _erpNextClientMock
            .Setup(c => c.GetAsync<JsonElement>(
                It.Is<string>(s => s.Contains("DocField")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemaResponse);

        _erpNextClientMock
            .Setup(c => c.GetAsync<JsonElement>(
                It.Is<string>(s => s.Contains("Customer")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(recordResponse);

        var fields = new Dictionary<string, object?> { ["customer"] = "NONEXISTENT" };

        // Act
        var result = await validator.ValidateAsync("tenant-1", "Sales Invoice", fields, default);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.InvalidFields);
        Assert.Equal("customer", result.InvalidFields[0].FieldName);
        Assert.Equal("NONEXISTENT", result.InvalidFields[0].Value);
    }

    [Fact]
    public async Task ValidateAsync_WhenExcludedDoctype_ReturnsIsValidTrue()
    {
        // Arrange
        var validator = CreateValidator();
        var fields = new Dictionary<string, object?> { ["role"] = "Admin" };

        // Act
        var result = await validator.ValidateAsync("tenant-1", "User", fields, default);

        // Assert
        Assert.True(result.IsValid);
        _erpNextClientMock.Verify(
            c => c.GetAsync<JsonElement>(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateAsync_CachesSchema_CallsErpNextOnceForSchema()
    {
        // Arrange
        var validator = CreateValidator();
        var schemaResponse = new ErpResponse<JsonElement>
        {
            IsSuccessStatusCode = true,
            Data = JsonSerializer.Deserialize<JsonElement>(
                """{"data": [{"fieldname": "customer", "options": "Customer"}]}""")
        };

        var recordResponse = new ErpResponse<JsonElement>
        {
            IsSuccessStatusCode = true,
            Data = JsonSerializer.Deserialize<JsonElement>(
                """{"name": "CUST-001"}""")
        };

        _erpNextClientMock
            .Setup(c => c.GetAsync<JsonElement>(
                It.Is<string>(s => s.Contains("DocField")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(schemaResponse);

        _erpNextClientMock
            .Setup(c => c.GetAsync<JsonElement>(
                It.Is<string>(s => s.Contains("Customer")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(recordResponse);

        var fields = new Dictionary<string, object?> { ["customer"] = "CUST-001" };

        // Act - call twice
        await validator.ValidateAsync("tenant-1", "Sales Invoice", fields, default);
        await validator.ValidateAsync("tenant-1", "Sales Invoice", fields, default);

        // Assert - schema API should be called only once
        _erpNextClientMock.Verify(
            c => c.GetAsync<JsonElement>(
                It.Is<string>(s => s.Contains("DocField")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
