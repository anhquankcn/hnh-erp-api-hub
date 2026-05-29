namespace ERPApiHub.Domain.Events;

public sealed record ErpNextChangeEvent(
    string Doctype,
    string Name,
    DateTimeOffset ModifiedTimestamp,
    string TenantId,
    string Source = "polling");
