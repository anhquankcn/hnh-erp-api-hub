using System.Text.Json;
using System.Text.RegularExpressions;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Domain.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERPApiHub.Application.Validation;

public sealed class LinkFieldValidator(
    IErpNextClient erpNextClient,
    ICacheService cache,
    IOptions<LinkFieldValidationOptions> options,
    ILogger<LinkFieldValidator> logger)
{
    private readonly LinkFieldValidationOptions _options = options.Value;
    private static readonly TimeSpan SchemaCacheTtl = TimeSpan.FromHours(1);
    private static readonly Regex DoctypeWhitelistPattern = new(
        "^[A-Za-z][A-Za-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private const string SchemaCachePrefix = "erphub:schema";

    public async Task<LinkValidationResult> ValidateAsync(
        string tenantId,
        string doctype,
        Dictionary<string, object?> fields,
        CancellationToken ct)
    {
        if (!_options.Enabled || _options.ExcludedDoctypes.Contains(doctype, StringComparer.OrdinalIgnoreCase))
        {
            return new LinkValidationResult(true, [], []);
        }

        var schema = await GetCachedSchemaAsync(tenantId, doctype, ct);
        if (schema is null)
        {
            logger.LogWarning(
                "Could not retrieve schema for {Doctype}; skipping link validation.",
                doctype);
            return new LinkValidationResult(true, [], []);
        }

        var linkFields = schema
            .Where(f => string.Equals(f.FieldType, "Link", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (linkFields.Count == 0)
        {
            return new LinkValidationResult(true, [], []);
        }

        var invalidFields = new List<InvalidLinkField>();
        var validFields = new List<ValidLinkField>();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        foreach (var linkField in linkFields)
        {
            if (!fields.TryGetValue(linkField.FieldName, out var rawValue) || rawValue is null)
            {
                continue;
            }

            var value = rawValue.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            try
            {
                if (!IsWhitelistedDoctype(linkField.LinkedDoctype))
                {
                    logger.LogWarning(
                        "Skipping link validation for {Doctype}.{Field} because linked doctype failed whitelist validation.",
                        doctype,
                        linkField.FieldName);

                    invalidFields.Add(new InvalidLinkField(
                        linkField.FieldName,
                        value,
                        linkField.LinkedDoctype,
                        "Invalid linked doctype"));

                    continue;
                }

                var exists = await CheckRecordExistsAsync(
                    linkField.LinkedDoctype,
                    value,
                    linkedCts.Token);

                if (exists)
                {
                    validFields.Add(new ValidLinkField(
                        linkField.FieldName,
                        value,
                        linkField.LinkedDoctype));
                }
                else
                {
                    invalidFields.Add(new InvalidLinkField(
                        linkField.FieldName,
                        value,
                        linkField.LinkedDoctype,
                        $"Record '{value}' not found in '{linkField.LinkedDoctype}'"));
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                logger.LogWarning(
                    "Link validation timeout for {Doctype}.{Field} → {LinkedDoctype}:{Value}",
                    doctype,
                    linkField.FieldName,
                    linkField.LinkedDoctype,
                    value);

                invalidFields.Add(new InvalidLinkField(
                    linkField.FieldName,
                    value,
                    linkField.LinkedDoctype,
                    "Validation timeout"));
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Link validation error for {Doctype}.{Field} → {LinkedDoctype}:{Value}",
                    doctype,
                    linkField.FieldName,
                    linkField.LinkedDoctype,
                    value);

                invalidFields.Add(new InvalidLinkField(
                    linkField.FieldName,
                    value,
                    linkField.LinkedDoctype,
                    "Validation error"));
            }
        }

        return new LinkValidationResult(invalidFields.Count == 0, invalidFields, validFields);
    }

    private async Task<List<DocFieldSchema>?> GetCachedSchemaAsync(
        string tenantId,
        string doctype,
        CancellationToken ct)
    {
        var cacheKey = $"{SchemaCachePrefix}:{tenantId}:{doctype}";
        var cached = await cache.GetAsync<List<DocFieldSchema>>(cacheKey, ct);
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var response = await erpNextClient.GetAsync<JsonElement>(
                $"resource/DocField?filters=[[\"parent\",\"=\",\"{Uri.EscapeDataString(doctype)}\"],[\"fieldtype\",\"=\",\"Link\"]]&fields=[\"fieldname\",\"options\"]",
                ct);

            if (!response.IsSuccessStatusCode || response.Data is null)
            {
                return null;
            }

            var schema = ParseSchema(response.Data.Value);
            await cache.SetAsync(cacheKey, schema, SchemaCacheTtl, ct);
            return schema;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch schema for {Doctype}", doctype);
            return null;
        }
    }

    private static List<DocFieldSchema> ParseSchema(JsonElement data)
    {
        var result = new List<DocFieldSchema>();

        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("data", out var array))
        {
            data = array;
        }

        if (data.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var fieldName = item.TryGetProperty("fieldname", out var fn) ? fn.GetString() : null;
            var options = item.TryGetProperty("options", out var opt) ? opt.GetString() : null;

            if (!string.IsNullOrWhiteSpace(fieldName) && !string.IsNullOrWhiteSpace(options))
            {
                result.Add(new DocFieldSchema(fieldName, "Link", options));
            }
        }

        return result;
    }

    private async Task<bool> CheckRecordExistsAsync(
        string linkedDoctype,
        string value,
        CancellationToken ct)
    {
        if (!IsWhitelistedDoctype(linkedDoctype))
            throw new ArgumentException("Invalid doctype format.", nameof(linkedDoctype));

        var response = await erpNextClient.GetAsync<JsonElement>(
            $"resource/{Uri.EscapeDataString(linkedDoctype)}/{Uri.EscapeDataString(value)}",
            ct);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        if (response.StatusCode == 404)
        {
            return false;
        }

        throw new InvalidOperationException("ERPNext link lookup failed.");
    }

    private static bool IsWhitelistedDoctype(string? doctype) =>
        !string.IsNullOrWhiteSpace(doctype) && DoctypeWhitelistPattern.IsMatch(doctype);
}

public sealed record DocFieldSchema(
    string FieldName,
    string FieldType,
    string LinkedDoctype);

public sealed record LinkValidationResult(
    bool IsValid,
    IReadOnlyList<InvalidLinkField> InvalidFields,
    IReadOnlyList<ValidLinkField> ValidFields);

public sealed record InvalidLinkField(
    string FieldName,
    object Value,
    string LinkedDoctype,
    string Reason);

public sealed record ValidLinkField(
    string FieldName,
    object Value,
    string LinkedDoctype);
