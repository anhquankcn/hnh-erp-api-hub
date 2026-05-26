namespace ERPApiHub.Infrastructure.ErpNext;

public sealed class ErpNextOptions
{
    public const string SectionName = "ErpNext";

    public string BaseUrl { get; set; } = "http://erpnext:8080";
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
}