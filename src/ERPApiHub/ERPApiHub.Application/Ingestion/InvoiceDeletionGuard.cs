using System.Text.Json;
using ERPApiHub.Application.Abstractions;

namespace ERPApiHub.Application.Ingestion;

public sealed record InvoiceDeletionResult(bool CanDelete, bool RequiresAudit, string? Reason);

public sealed class InvoiceDeletionGuard
{
    private const string SalesInvoiceDoctype = "Sales Invoice";
    private const string IssuedStatus = "Issued";
    private readonly IErpNextClient _erpNextClient;

    public InvoiceDeletionGuard(IErpNextClient erpNextClient)
    {
        _erpNextClient = erpNextClient;
    }

    public async Task<InvoiceDeletionResult> CanDeleteAsync(
        string doctype,
        string name,
        bool force,
        string userRole,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(doctype, SalesInvoiceDoctype, StringComparison.Ordinal))
        {
            return new InvoiceDeletionResult(true, false, null);
        }

        var invoice = await _erpNextClient.GetAsync<JsonElement>(
            $"{SalesInvoiceDoctype}/{Uri.EscapeDataString(name)}",
            cancellationToken);
        if (!invoice.IsSuccessStatusCode || invoice.Data is null)
        {
            return new InvoiceDeletionResult(false, false, "Invoice status could not be verified");
        }

        var status = ExtractStatus(invoice.Data);
        if (status is null)
        {
            return new InvoiceDeletionResult(false, false, "Invoice status could not be verified");
        }

        if (!string.Equals(status, IssuedStatus, StringComparison.Ordinal))
        {
            return new InvoiceDeletionResult(true, false, null);
        }

        if (!force)
        {
            return new InvoiceDeletionResult(false, false, "Invoice has been issued");
        }

        return string.Equals(userRole, "admin", StringComparison.OrdinalIgnoreCase)
            ? new InvoiceDeletionResult(true, true, null)
            : new InvoiceDeletionResult(false, false, "Invoice has been issued");
    }

    private static string? ExtractStatus(JsonElement invoice)
    {
        if (invoice.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (invoice.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.String)
        {
            return status.GetString();
        }

        if (invoice.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("status", out var nestedStatus)
            && nestedStatus.ValueKind == JsonValueKind.String)
        {
            return nestedStatus.GetString();
        }

        return null;
    }
}
