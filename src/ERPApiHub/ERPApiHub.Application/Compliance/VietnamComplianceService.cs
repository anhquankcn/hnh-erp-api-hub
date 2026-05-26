using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ERPApiHub.Application.Compliance;

/// <summary>
/// Vietnam compliance validation service — Tax ID (MST), e-invoice format, PDPA.
/// FRD refs: §13 (FR-VNC-001~007), FR-SEC-008
/// </summary>
public sealed class VietnamComplianceService
{
    private static readonly Regex TaxIdPattern = new(
        @"^\d{10}(-\d{3})?$",
        RegexOptions.Compiled);

    private static readonly Regex TaxIdForeignPattern = new(
        @"^\d{2}\d{8}(-\d{3})?$",
        RegexOptions.Compiled);

    private static readonly Regex InvoiceTemplatePattern = new(
        @"^[0-9]{2}/[0-9]{3}/[A-Z]{2,3}$",
        RegexOptions.Compiled);

    private static readonly Regex InvoiceSymbolPattern = new(
        @"^[C,K,L,M,N,P,Q,R,S,T,V,X]{1}\d{2}[A-Z]{0,2}$",
        RegexOptions.Compiled);

    /// <summary>
    /// Validate Vietnam Tax ID (Mã số thuế).
    /// 10 digits for domestic entities; optional 3-digit branch suffix.
    /// </summary>
    public (bool IsValid, string? Error) ValidateTaxId(string taxId)
    {
        if (string.IsNullOrWhiteSpace(taxId))
            return (false, "Tax ID is required.");

        taxId = taxId.Trim();

        if (taxId.Length < 10 || taxId.Length > 14)
            return (false, "Tax ID must be 10 digits or 10-3 digits with branch suffix.");

        if (!TaxIdPattern.IsMatch(taxId) && !TaxIdForeignPattern.IsMatch(taxId))
            return (false, "Tax ID format invalid. Expected 10 digits or 10-3 digits with hyphen separator.");

        // Validate check digit for 10-digit MST
        var baseNumber = taxId.Contains('-')
            ? taxId.Split('-')[0]
            : taxId;

        if (baseNumber.Length == 10)
        {
            if (!ValidateCheckDigit(baseNumber))
                return (false, "Tax ID check digit verification failed.");
        }

        return (true, null);
    }

    /// <summary>
    /// Validate e-invoice template number format: XX/XXX/XX(X)
   /// FRD ref: FR-VNC-004
    /// </summary>
    public (bool IsValid, string? Error) ValidateInvoiceTemplate(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return (false, "Invoice template number is required.");

        if (!InvoiceTemplatePattern.IsMatch(template.Trim()))
            return (false, "Invoice template format invalid. Expected pattern: XX/XXX/XX(X) e.g. 01/001/PXT");

        return (true, null);
    }

    /// <summary>
    /// Validate e-invoice symbol format.
    /// FRD ref: FR-VNC-005
    /// </summary>
    public (bool IsValid, string? Error) ValidateInvoiceSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return (false, "Invoice symbol is required.");

        if (!InvoiceSymbolPattern.IsMatch(symbol.Trim()))
            return (false, "Invoice symbol format invalid. Expected: C24, K23AA, etc.");

        return (true, null);
    }

    /// <summary>
    /// Validate complete e-invoice record.
    /// FRD ref: FR-VNC-003~007
    /// </summary>
    public (bool IsValid, List<string> Errors) ValidateEInvoice(EInvoiceRequest invoice)
    {
        var errors = new List<string>();

        // Buyer tax ID (optional for retail, validate if provided)
        if (!string.IsNullOrWhiteSpace(invoice.BuyerTaxId))
        {
            var (valid, error) = ValidateTaxId(invoice.BuyerTaxId);
            if (!valid && error is not null) errors.Add($"BuyerTaxId: {error}");
        }

        // Seller tax ID (required)
        if (string.IsNullOrWhiteSpace(invoice.SellerTaxId))
        {
            errors.Add("SellerTaxId is required for e-invoice.");
        }
        else
        {
            var (valid, error) = ValidateTaxId(invoice.SellerTaxId);
            if (!valid && error is not null) errors.Add($"SellerTaxId: {error}");
        }

        // Invoice template
        if (!string.IsNullOrWhiteSpace(invoice.InvoiceTemplate))
        {
            var (valid, error) = ValidateInvoiceTemplate(invoice.InvoiceTemplate);
            if (!valid && error is not null) errors.Add($"InvoiceTemplate: {error}");
        }

        // Invoice symbol
        if (!string.IsNullOrWhiteSpace(invoice.InvoiceSymbol))
        {
            var (valid, error) = ValidateInvoiceSymbol(invoice.InvoiceSymbol);
            if (!valid && error is not null) errors.Add($"InvoiceSymbol: {error}");
        }

        // Invoice number format (6 digits)
        if (!string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
        {
            if (!Regex.IsMatch(invoice.InvoiceNumber, @"^\d{6}$"))
                errors.Add("InvoiceNumber must be exactly 6 digits.");
        }

        // Currency code (VND default)
        if (!string.IsNullOrWhiteSpace(invoice.CurrencyCode) &&
            invoice.CurrencyCode != "VND" &&
            invoice.CurrencyCode.Length != 3)
        {
            errors.Add("CurrencyCode must be a valid 3-letter ISO 4217 code or 'VND'.");
        }

        return (errors.Count == 0, errors);
    }

    /// <summary>
    /// Vietnam MST check digit validation (mod 11 algorithm).
    /// </summary>
    private static bool ValidateCheckDigit(string taxId)
    {
        if (taxId.Length != 10) return false;

        var weights = new[] { 31, 29, 23, 19, 17, 13, 7, 5, 3 };
        var sum = 0;

        for (var i = 0; i < 9; i++)
        {
            if (!char.IsDigit(taxId[i])) return false;
            sum += (taxId[i] - '0') * weights[i];
        }

        var checkDigit = sum % 11;
        if (checkDigit >= 10) checkDigit = checkDigit - 10;

        var lastDigit = taxId[9] - '0';
        return lastDigit == checkDigit;
    }
}

/// <summary>
/// E-invoice validation request.
/// FRD ref: §13
/// </summary>
public sealed class EInvoiceRequest
{
    [JsonPropertyName("buyer_tax_id")]
    public string? BuyerTaxId { get; init; }

    [JsonPropertyName("seller_tax_id")]
    public string SellerTaxId { get; init; } = string.Empty;

    [JsonPropertyName("invoice_template")]
    public string? InvoiceTemplate { get; init; }

    [JsonPropertyName("invoice_symbol")]
    public string? InvoiceSymbol { get; init; }

    [JsonPropertyName("invoice_number")]
    public string? InvoiceNumber { get; init; }

    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; init; } = "VND";

    [JsonPropertyName("invoice_type")]
    public string? InvoiceType { get; init; }
}