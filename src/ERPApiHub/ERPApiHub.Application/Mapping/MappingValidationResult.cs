namespace ERPApiHub.Application.Mapping;

public sealed record MappingValidationResult(bool IsValid, IReadOnlyList<string> Errors);
