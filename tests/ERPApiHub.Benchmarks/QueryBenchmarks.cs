using System.Security.Claims;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Query;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ERPApiHub.Benchmarks;

[MemoryDiagnoser]
public class QueryBenchmarks
{
    private readonly Mock<IErpNextClient> _erpNextClient = new();
    private readonly Mock<IErpHubRepository> _repository = new();
    private QueryService _cachedQueryService = null!;
    private QueryService _uncachedQueryService = null!;
    private QueryRequest _request = null!;
    private PaginatedResponse<JsonElement> _cachedResponse = null!;
    private JsonElement _erpResponse;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _request = new QueryRequest
        {
            Doctype = "Customer",
            Page = 1,
            PageSize = 20,
            Fields = """["name","customer_name","modified"]""",
            OrderBy = "modified desc"
        };

        _erpResponse = JsonDocument.Parse("""
            {"data":[
              {"name":"CUST-001","customer_name":"Acme One","modified":"2026-05-28 08:00:00"},
              {"name":"CUST-002","customer_name":"Acme Two","modified":"2026-05-28 08:01:00"},
              {"name":"CUST-003","customer_name":"Acme Three","modified":"2026-05-28 08:02:00"}
            ]}
            """).RootElement.Clone();

        _cachedResponse = new PaginatedResponse<JsonElement>
        {
            Data = _erpResponse.GetProperty("data").EnumerateArray().Select(x => x.Clone()).ToList(),
            Pagination = new PaginationMeta { Page = 1, PageSize = 20, TotalCount = 3 }
        };

        _erpNextClient
            .Setup(x => x.GetAsync<JsonElement>(It.IsAny<string>(), "SGN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ErpNextResponse<JsonElement>(_erpResponse, 200, null));

        _repository
            .Setup(x => x.CreateAuditLogAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);

        var cacheHit = new Mock<ICacheService>();
        cacheHit
            .Setup(x => x.GetAsync<PaginatedResponse<JsonElement>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_cachedResponse);

        var cacheMiss = new Mock<ICacheService>();
        cacheMiss
            .Setup(x => x.GetAsync<PaginatedResponse<JsonElement>>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaginatedResponse<JsonElement>?)null);
        cacheMiss
            .Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<PaginatedResponse<JsonElement>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _cachedQueryService = new QueryService(
            _erpNextClient.Object,
            cacheHit.Object,
            _repository.Object,
            new HttpContextAccessor { HttpContext = CreateHttpContext(noCache: false) },
            NullLogger<QueryService>.Instance);

        _uncachedQueryService = new QueryService(
            _erpNextClient.Object,
            cacheMiss.Object,
            _repository.Object,
            new HttpContextAccessor { HttpContext = CreateHttpContext(noCache: false) },
            NullLogger<QueryService>.Instance);
    }

    [Benchmark(Baseline = true)]
    public async Task<PaginatedResponse<JsonElement>> ListAsyncWithCache()
    {
        return await _cachedQueryService.ListAsync(_request, "SGN", CancellationToken.None);
    }

    [Benchmark]
    public async Task<PaginatedResponse<JsonElement>> ListAsyncWithoutCache()
    {
        return await _uncachedQueryService.ListAsync(_request, "SGN", CancellationToken.None);
    }

    private static HttpContext CreateHttpContext(bool noCache)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("BranchId", "SGN"),
                new Claim(ClaimTypes.NameIdentifier, "bench-user")
            ], "benchmark"))
        };

        if (noCache)
        {
            context.Request.Headers.CacheControl = "no-cache";
        }

        return context;
    }
}
