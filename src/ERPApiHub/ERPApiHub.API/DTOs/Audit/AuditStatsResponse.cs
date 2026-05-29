namespace ERPApiHub.API.DTOs.Audit;

public sealed class AuditStatsResponse
{
    public long TotalEvents { get; set; }
    public long SuccessCount { get; set; }
    public long FailureCount { get; set; }
    public long WarningCount { get; set; }
    public IReadOnlyDictionary<string, long> EventsByType { get; set; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> EventsByTenant { get; set; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, long> EventsByDay { get; set; } = new Dictionary<string, long>();
}
