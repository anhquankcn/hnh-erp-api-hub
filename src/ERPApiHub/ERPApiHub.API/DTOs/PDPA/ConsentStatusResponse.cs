namespace ERPApiHub.API.DTOs.PDPA;

/// <summary>
/// Current PDPA consent status for a data subject.
/// </summary>
public sealed record ConsentStatusResponse
{
    /// <summary>Data subject identifier.</summary>
    public required string SubjectId { get; init; }

    /// <summary>Whether the subject has at least one active, unexpired consent.</summary>
    public bool HasActiveConsent { get; init; }

    /// <summary>Known consent records for the subject.</summary>
    public required IReadOnlyList<ConsentResponse> Consents { get; init; }
}
