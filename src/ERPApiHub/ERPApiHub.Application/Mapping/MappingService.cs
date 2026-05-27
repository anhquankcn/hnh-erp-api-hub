using System.Text.Json;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ERPApiHub.Application.Mapping;

public sealed class MappingService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

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

    public async Task<IReadOnlyList<FieldMapping>> GetMappingsAsync(
        string systemId,
        string doctype,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(doctype);

        var cacheKey = $"mapping:{systemId}:{doctype}";
        var cached = await _cacheService.GetAsync<IReadOnlyList<FieldMapping>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var mappings = await _repository.GetFieldMappingsAsync(systemId, cancellationToken);
        var filtered = mappings
            .Where(x => string.Equals(x.ErpNextDoctype, doctype, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ExternalField)
            .ToList();

        await _cacheService.SetAsync(cacheKey, filtered, CacheTtl, cancellationToken);

        return filtered;
    }
}
