# ERP API Hub

ERP API Hub is a .NET 9 integration service for ERPNext-facing ingestion, query, webhook, audit, compliance, and operational APIs.

## Sprint 7 Completion: Webhooks, Rate Limiting & Security

Sprint 7 is complete: **Webhook Infrastructure, Atomic Rate Limiting, and Token Security** delivered across **5 stories (42 story points)**.

### S7 Features

| Story | Feature | Summary |
|-------|---------|---------|
| S7-001 | Webhook Dispatcher + Management API | HMAC signature, SSRF protection, delivery dedup (Redis TTL 24h), DLQ, retry policy (5s → 15min → 30min). Management endpoints for CRUD webhook subscriptions. |
| S7-002 | Frappe Event Ingestion | Secure `/internal/v1/events/ingest` with HMAC validation, timestamp anti-replay, IP restriction. Mock ERPNext Event Generator for testing. |
| S7-003 | Atomic Rate Limiting | Redis Lua sliding window (ZADD + ZREMRANGEBYSCORE + ZCARD), EVALSHA with NOSCRIPT fallback, per-tier limits (TIER_1 10K/min, TIER_2 1K/min, TIER_3 100/min), per-endpoint percentages, Kong config generator. |
| S7-004 | Token Security Fix | Unified error message for all token failures (anti-timing attack), PlainToken cleanup from entity + Redis, constant-time comparison via BCrypt. |
| S7-005 | Query Cache Invalidation | Cache stampede prevention (KeyedSemaphore), tag-based cache invalidation, background worker consuming webhook events, Redis failure fail-open. |

### S7 Security Fixes

- **S7-001**: SSRF protection — blocks private IPs, requires HTTPS, validates redirects
- **S7-001**: Delivery dedup prevents duplicate webhook delivery for same event
- **S7-002**: HMAC + timestamp prevents replay attacks on event ingestion
- **S7-003**: Redis Lua scripts ensure atomic rate limit counters (no race conditions)
- **S7-004**: Anti-timing attack — all token validation failures return same error message
- **S7-004**: O(N) token scan eliminated — SHA-256 Redis hash enables O(1) lookup

## Sprint 6 Completion: Operations & Compliance

Sprint 6 is complete: **Operations & Compliance** delivered across 6 stories. The release includes background jobs, audit search/export, token lifecycle, and field validation with security hardening.

### S6 Features

| Story | Feature | Summary |
|-------|---------|---------|
| S6-001 | Polling Fallback Worker | Background polling with internal pagination + guard cursor to prevent data loss when >100 records share timestamp. |
| S6-002 | PDPA REST Endpoints | Durable DB persistence for consent and erasure requests with composite keys and real timestamps. |
| S6-003 | Token Lifecycle Management | BCrypt hashing for tokens, plaintext token removal from cache, secure token rotation and revocation. |
| S6-004 | Tenant Health Check Background Job | Parallel health checks with `MaxDegreeOfParallelism=5`, alert deduplication, correlation IDs, and safe error messages. |
| S6-005 | Audit Search & Export API | CSV export with escaping (prevent injection), export limits (`MaxExportRecords=100k`), and thread-safe stats collection. |
| S6-006 | Link-Field Validation | Doctype whitelist regex (`^[A-Za-z][A-Za-z0-9_]*$`), defense-in-depth checks, and generic error messages (prevent info leak). |

### Sprint 6 Security Fixes

- **S6-003**: Replaced SHA256 with BCrypt for token hashing (prevents brute force)
- **S6-005**: Fixed CSV injection vulnerability in audit export (escapes `=`, `+`, `-`, `@`, tabs, newlines)
- **S6-006**: Added doctype whitelist validation to prevent path traversal / injection
- **S6-004**: Added correlation IDs to health alerts for traceability; safe error messages (no tenant info leak)

## Sprint 5 Completion: Production Hardening & Compliance

Sprint 5 is complete: **Production Hardening & Compliance** delivered **52 story points across 7 stories**. The release is cross-referenced in [PR #2](https://github.com/anhquankcn/hnh-erp-api-hub/pull/2).

### S5 Features

| Story | Feature | Summary |
|-------|---------|---------|
| S5-001 | Invoice Deletion Block | Blocks issued Sales Invoice deletion, uses fail-closed guard behavior, and records audit outcomes. |
| S5-002 | PDPA Compliance | Adds consent, export, erasure request, and compliance service foundations for data subject rights. |
| S5-003 | Audit Retention | Adds audit retention and archival support with persisted hash-chain metadata and two-phase archive handling. |
| S5-004 | Health Check Probes | Adds liveness, readiness, startup, and aggregate health probes for production orchestration. |
| S5-005 | API Versioning | Adds `/api/v1`, `/api/v2`, `/versions`, and deprecation metadata for v1 responses. |
| S5-006 | Deployment Automation | Adds Docker build/runtime support, compose dependencies, and CI/CD deployment workflow support. |
| S5-007 | Monitoring & Alerting | Adds Prometheus scraping at `/metrics` and Grafana dashboard configuration. |

## Docker Setup

Copy the sample environment file and adjust secrets before running locally:

```sh
cp .env.example .env
docker compose up --build
```

The API container listens on `API_PORT`, defaulting to `8008`. With the default `.env.example` shape, the service is available at:

- API root: `http://localhost:8008/`
- Health: `http://localhost:8008/health`
- Readiness: `http://localhost:8008/health/ready`
- Metrics: `http://localhost:8008/metrics`

The compose stack includes the API, PostgreSQL, Redis, and RabbitMQ. See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for production-oriented deployment notes.

## Monitoring Stack

Prometheus is configured in [infrastructure/monitoring/prometheus.yml](infrastructure/monitoring/prometheus.yml) to scrape `/metrics` from `erp-api-hub:8008`. Grafana dashboard JSON is stored at [infrastructure/monitoring/grafana-dashboard.json](infrastructure/monitoring/grafana-dashboard.json).

When Prometheus and Grafana are run on the same Docker network as the API, make sure the scrape target in `prometheus.yml` resolves in that network. The checked-in target is `erp-api-hub:8008`; with the local `docker-compose.yml` service name, use `api:8008` or provide an `erp-api-hub` network alias.

```sh
docker run --rm --network sprint-5-a_default \
  -p 9090:9090 \
  -v "$PWD/infrastructure/monitoring/prometheus.yml:/etc/prometheus/prometheus.yml:ro" \
  prom/prometheus

docker run --rm --network sprint-5-a_default \
  -p 3000:3000 \
  grafana/grafana
```

Import `infrastructure/monitoring/grafana-dashboard.json` into Grafana and configure a Prometheus data source pointing at `http://prometheus:9090` or the reachable Prometheus URL for your environment.

## API Versioning

The API exposes versioned routes under `/api/v1` and `/api/v2`.

- `/versions` reports supported versions, deprecated versions, and the current v1 sunset date.
- `/api/v1/health` returns the v1 health shape and is marked deprecated.
- `/api/v2/health` returns enhanced health metadata.
- `/api/v2/health/detailed` returns dependency-level health details.
- Successful `/api/v1` responses include `Sunset: Sat, 31 Dec 2026 00:00:00 GMT` and `Deprecation: true`.

Use `/api/v2` for new health integrations and keep existing `/api/v1` clients on a migration plan before the sunset date.

## Sprint History

| Sprint | Theme | Stories | Status |
|--------|-------|---------|--------|
| Sprint 6 | Operations & Compliance | 6 | ✅ Complete |
| Sprint 5 | Production Hardening | 7 | ✅ Complete |
| Sprint 4 | Caching & Performance | - | ✅ Complete |
| Sprint 3 | Core API & Integration | - | ✅ Complete |
| Sprint 2 | Authentication & Security | - | ✅ Complete |
| Sprint 1 | Bootstrap & Foundation | - | ✅ Complete |
