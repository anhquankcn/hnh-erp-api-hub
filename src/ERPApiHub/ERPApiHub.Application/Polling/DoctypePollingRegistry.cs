using Microsoft.Extensions.Options;

namespace ERPApiHub.Application.Polling;

public sealed class DoctypePollingRegistry
{
    private static readonly IReadOnlyList<PollingDoctypeOptions> DefaultDoctypes =
    [
        new() { Name = "Sales Invoice", Priority = PollingPriority.Critical, LastCursorField = "modified" },
        new() { Name = "Customer", Priority = PollingPriority.Standard, LastCursorField = "modified" },
        new() { Name = "Item", Priority = PollingPriority.Standard, LastCursorField = "modified" },
        new() { Name = "Sales Order", Priority = PollingPriority.Critical, LastCursorField = "modified" },
        new() { Name = "Purchase Order", Priority = PollingPriority.Standard, LastCursorField = "modified" }
    ];

    private readonly PollingOptions _options;

    public DoctypePollingRegistry(IOptions<PollingOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<DoctypePollingRegistration> GetActiveDoctypes()
    {
        var configured = _options.Doctypes.Count > 0 ? _options.Doctypes : DefaultDoctypes;

        return configured
            .Where(doctype => doctype.Enabled && !string.IsNullOrWhiteSpace(doctype.Name))
            .Select(CreateRegistration)
            .ToArray();
    }

    private DoctypePollingRegistration CreateRegistration(PollingDoctypeOptions doctype)
    {
        var priority = NormalizePriority(doctype.Priority);
        var interval = doctype.Interval ?? GetDefaultInterval(priority);

        return new DoctypePollingRegistration(
            doctype.Name,
            interval,
            priority,
            string.IsNullOrWhiteSpace(doctype.LastCursorField) ? "modified" : doctype.LastCursorField);
    }

    private TimeSpan GetDefaultInterval(string priority) =>
        priority == PollingPriority.Critical
            ? _options.CriticalInterval
            : _options.StandardInterval;

    private static string NormalizePriority(string? priority) =>
        string.Equals(priority, PollingPriority.Critical, StringComparison.OrdinalIgnoreCase)
            ? PollingPriority.Critical
            : PollingPriority.Standard;
}

public sealed record DoctypePollingRegistration(
    string Doctype,
    TimeSpan Interval,
    string Priority,
    string LastCursorField);
