using System.ComponentModel.DataAnnotations;

namespace ERPApiHub.API.DTOs.PDPA;

/// <summary>
/// Request payload for recording PDPA consent from a data subject.
/// </summary>
public sealed record CreateConsentRequest
{
    /// <summary>Data subject identifier.</summary>
    [Required]
    public required string SubjectId { get; init; }

    /// <summary>Processing purpose covered by the consent.</summary>
    [Required]
    public required string Purpose { get; init; }

    /// <summary>ERPNext doctypes covered by the consent.</summary>
    [Required]
    public required IReadOnlyList<string> Doctypes { get; init; }

    /// <summary>Optional consent expiry date.</summary>
    public DateTimeOffset? ExpiryDate { get; init; }

    /// <summary>Optional consent notes.</summary>
    public string? Notes { get; init; }
}
