namespace ERPApiHub.Application.Ingestion;

public interface IAllowedDoctypeValidator
{
    bool IsAllowed(string doctype);
}

public sealed class AllowedDoctypeValidator : IAllowedDoctypeValidator
{
    private readonly HashSet<string> _allowedDoctypes;

    public AllowedDoctypeValidator(IEnumerable<string> allowedDoctypes)
    {
        _allowedDoctypes = new HashSet<string>(allowedDoctypes, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAllowed(string doctype)
    {
        return _allowedDoctypes.Contains(doctype);
    }
}
