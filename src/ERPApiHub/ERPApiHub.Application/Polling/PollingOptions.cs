namespace ERPApiHub.Application.Polling;

public sealed class PollingOptions
{
    public const string SectionName = "Polling";

    public bool Enabled { get; set; } = true;
    public TimeSpan SchedulerInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan CriticalInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan StandardInterval { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan CursorTtl { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan InitialCursorLookback { get; set; } = TimeSpan.FromHours(1);
    public int BatchLimit { get; set; } = 100;
    public PollingBackoffOptions Backoff { get; set; } = new();
    public List<PollingDoctypeOptions> Doctypes { get; set; } = [];
}

public sealed class PollingBackoffOptions
{
    public TimeSpan RateLimitDelay { get; set; } = TimeSpan.FromSeconds(60);
}

public sealed class PollingDoctypeOptions
{
    public string Name { get; set; } = string.Empty;
    public TimeSpan? Interval { get; set; }
    public string Priority { get; set; } = PollingPriority.Standard;
    public string LastCursorField { get; set; } = "modified";
    public bool Enabled { get; set; } = true;
}

public static class PollingPriority
{
    public const string Critical = "critical";
    public const string Standard = "standard";
}
