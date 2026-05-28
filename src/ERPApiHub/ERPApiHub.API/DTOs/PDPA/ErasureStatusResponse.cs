namespace ERPApiHub.API.DTOs.PDPA;

/// <summary>
/// Current PDPA erasure request status.
/// </summary>
public sealed record ErasureStatusResponse
{
    /// <summary>Erasure request identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Data subject identifier.</summary>
    public required string SubjectId { get; init; }

    /// <summary>Current erasure request status.</summary>
    public required string Status { get; init; }

    /// <summary>ERPNext doctypes requested for erasure.</summary>
    public required IReadOnlyList<string> RequestedDoctypes { get; init; }

    /// <summary>When the erasure request was submitted.</summary>
    public DateTimeOffset RequestedAt { get; init; }

    /// <summary>When the erasure request completed, if applicable.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
}
