using System.Text.Json;
using ERPApiHub.Domain.Entities;

namespace ERPApiHub.Application.Mapping;

public sealed class TransformationPipeline
{
    private readonly IReadOnlyList<IFieldTransformer> _transformers;

    public TransformationPipeline(IReadOnlyList<IFieldTransformer> transformers)
    {
        _transformers = transformers;
    }

    public Task<JsonElement> TransformAsync(
        JsonElement source,
        IReadOnlyList<FieldMapping> mappings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryGetSourceValue(source, mapping.ExternalField, out var sourceValue))
            {
                continue;
            }

            var transformed = ApplyTransform(sourceValue, mapping.TransformRule);
            target[mapping.ErpNextField] = JsonSerializer.Deserialize<object?>(transformed.GetRawText());
        }

        return Task.FromResult(ToJsonElement(target));
    }

    private JsonElement ApplyTransform(JsonElement value, string? transformRule)
    {
        if (string.IsNullOrWhiteSpace(transformRule))
        {
            return value.Clone();
        }

        var transformer = _transformers.FirstOrDefault(x => x.CanTransform(transformRule));
        return transformer is null
            ? value.Clone()
            : transformer.Transform(value, transformRule).Clone();
    }

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

    private static JsonElement ToJsonElement(Dictionary<string, object?> value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        using var document = JsonDocument.Parse(bytes);
        return document.RootElement.Clone();
    }
}

public interface IFieldTransformer
{
    bool CanTransform(string format);

    JsonElement Transform(JsonElement value, string format);
}
