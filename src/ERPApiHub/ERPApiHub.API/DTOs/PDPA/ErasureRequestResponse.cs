namespace ERPApiHub.API.DTOs.PDPA;

/// <summary>
/// PDPA erasure request response.
/// </summary>
public sealed record ErasureRequestResponse
{
    /// <summary>Erasure request identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Data subject identifier.</summary>
    public required string SubjectId { get; init; }

    /// <summary>Reason for requesting erasure.</summary>
    public required string Reason { get; init; }

    /// <summary>ERPNext doctypes requested for erasure.</summary>
    public required IReadOnlyList<string> RequestedDoctypes { get; init; }

    /// <summary>Erasure request status.</summary>
    public required string Status { get; init; }

    /// <summary>When the erasure request was submitted.</summary>
    public DateTimeOffset RequestedAt { get; init; }
}
