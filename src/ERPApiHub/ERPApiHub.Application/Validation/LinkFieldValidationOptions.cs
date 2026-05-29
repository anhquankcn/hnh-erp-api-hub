using System.ComponentModel.DataAnnotations;

namespace ERPApiHub.Application.Validation;

public sealed class LinkFieldValidationOptions
{
    public const string SectionName = "LinkFieldValidation";

    public bool Enabled { get; set; } = true;

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 5;

    public bool FailOnInvalid { get; set; } = false;

    public List<string> ExcludedDoctypes { get; set; } = ["User", "Role Profile"];
}
