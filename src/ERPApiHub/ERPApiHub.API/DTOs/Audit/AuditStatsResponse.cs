namespace ERPApiHub.API.DTOs.Audit;

public sealed class AuditStatsResponse
{
    public long TotalEvents { get; set; }
    public long SuccessCount { get; set; }
    public long FailureCount { get; set; }
    public long WarningCount { get; set; }
    public Dictionary<string, long> EventsByType { get; set; } = [];
    public Dictionary<string, long> EventsByTenant { get; set; } = [];
    public Dictionary<string, long> EventsByDay { get; set; } = [];
}
