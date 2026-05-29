using System.ComponentModel.DataAnnotations;

namespace ERPApiHub.API.DTOs.PDPA;

public sealed class WithdrawConsentRequest
{
    [Required]
    public required string SubjectId { get; set; }

    public string? Reason { get; set; }
}
