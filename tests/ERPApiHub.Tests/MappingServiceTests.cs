using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Mapping;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ERPApiHub.Tests;

public sealed class MappingServiceTests
{
    private readonly Mock<IErpHubRepository> _repository = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<ILogger<MappingService>> _logger = new();

    private MappingService CreateService() => new(
        _repository.Object,
        _cache.Object,
        _logger.Object);

    [Fact]
    public async Task ApplyMappingAsync_AppliesFieldNameMappingCorrectly()
    {
        _cache.Setup(x => x.GetAsync<IReadOnlyList<FieldMapping>>("mapping:system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<FieldMapping>?)null);
        _repository.Setup(x => x.GetFieldMappingsAsync("system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CreateMapping("system-1", "Customer", "customer_name", "customerName", "string")
            ]);
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<FieldMapping>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = CreateService();

        var result = await service.ApplyMappingAsync(
            "system-1",
            "Customer",
            Json("""{"customer_name":"Acme"}"""),
            CancellationToken.None);

        Assert.Equal("Acme", result.GetProperty("customerName").GetString());
        Assert.False(result.TryGetProperty("customer_name", out _));
        _cache.Verify(x => x.SetAsync(
            "mapping:system-1",
            It.IsAny<IReadOnlyList<FieldMapping>>(),
            TimeSpan.FromMinutes(10),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyMappingAsync_UsesCachedMappingsWhenAvailable()
    {
        IReadOnlyList<FieldMapping> cached =
        [
            CreateMapping("system-1", "Customer", "external_id", "externalId", "string")
        ];
        _cache.Setup(x => x.GetAsync<IReadOnlyList<FieldMapping>>("mapping:system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);
        var service = CreateService();

        var result = await service.ApplyMappingAsync(
            "system-1",
            "Customer",
            Json("""{"external_id":"C-001"}"""),
            CancellationToken.None);

        Assert.Equal("C-001", result.GetProperty("externalId").GetString());
        _repository.Verify(x => x.GetFieldMappingsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(x => x.SetAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<FieldMapping>>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyMappingAsync_FallsBackToRawPayloadWhenNoMappingsExist()
    {
        _cache.Setup(x => x.GetAsync<IReadOnlyList<FieldMapping>>("mapping:system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<FieldMapping>?)null);
        _repository.Setup(x => x.GetFieldMappingsAsync("system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<FieldMapping>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = CreateService();
        var payload = Json("""{"customer_name":"Acme","age":"42"}""");

        var result = await service.ApplyMappingAsync("system-1", "Customer", payload, CancellationToken.None);

        Assert.Equal("Acme", result.GetProperty("customer_name").GetString());
        Assert.Equal("42", result.GetProperty("age").GetString());
    }

    [Fact]
    public async Task ApplyMappingAsync_AppliesDataTypeConversionFromStringToInt()
    {
        _cache.Setup(x => x.GetAsync<IReadOnlyList<FieldMapping>>("mapping:system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<FieldMapping>?)null);
        _repository.Setup(x => x.GetFieldMappingsAsync("system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                CreateMapping("system-1", "Customer", "age", "age", "int")
            ]);
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<FieldMapping>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var service = CreateService();

        var result = await service.ApplyMappingAsync(
            "system-1",
            "Customer",
            Json("""{"age":"42"}"""),
            CancellationToken.None);

        Assert.Equal(JsonValueKind.Number, result.GetProperty("age").ValueKind);
        Assert.Equal(42, result.GetProperty("age").GetInt32());
    }

    [Fact]
    public async Task ValidateMappingAsync_ReturnsValidWhenAllRequiredFieldsPresent()
    {
        SetupMappings(
        [
            CreateMapping("system-1", "Customer", "customer_name", "customerName", "string", isRequired: true),
            CreateMapping("system-1", "Customer", "age", "age", "int", isRequired: true)
        ]);
        var service = CreateService();

        var result = await service.ValidateMappingAsync(
            "system-1",
            "Customer",
            Json("""{"customer_name":"Acme","age":42}"""),
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateMappingAsync_ReturnsInvalidWhenRequiredFieldMissing()
    {
        SetupMappings(
        [
            CreateMapping("system-1", "Customer", "customer_name", "customerName", "string", isRequired: true)
        ]);
        var service = CreateService();

        var result = await service.ValidateMappingAsync(
            "system-1",
            "Customer",
            Json("""{"external_id":"C-001"}"""),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("customer_name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateMappingAsync_ReturnsInvalidWhenDataTypeMismatch()
    {
        SetupMappings(
        [
            CreateMapping("system-1", "Customer", "age", "age", "int", isRequired: true)
        ]);
        var service = CreateService();

        var result = await service.ValidateMappingAsync(
            "system-1",
            "Customer",
            Json("""{"age":"42"}"""),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("age", StringComparison.Ordinal));
    }

    private void SetupMappings(IReadOnlyList<FieldMapping> mappings)
    {
        _cache.Setup(x => x.GetAsync<IReadOnlyList<FieldMapping>>("mapping:system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<FieldMapping>?)null);
        _repository.Setup(x => x.GetFieldMappingsAsync("system-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mappings);
        _cache.Setup(x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<FieldMapping>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static FieldMapping CreateMapping(
        string systemId,
        string doctype,
        string sourceField,
        string targetField,
        string dataType,
        bool isRequired = false) =>
        new(systemId, sourceField, targetField, dataType, transformExpression: null, isRequired)
        {
            ErpNextDoctype = doctype
        };

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
