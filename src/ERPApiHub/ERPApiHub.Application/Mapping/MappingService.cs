using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Mapping;

public sealed class MappingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly IErpHubRepository _repository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<MappingService> _logger;

    public MappingService(
        IErpHubRepository repository,
        ICacheService cacheService,
        ILogger<MappingService> logger)
    {
        _repository = repository;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<JsonElement> MapAsync(
        string sourceSystem,
        string targetSystem,
        string doctype,
        JsonElement sourcePayload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(doctype);

        var mappings = await GetMappingsAsync(sourceSystem, doctype, cancellationToken);
        var pipeline = new TransformationPipeline([]);
        var transformed = await pipeline.TransformAsync(sourcePayload, mappings, cancellationToken);

        _logger.LogDebug(
            "Applied {MappingCount} mappings from {SourceSystem} to {TargetSystem} for doctype {Doctype}",
            mappings.Count, sourceSystem, targetSystem, doctype);

        return transformed;
    }

    public async Task<JsonElement> ApplyMappingAsync(
        string systemId,
        string sourceDoctype,
        JsonElement sourcePayload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDoctype);

        var mappings = await GetMappingsAsync(systemId, sourceDoctype, cancellationToken);
        if (mappings.Count == 0)
        {
            return sourcePayload.Clone();
        }

        var target = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetSourceValue(sourcePayload, mapping.ExternalField, out var sourceValue))
            {
                continue;
            }

            target[mapping.ErpNextField] = ConvertValue(sourceValue, mapping.DataType);
        }

        return ToJsonElement(target);
    }

    public async Task<MappingValidationResult> ValidateMappingAsync(
        string systemId,
        string sourceDoctype,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDoctype);

        var mappings = await GetMappingsAsync(systemId, sourceDoctype, cancellationToken);
        var errors = new List<string>();

        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetSourceValue(payload, mapping.ExternalField, out var value))
            {
                if (mapping.IsRequired)
                {
                    errors.Add($"Required field '{mapping.ExternalField}' is missing.");
                }

                continue;
            }

            if (!IsTypeMatch(value, mapping.DataType))
            {
                errors.Add($"Field '{mapping.ExternalField}' must be '{mapping.DataType}'.");
            }
        }

        return new MappingValidationResult(errors.Count == 0, errors);
    }

    public async Task<IReadOnlyList<FieldMapping>> GetMappingsAsync(
        string systemId,
        string doctype,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(doctype);

        var cacheKey = $"mapping:{systemId}";
        var cached = await _cacheService.GetAsync<IReadOnlyList<FieldMapping>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return FilterByDoctype(cached, doctype);
        }

        var mappings = await _repository.GetFieldMappingsAsync(systemId, cancellationToken);
        var cachedMappings = mappings
            .OrderBy(x => x.ExternalField)
            .ToList();

        await _cacheService.SetAsync(cacheKey, cachedMappings, CacheTtl, cancellationToken);

        return FilterByDoctype(cachedMappings, doctype);
    }

    private static IReadOnlyList<FieldMapping> FilterByDoctype(IReadOnlyList<FieldMapping> mappings, string doctype) =>
        mappings
            .Where(x =>
                string.IsNullOrWhiteSpace(x.ErpNextDoctype) ||
                string.Equals(x.ErpNextDoctype, doctype, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ExternalField)
            .ToList();

    private static bool TryGetSourceValue(JsonElement source, string fieldPath, out JsonElement value)
    {
        value = source;

        foreach (var segment in fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                value = default;
                return false;
            }
        }

        return true;
    }

    private static object? ConvertValue(JsonElement value, string dataType) =>
        NormalizeDataType(dataType) switch
        {
            "int" => value.ValueKind == JsonValueKind.Number
                ? value.GetInt32()
                : int.Parse(value.GetString() ?? string.Empty),
            "decimal" => value.ValueKind == JsonValueKind.Number
                ? value.GetDecimal()
                : decimal.Parse(value.GetString() ?? string.Empty),
            "double" => value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : double.Parse(value.GetString() ?? string.Empty),
            "bool" => value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : bool.Parse(value.GetString() ?? string.Empty),
            _ => JsonSerializer.Deserialize<object?>(value.GetRawText())
        };

    private static bool IsTypeMatch(JsonElement value, string dataType) =>
        NormalizeDataType(dataType) switch
        {
            "int" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            "decimal" or "double" => value.ValueKind == JsonValueKind.Number,
            "bool" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "string" => value.ValueKind == JsonValueKind.String,
            _ => true
        };

    private static string NormalizeDataType(string? dataType) =>
        dataType?.Trim().ToLowerInvariant() switch
        {
            "integer" => "int",
            "boolean" => "bool",
            "number" => "decimal",
            null or "" => "string",
            var value => value
        };

    private static JsonElement ToJsonElement(Dictionary<string, object?> value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }
}
