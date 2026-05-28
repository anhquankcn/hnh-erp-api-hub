using System.ComponentModel.DataAnnotations;

namespace ERPApiHub.API.DTOs.PDPA;

/// <summary>
/// Request payload for submitting a PDPA data erasure request.
/// </summary>
public sealed record CreateErasureRequest
{
    /// <summary>Data subject identifier.</summary>
    [Required]
    public required string SubjectId { get; init; }

    /// <summary>Reason for requesting erasure.</summary>
    [Required]
    public required string Reason { get; init; }

    /// <summary>ERPNext doctypes requested for erasure.</summary>
    [Required]
    public required IReadOnlyList<string> RequestedDoctypes { get; init; }
}
