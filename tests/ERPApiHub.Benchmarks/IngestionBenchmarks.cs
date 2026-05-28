using System.Security.Claims;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using ERPApiHub.Application.Abstractions;
using ERPApiHub.Application.Ingestion;
using ERPApiHub.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ERPApiHub.Benchmarks;

[MemoryDiagnoser]
public class IngestionBenchmarks
{
    private readonly Mock<IAllowedDoctypeValidator> _doctypeValidator = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IErpHubRepository> _repository = new();
    private readonly Mock<IMessageBus> _messageBus = new();
    private IngestionService _ingestionService = null!;
    private JsonElement _payload;
    private IReadOnlyList<BatchOperation> _batchOperations = [];

    [GlobalSetup]
    public void GlobalSetup()
    {
        _payload = JsonDocument.Parse("""
            {"name":"CUST-BENCH","customer_name":"Benchmark Customer","customer_type":"Company"}
            """).RootElement.Clone();

        _batchOperations = Enumerable.Range(1, 25)
            .Select(i => new BatchOperation("Customer", JsonDocument.Parse($$"""
                {"name":"CUST-BENCH-{{i:D3}}","customer_name":"Benchmark Customer {{i}}","customer_type":"Company"}
                """).RootElement.Clone()))
            .ToArray();

        _doctypeValidator.Setup(x => x.IsAllowed("Customer")).Returns(true);
        _cache
            .Setup(x => x.GetAsync<IngestionResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IngestionResponse?)null);
        _cache
            .Setup(x => x.SetAsync(It.IsAny<string>(), It.IsAny<JobStatusResponse>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _messageBus
            .Setup(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ErpEventEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(x => x.CreateAuditLogAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuditLog log, CancellationToken _) => log);

        _ingestionService = new IngestionService(
            _doctypeValidator.Object,
            _cache.Object,
            _repository.Object,
            new HttpContextAccessor { HttpContext = CreateHttpContext() },
            _messageBus.Object,
            NullLogger<IngestionService>.Instance);
    }

    [Benchmark(Baseline = true)]
    public async Task<IngestionResponse> SingleIngestion()
    {
        return await _ingestionService.IngestAsync("Customer", _payload, "CUST-BENCH", CancellationToken.None);
    }

    [Benchmark]
    public async Task<IngestionResponse> BatchIngestion()
    {
        return await _ingestionService.BatchIngestAsync(_batchOperations, CancellationToken.None);
    }

    private static HttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("BranchId", "SGN"),
                new Claim(ClaimTypes.NameIdentifier, "bench-user")
            ], "benchmark"))
        };
        context.Request.Headers["X-Request-ID"] = "bench-correlation";
        return context;
    }
}
