namespace ERPApiHub.Application.RateLimiting;

/// <summary>
/// Endpoint type for per-endpoint rate limit reduction (FRD FR-RLM-002).
/// </summary>
public enum EndpointType
{
    /// <summary>Ingestion endpoints: 50% of tier limit</summary>
    Ingestion,

    /// <summary>Query endpoints: 100% of tier limit</summary>
    Query,

    /// <summary>Webhook management: 10% of tier limit</summary>
    WebhookManagement,

    /// <summary>Admin endpoints: 5% of tier limit</summary>
    Admin,

    /// <summary>Uncategorized: 100% of tier limit</summary>
    Other
}