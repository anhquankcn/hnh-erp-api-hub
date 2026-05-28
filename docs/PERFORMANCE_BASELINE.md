# ERP API Hub Performance Baseline

This baseline tracks S4-006 performance tests for the .NET 9 ERP API Hub. Treat the numbers below as initial guardrails, not contractual SLOs, until they are replaced by measured CI or staging results.

## Environment Assumptions

- Runtime: .NET 9, Release configuration, server GC enabled by default.
- Host: developer workstation or CI runner with at least 4 physical cores, 16 GB RAM, SSD storage.
- Dependencies for load tests: Redis and RabbitMQ reachable by the API; ERPNext calls should target a stable test ERPNext instance or a controlled stub.
- API URL for k6: `http://localhost:8008`.
- Authentication: Bearer token passed through `AUTH_TOKEN`.
- Benchmarks isolate dependencies with mocks and in-memory stores; load tests exercise the running API and its configured backing services.

## BenchmarkDotNet Baseline

Run from the repository root:

```bash
dotnet run -c Release --project tests/ERPApiHub.Benchmarks
```

Run a subset:

```bash
dotnet run -c Release --project tests/ERPApiHub.Benchmarks -- --filter '*CacheBenchmarks*'
dotnet run -c Release --project tests/ERPApiHub.Benchmarks -- --filter '*QueryBenchmarks*'
dotnet run -c Release --project tests/ERPApiHub.Benchmarks -- --filter '*IngestionBenchmarks*'
```

Expected local baseline:

| Benchmark | Scenario | Expected p95/mean guardrail | Allocation guardrail |
| --- | --- | ---: | ---: |
| `CacheBenchmarks.CacheHitL1` | MemoryCache hit | < 1 ms mean | < 10 KB/op |
| `CacheBenchmarks.CacheMissL2Hit` | L1 miss, Redis/L2 hit, L1 refill | < 5 ms mean | < 30 KB/op |
| `CacheBenchmarks.CacheStampedeConcurrentRequests` | 100 concurrent requests for one missing key | < 25 ms mean, factory called once by design | < 500 KB/op |
| `QueryBenchmarks.ListAsyncWithCache` | `QueryService.ListAsync` returns cached page | < 2 ms mean | < 25 KB/op |
| `QueryBenchmarks.ListAsyncWithoutCache` | `QueryService.ListAsync` calls mocked ERPNext and writes cache | < 10 ms mean | < 80 KB/op |
| `IngestionBenchmarks.SingleIngestion` | One `Customer` ingestion event | < 5 ms mean | < 60 KB/op |
| `IngestionBenchmarks.BatchIngestion` | Batch of 25 `Customer` ingestion events | < 20 ms mean | < 250 KB/op |

BenchmarkDotNet reports are written under `tests/ERPApiHub.Benchmarks/BenchmarkDotNet.Artifacts/`.

## k6 Load Tests

Install k6 locally, start the API on port `8008`, and export a valid token:

```bash
export BASE_URL=http://localhost:8008
export AUTH_TOKEN='<bearer-token>'
```

Run scripts from the repository root:

```bash
k6 run tests/ERPApiHub.LoadTests/k6/cache-hit.js
k6 run tests/ERPApiHub.LoadTests/k6/cache-miss.js
k6 run tests/ERPApiHub.LoadTests/k6/ingestion.js
k6 run tests/ERPApiHub.LoadTests/k6/streaming.js
```

Override load shape when needed:

```bash
VUS=50 DURATION=5m k6 run tests/ERPApiHub.LoadTests/k6/cache-hit.js
RATE=100 VUS=100 DURATION=5m k6 run tests/ERPApiHub.LoadTests/k6/ingestion.js
```

Expected load-test guardrails:

| Script | Target | Expected throughput | Latency threshold |
| --- | --- | ---: | ---: |
| `cache-hit.js` | Warmed `GET /api/v1/query/Customer` | 25 VUs sustained | p95 < 75 ms |
| `cache-miss.js` | `GET /api/v1/query/Customer` with `Cache-Control: no-cache` and unique filters | 50 RPS | p95 < 250 ms |
| `ingestion.js` | Single and batch ingestion endpoints | 30 iterations/s | single p95 < 200 ms, batch p95 < 500 ms |
| `streaming.js` | `GET /api/v1/query/Customer/stream` | 10 VUs sustained | p95 < 1000 ms |

## Notes

- k6 scripts use `http.batch()` where concurrent request groups better represent cache, ingestion, or stream pressure.
- Cache miss load testing intentionally sends `Cache-Control: no-cache` and unique filters to avoid measuring warmed responses.
- If a guardrail fails, capture the BenchmarkDotNet markdown summary or k6 end-of-test summary with host specs, dependency versions, and API configuration before comparing runs.
