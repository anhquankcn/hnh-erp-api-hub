namespace ERPApiHub.API.DTOs.PDPA;

/// <summary>
/// PDPA consent response.
/// </summary>
public sealed record ConsentResponse
{
    /// <summary>Consent identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Data subject identifier.</summary>
    public required string SubjectId { get; init; }

    /// <summary>Processing purpose covered by the consent.</summary>
    public required string Purpose { get; init; }

    /// <summary>ERPNext doctypes covered by the consent.</summary>
    public required IReadOnlyList<string> Doctypes { get; init; }

    /// <summary>Consent status.</summary>
    public required string Status { get; init; }

    /// <summary>When consent was granted.</summary>
    public DateTimeOffset GrantedAt { get; init; }

    /// <summary>When consent was withdrawn, if applicable.</summary>
    public DateTimeOffset? WithdrawnAt { get; init; }

    /// <summary>Optional consent expiry date.</summary>
    public DateTimeOffset? ExpiryDate { get; init; }

    /// <summary>Optional consent notes.</summary>
    public string? Notes { get; init; }
}
