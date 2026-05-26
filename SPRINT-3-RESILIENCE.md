# Sprint Planning Document
# ERP API Hub - Phase 3: Resilience & Operations

## Sprint Info

| Field | Value |
|-------|-------|
| Sprint name | Sprint 3 - Resilience & Operations |
| Duration | 2 weeks |
| Dates | 2026-05-27 to 2026-06-09 |
| Product Owner | IT Manager / HNH Travel |
| Scrum Master / Tech Lead | Tech Lead ERP API Hub |
| Main references | `BRD-ERP-API-HUB.md`, `FRD-ERP-API-HUB.md`, `TDD-ERP-API-HUB.md`, `CLAUDE.md` |

### Sprint Goal

Triển khai **Resilience & Operations** — rate limiting, external system configuration, ERPNext event ingestion (Server Script + internal endpoint), standardized error responses (RFC 7807), và observability foundation (Prometheus metrics + Serilog structured logging). Cuối sprint, API Hub có đầy đủ operational readiness: external systems tự quản lý registration, rate limit bảo vệ theo tier, ERPNext events chảy vào webhook pipeline, và mọi error response tuân thủ RFC 7807.

### Sprint 2 Recap

| Story | Status | Notes |
|-------|--------|-------|
| S2-001 Ingestion API | ✅ Done | POST/PUT/DELETE + idempotency + RabbitMQ |
| S2-002 Ingestion Update/Delete | ✅ Done | PUT/DELETE with validation |
| S2-003 Ingestion Status & DLQ | ✅ Done | Status lookup; DLQ placeholder |
| S2-004 Query API | ✅ Done | GET list/get/count + cache + pagination |
| S2-005 Real ERPNext Client | ✅ Done | ErpNextHttpClient with Polly, tenant resolution |
| S2-006 Webhook Foundation | ✅ Done | CRUD + delivery + HMAC-SHA256 signing |
| S2-007 Audit API & PII Masking | ✅ Done | Query/export + PII masking |
| S2-008 Batch Ingestion | ✅ Done | Batch up to 100 docs |
| S2-009 Integration Tests | ✅ Done | 7 test files, ~30 methods |

### Current Codebase State

- **42+ .cs files**, ~3000 LOC (excl. migrations)
- **Program.cs**: All Sprint 2 endpoints wired
- **Missing from FRD**: Rate limiting, external system config CRUD, ERPNext event ingestion, RFC 7807 errors, observability, VN compliance validation

---

## Sprint Backlog

### S3-001 - Rate Limiting (Per-Consumer + Per-Endpoint)

| Field | Value |
|-------|-------|
| Title | Implement application-level rate limiting with Redis sliding window, tier-based limits, and standard 429 responses |
| Story points | 8 |
| Priority | P0 |
| Dependencies | S1-003 (Redis, done) |
| FRD Ref | FR-RLM-001, FR-RLM-002, FR-RLM-003, FR-RLM-004, FR-RLM-005 |

**Acceptance criteria**

```gherkin
Feature: Rate limiting

  Scenario: Per-consumer rate limit (TIER_2 Standard)
    Given the external system has rate_limit_tier = "TIER_2"
    When the system exceeds 1000 requests per minute
    Then the API returns 429 Too Many Requests
    And the response includes X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset headers
    And the response includes Retry-After header

  Scenario: Per-endpoint rate limit reduction
    Given the external system has TIER_2 (1000 req/min)
    When the system calls ingestion endpoints (50% of tier = 500/min)
    And exceeds that limit
    Then the API returns 429 for ingestion specifically

  Scenario: Burst handling (token bucket)
    Given the external system has TIER_2
    When a short burst of 2000 requests arrives
    Then the first 2000 are allowed (2x burst)
    And subsequent requests are limited to 1000/min sustained

  Scenario: TIER_1 Premium
    Given the external system has TIER_1
    Then the limit is 10000 req/min with burst of 20000

  Scenario: TIER_3 Basic
    Given the external system has TIER_3
    Then the limit is 100 req/min with burst of 200

  Scenario: Unregistered system defaults to TIER_3
    Given the system_id is not in external_systems table
    Then the default tier is TIER_3 Basic

  Scenario: Rate limit response format (RFC 7807)
    When a 429 response is returned
    Then the body follows RFC 7807 Problem Details format
    And includes retry_after field
```

**Implementation notes:**
- Create `RateLimitService` in Application layer
- Redis sliding window: `erphub:ratelimit:{system_id}:{endpoint_type}:{window}` 
- Token bucket for burst: `erphub:ratelimit:burst:{system_id}`
- Rate limit tiers configured in `appsettings.json` (or DB via `external_systems.rate_limit_tier`)
- Middleware or endpoint filter that checks rate BEFORE handler execution
- Headers: `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset`, `Retry-After`
- Register as global filter on the API pipeline

---

### S3-002 - External System Configuration (CRUD + API Key Management)

| Field | Value |
|-------|-------|
| Title | Implement CRUD endpoints for external system registry and API key lifecycle |
| Story points | 8 |
| Priority | P0 |
| Dependencies | S1-002 (PostgreSQL, done), S2-005 (ERPNext client, done) |
| FRD Ref | FR-CFG-001, FR-AUTH-002 |

**Acceptance criteria**

```gherkin
Feature: External system configuration

  Scenario: Register external system
    Given the client has api-hub:admin permission
    When POST /api/v1/systems with valid payload
    Then the system is created in external_systems table
    And an API key mapping is auto-generated
    And the ERPNext API key/secret are encrypted with DataProtection

  Scenario: List systems
    Given the client has api-hub:admin permission
    When GET /api/v1/systems
    Then returns paginated list of active external systems

  Scenario: Get system details
    Given the client has api-hub:admin permission
    When GET /api/v1/systems/{id}
    Then returns system details (API secrets masked)

  Scenario: Update system
    Given the client has api-hub:admin permission
    When PUT /api/v1/systems/{id} with updated fields
    Then the system is updated
    And updated_at timestamp is set

  Scenario: Deactivate system (soft delete)
    Given the client has api-hub:admin permission
    When DELETE /api/v1/systems/{id}
    Then is_active = false and deleted_at is set
    And associated API key mapping is deactivated

  Scenario: Rotate API key
    Given the client has api-hub:admin permission
    When POST /api/v1/systems/{id}/api-keys/rotate
    Then a new API key/secret pair is generated
    And the old key is deactivated after a grace period

  Scenario: API secrets never returned in full
    Given any API response including system details
    Then API secrets are always masked (***REDACTED***)
```

**Implementation notes:**
- Create `ExternalSystemService` in Application layer
- CRUD for `external_systems` + `api_key_mapping` tables
- API key auto-generation on system registration
- DataProtection encryption for API keys (already in ErpNextHttpClient)
- Key rotation: generate new key, mark old as `is_active = false` after configurable grace period
- Soft delete: `deleted_at` + `is_active = false`
- Max 50 active systems per tenant (configurable)

---

### S3-003 - ERPNext Event Ingestion (Server Script + Internal Endpoint)

| Field | Value |
|-------|-------|
| Title | Implement internal endpoint for ERPNext Server Script events and webhook dispatch pipeline |
| Story points | 8 |
| Priority | P0 |
| Dependencies | S2-006 (Webhook foundation, done), S1-009 (RabbitMQ, done) |
| FRD Ref | FR-WHK-001b, FR-WHK-002, §16.8 |

**Acceptance criteria**

```gherkin
Feature: ERPNext event ingestion

  Scenario: Receive event from ERPNext Server Script
    Given ERPNext sends POST /internal/v1/events/ingest
    With valid HMAC signature from ERPNext
    Then the event is validated and queued for webhook dispatch
    And an audit log is recorded

  Scenario: Reject event without valid signature
    Given ERPNext sends POST /internal/v1/events/ingest
    Without valid HMAC signature
    Then the API returns 401 Unauthorized

  Scenario: Event dispatches to matching subscriptions
    Given a subscription for event_type "customer_created" exists
    When an event with type "customer_created" is received
    Then the webhook delivery service dispatches to the subscription URL
    And delivery is recorded in webhook_deliveries

  Scenario: Multiple subscriptions match
    Given 3 subscriptions match event_type "invoice_paid"
    When the event is received
    Then all 3 webhooks are dispatched
    And each delivery is tracked independently

  Scenario: Standard event envelope
    Given any event from ERPNext
    Then the envelope follows HNH standard: {eventId, eventType, source, correlationId, timestamp, version, payload}
    And eventId is a valid 26-char ULID

  Scenario: Server Script example provided
    Given a Frappe Server Script needs to be created in ERPNext
    Then documentation/example is provided for the Server Script code
    That POSTs to API Hub /internal/v1/events/ingest
```

**Implementation notes:**
- Create `EventIngestionController` or minimal API endpoint: `POST /internal/v1/events/ingest`
- HMAC signature verification between ERPNext and API Hub (shared secret in config)
- Event validation: ULID eventId, required fields, timestamp within tolerance
- After validation → publish to RabbitMQ `1stopshop_event_bus` with routing key `erphub.webhook.{event_type}`
- Create `WebhookDispatcherConsumer` (Worker) that:
  1. Consumes from `erphub.webhook.delivery` queue
  2. Looks up matching subscriptions by event_type
  3. Dispatches to each subscription URL with HMAC signing
  4. Records delivery in `webhook_deliveries`
- Add `ErpNextEventOptions` for shared HMAC secret configuration
- Provide Frappe Server Script example in `/docs/erpnext-server-script-example.py`

---

### S3-004 - RFC 7807 Problem Details (Standardized Error Responses)

| Field | Value |
|-------|-------|
| Title | Implement RFC 7807 Problem Details for all error responses |
| Story points | 5 |
| Priority | P1 |
| Dependencies | — |
| FRD Ref | §12.2, §10.3 |

**Acceptance criteria**

```gherkin
Feature: Standardized error responses

  Scenario: Validation error (400)
    Given a request fails validation
    When the API returns 400 Bad Request
    Then the response body follows RFC 7807 format:
      | Field     | Value                                          |
      | type      | https://api.hnhtravel.work/errors/validation    |
      | title     | Validation Failed                               |
      | status    | 400                                             |
      | detail    | Human-readable message                          |
      | instance  | /api/v1/ingest/customer                         |
      | errors[]  | [{field, message, code}]                        |
      | request_id| ULID correlation ID                             |
      | timestamp | ISO 8601 UTC                                    |

  Scenario: Auth error (401/403)
    Given authentication or authorization fails
    Then RFC 7807 format with appropriate type URI

  Scenario: Not found (404)
    Given a resource doesn't exist
    Then RFC 7807 format with type: https://api.hnhtravel.work/errors/not-found

  Scenario: Conflict (409)
    Given an idempotency key collision
    Then RFC 7807 format with type: https://api.hnhtravel.work/errors/conflict

  Scenario: Rate limited (429)
    Given rate limit exceeded
    Then RFC 7807 format with retry_after field

  Scenario: ERPNext downstream error (502)
    Given ERPNext returns 5xx
    Then RFC 7807 format with type: https://api.hnhtravel.work/errors/erpnext-error

  Scenario: Internal server error (500)
    Given an unexpected error
    Then RFC 7807 format with type: https://api.hnhtravel.work/errors/internal-error
    And no stack trace or internal details exposed in production
```

**Implementation notes:**
- Create `ProblemDetailsMiddleware` or use ASP.NET Core built-in `ProblemDetails` service
- Configure `Microsoft.AspNetCore.Http.Extensions.ProblemDetails` in Program.cs
- Custom `ProblemDetails` subclass with `request_id`, `timestamp`, `errors[]` extensions
- Global exception handler that maps exceptions → ProblemDetails
- Error type URI pattern: `https://api.hnhtravel.work/errors/{category}`
- In Development: include stack trace; in Production: hide internal details
- Update ALL existing endpoints to return ProblemDetails on error

---

### S3-005 - Observability Foundation (Metrics + Structured Logging)

| Field | Value |
|-------|-------|
| Title | Implement Prometheus metrics and Serilog structured logging |
| Story points | 8 |
| Priority | P1 |
| Dependencies | — |
| FRD Ref | §15.4 (Observability), §16.9 |

**Acceptance criteria**

```gherkin
Feature: Observability

  Scenario: Prometheus metrics endpoint
    Given the API Hub is running
    When GET /metrics is called
    Then Prometheus-formatted metrics are returned
    And includes:
      | Metric                                  | Type      | Labels                          |
      | api_hub_requests_total                  | Counter   | endpoint, method, status        |
      | api_hub_request_duration_seconds        | Histogram | endpoint                        |
      | api_hub_cache_hit_ratio                 | Gauge     |                                 |
      | api_hub_erpnext_errors_total            | Counter   | error_type                      |
      | api_hub_queue_depth                     | Gauge     | queue_name                      |
      | api_hub_webhook_deliveries_total        | Counter   | status                          |
      | api_hub_rate_limit_rejections_total      | Counter   | system_id, tier                  |

  Scenario: Structured JSON logging
    Given the API Hub is running
    When any log entry is written
    Then the log is in structured JSON format via Serilog
    And includes: timestamp, level, message, correlation_id, tenant_id, request_id

  Scenario: Correlation ID propagation
    Given a request arrives with X-Request-ID header
    When the request is processed
    Then the correlation ID is included in all log entries
    And propagated to downstream ERPNext calls

  Scenario: Request duration tracking
    Given any API request
    When the request completes
    Then the duration is recorded in api_hub_request_duration_seconds histogram
    And logged in the audit trail
```

**Implementation notes:**
- Add NuGet: `prometheus-net`, `prometheus-net.AspNetCore`, `Serilog`, `Serilog.AspNetCore`, `Serilog.Sinks.Console`, `Serilog.Formatting.Compact`
- Create `PrometheusMetricsService` (singleton) wrapping Prometheus metrics instances
- Middleware: `PrometheusMiddleware` to record request duration + count
- Serilog: configure in `Program.cs` with compact JSON formatter
- Correlation ID middleware: read `X-Request-ID` (or generate ULID), set in `ILogger` scope
- Expose `GET /metrics` endpoint (no auth for Prometheus scrape)
- Add to docker-compose Prometheus scrape config

---

### S3-006 - Vietnam Compliance Validation (Tax ID + Invoice)

| Field | Value |
|-------|-------|
| Title | Implement Vietnamese Tax ID validation and invoice compliance rules during ingestion |
| Story points | 5 |
| Priority | P1 |
| Dependencies | S2-001 (Ingestion API, done) |
| FRD Ref | FR-VN-001, FR-VN-002, FR-VN-003 |

**Acceptance criteria**

```gherkin
Feature: Vietnam compliance validation

  Scenario: Valid 10-digit MST
    Given a Customer ingestion with tax_id = "0101234567" (10 digits)
    And country = "Vietnam"
    When the ingestion is processed
    Then validation passes
    And the document is queued for ERPNext

  Scenario: Valid 13-digit MST (chi nhánh)
    Given a Customer ingestion with tax_id = "0101234567-001" (13 chars with dash)
    And country = "Vietnam"
    Then validation passes

  Scenario: Invalid MST format
    Given a Customer ingestion with tax_id = "0123X" (invalid format)
    And country = "Vietnam"
    When the ingestion is processed
    Then validation fails with 400 Bad Request
    And error indicates "Invalid Tax ID format"

  Scenario: MST starting with 0
    Given a Customer ingestion with tax_id = "0012345678" (starts with 00)
    Then validation fails

  Scenario: Foreign customer MST bypass
    Given a Customer ingestion with country = "Singapore" (not Vietnam)
    And tax_id is missing or any format
    Then MST validation is skipped

  Scenario: Invoice deletion blocked
    Given a Sales Invoice with name "SINV-2026-00001" exists
    When DELETE /api/v1/ingest/Sales Invoice/SINV-2026-00001 is called
    Then the API returns 403 Forbidden
    And error message: "Invoice deletion not permitted per Thong tu 78/2021/TT-BTC"
    And an audit log is recorded

  Scenario: Currency default VND
    Given an ingestion for Sales Order without currency field
    When processed
    Then currency defaults to "VND"
    And amount has no decimal places (VND is integer currency)

  Scenario: Exchange rate recording
    Given a Sales Order with currency = "USD"
    When processed
    Then the system requires exchange_rate field
    And records the rate and date applied
```

**Implementation notes:**
- Create `VietnamComplianceService` in Application layer
- Tax ID validation: regex `^\d{10}$` or `^\d{10}-\d{3}$`, no leading zero for first digit
- Invoice deletion guard: check doctype against protected doctypes list (`Sales Invoice`, `Purchase Invoice`, etc.)
- VND currency defaulting: middleware or IngestionService enrichment
- Protected doctypes configurable in `appsettings.json`
- Integration with `AllowedDoctypeValidator` — add compliance rules layer
- Audit log for blocked operations

---

### S3-007 - Integration Tests for Sprint 3

| Field | Value |
|-------|-------|
| Title | Integration tests for rate limiting, external system config, event ingestion, error responses |
| Story points | 5 |
| Priority | P1 |
| Dependencies | S3-001, S3-002, S3-003, S3-004 |

**Acceptance criteria**

```gherkin
Feature: Sprint 3 integration tests

  Scenario: Rate limiting tests
    Given a TIER_2 system
    When exceeding rate limit
    Then 429 response with proper headers and RFC 7807 body

  Scenario: External system CRUD tests
    Given admin permission
    When CRUD operations on /api/v1/systems
    Then correct status codes and data persistence

  Scenario: Event ingestion tests
    Given a valid ERPNext event with HMAC signature
    When POST /internal/v1/events/ingest
    Then event is queued and webhooks dispatched

  Scenario: Problem Details tests
    Given various error scenarios
    When errors occur
    Then all responses follow RFC 7807 format

  Scenario: VN compliance tests
    Given various tax_id formats
    When ingesting with country = Vietnam
    Then valid MST passes, invalid MST fails
```

---

## Sprint Summary

| Story | Title | SP | Priority | Dependencies |
|-------|-------|-----|----------|-------------|
| S3-001 | Rate Limiting (Per-Consumer + Per-Endpoint) | 8 | P0 | S1-003 |
| S3-002 | External System Configuration (CRUD + API Keys) | 8 | P0 | S1-002, S2-005 |
| S3-003 | ERPNext Event Ingestion (Server Script + Dispatch) | 8 | P0 | S2-006, S1-009 |
| S3-004 | RFC 7807 Problem Details | 5 | P1 | — |
| S3-005 | Observability Foundation (Metrics + Logging) | 8 | P1 | — |
| S3-006 | Vietnam Compliance Validation | 5 | P1 | S2-001 |
| S3-007 | Integration Tests | 5 | P1 | S3-001~004 |
| **Total** | | **47 SP** | | |

### Priority Order (P0 first)

1. **S3-001** — Rate Limiting (protect system from abuse)
2. **S3-002** — External System Config (admin can register systems, API key lifecycle)
3. **S3-003** — ERPNext Event Ingestion (webhook pipeline becomes real, not just foundation)
4. **S3-004** — RFC 7807 Problem Details (standardize all error responses)
5. S3-005 — Observability (metrics + logging)
6. S3-006 — VN Compliance (Tax ID + Invoice rules)
7. S3-007 — Integration Tests

### Agent Assignment Strategy

| Agent | Track | Stories |
|-------|-------|---------|
| Codex | Track A: Resilience | S3-001 → S3-004 |
| Claude | Track B: Operations | S3-002 → S3-003 → S3-006 |
| Gemini | Cross-review | Review Track A + Track B after merge |
| (Lam direct) | Tests | S3-005 → S3-007 |

2 worktrees, 2 agents chạy song song, Gemini cross-review sau merge.

## Definition of Done

- Same as Sprint 1+2 DoD, plus:
- Rate limiting works with Redis sliding window across all tiers
- External system CRUD with encrypted API key lifecycle
- ERPNext events flow from Server Script → API Hub → webhook dispatch
- ALL error responses follow RFC 7807 Problem Details
- Prometheus `/metrics` endpoint returns standard metrics
- Serilog structured JSON logging with correlation ID
- Vietnam Tax ID validation enforced for Vietnamese customers
- Invoice deletion blocked per Thông tư 78/2021
- Integration tests cover all Sprint 3 stories

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Redis sliding window implementation complexity | S3-001 delayed | Use proven algorithm (fixed window + counter is simpler, upgrade to sliding later); existing RedisCacheService can be extended |
| API key rotation grace period race condition | S3-002 security risk | Atomic key swap with Redis lock; test thoroughly |
| ERPNext Server Script requires ERPNext access | S3-003 can't test real events | Provide example script; test with mock HTTP calls to /internal/v1/events/ingest |
| Problem Details middleware conflicts with existing error returns | S3-004 breaks existing endpoints | Incremental: add middleware, then update each endpoint to throw instead of return error manually |
| Prometheus metrics cardinality explosion | S3-005 memory pressure | Limit label cardinality; don't label with request_id; use histograms with reasonable buckets |
| VN Tax ID validation too strict | S3-006 blocks legitimate data | Allow bypass for foreign customers; configurable validation strictness |