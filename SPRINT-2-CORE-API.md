# Sprint Planning Document
# ERP API Hub - Phase 2: Core API

## Sprint Info

| Field | Value |
|-------|-------|
| Sprint name | Sprint 2 - Core API |
| Duration | 2 weeks |
| Dates | 2026-05-27 to 2026-06-09 |
| Product Owner | IT Manager / HNH Travel |
| Scrum Master / Tech Lead | Tech Lead ERP API Hub |
| Main references | `BRD-ERP-API-HUB.md`, `FRD-ERP-API-HUB.md`, `TDD-ERP-API-HUB.md`, `CLAUDE.md` |

### Sprint Goal

Triển khai 2 luồng API cốt lõi — **Ingestion (Push)** và **Query (Pull)** — hoàn chỉnh với validation, idempotency, caching, audit logging, và ERPNext integration. Cuối sprint, external systems có thể push data vào ERPNext qua API Hub và query data ra với caching + pagination. Webhook outbound foundation và admin audit endpoints cũng sẵn sàng.

### Sprint 1 Recap

| Story | Status |
|-------|--------|
| S1-001 Scaffold | ✅ Done |
| S1-002 PostgreSQL | ✅ Done |
| S1-003 Redis | ✅ Done |
| S1-004 Keycloak JWT | ✅ Done |
| S1-005 Kong Config | ✅ Done |
| S1-006 YARP Gateway | ✅ Done |
| S1-007 Ingestion API | ⏭️ Moved to Sprint 2 |
| S1-008 Query API | ⏭️ Moved to Sprint 2 |
| S1-009 RabbitMQ | ✅ Done |
| S1-010 Health Checks | ✅ Done |

### Current Codebase State

- **API Program.cs**: Minimal endpoints — only root + health. No business endpoints yet.
- **IErpNextClient**: Interface defined with `GetAsync<T>` and `PostAsync<T>`, but only `NoOpErpNextClient` implementation.
- **ErpIngestionConsumer**: Worker that consumes from RabbitMQ, but calls `NoOpErpNextClient`.
- **RedisCacheService**: `GetOrCreateAsync` with stampede protection. No business usage yet.
- **ErpHubDbContext**: All 8 entities + migration. No Application layer logic.

---

## Sprint Backlog

### S2-001 - Ingestion API Endpoint

| Field | Value |
|-------|-------|
| Title | Implement POST /api/v1/ingest/{doctype} with validation, idempotency, audit, and queue publish |
| Story points | 8 |
| Priority | P0 |
| Dependencies | S1-002, S1-003, S1-004 (all done) |
| FRD Ref | FR-ING-002, FR-ING-003, FR-ING-005, FR-ING-009, FR-AUD-001 |

**Acceptance criteria**

```gherkin
Feature: Single document ingestion

  Scenario: Accept valid ingestion request
    Given the client has api-hub:write permission
    And the doctype is in the allowed list
    And the payload passes schema validation
    When the client posts to /api/v1/ingest/{doctype}
    Then the API returns 202 Accepted
    And the response includes a ULID job_id
    And a message is published to RabbitMQ exchange 1stopshop_event_bus
      with routing key erphub.ingestion.{doctype}.created
    And an audit log is recorded

  Scenario: Return cached response for duplicate idempotency key
    Given a successful ingestion request used X-Idempotency-Key
    When the same request is sent again within the idempotency TTL
    Then the API returns the original 202 response
    And no duplicate RabbitMQ message is published

  Scenario: Reject invalid ingestion request
    Given the doctype is not in the allowed list or required fields are missing
    When the client posts to /api/v1/ingest/{doctype}
    Then the API returns 400 Bad Request or 422 Unprocessable Entity
    And an audit log captures the failed result

  Scenario: Reject unauthenticated request
    Given the client has no valid JWT
    When the client posts to /api/v1/ingest/{doctype}
    Then the API returns 401 Unauthorized
```

**Implementation notes:**
- Create `IngestionService` in Application layer (CQRS command handler pattern)
- Create `IngestionRequest` / `IngestionResponse` DTOs
- Validate doctype against allowed list (configurable in appsettings)
- Use Redis for idempotency: key = `erphub:idempotency:{tenant}:{key}`, TTL = 5 min
- Publish `ErpEventEnvelope` to RabbitMQ via existing exchange
- Audit: write to `audit_logs` table via `ErpHubDbContext`
- Map endpoint in API `Program.cs` with `RequireAuthorization("api-hub:write")`

---

### S2-002 - Ingestion Update & Delete Endpoints

| Field | Value |
|-------|-------|
| Title | Implement PUT/DELETE /api/v1/ingest/{doctype}/{name} with ERPNext CRUD mapping |
| Story points | 5 |
| Priority | P1 |
| Dependencies | S2-001 |
| FRD Ref | FR-ING-004, FR-ING-004b |

**Acceptance criteria**

```gherkin
Feature: Ingestion update and delete

  Scenario: Update existing document
    Given the client has api-hub:write permission
    And the document exists in ERPNext
    When the client puts to /api/v1/ingest/{doctype}/{name}
    Then the API returns 202 Accepted
    And a message is published with routing key erphub.ingestion.{doctype}.updated

  Scenario: Delete document (soft)
    Given the client has api-hub:write permission
    When the client deletes /api/v1/ingest/{doctype}/{name}
    Then the API returns 202 Accepted
    And a message is published with routing key erphub.ingestion.{doctype}.deleted

  Scenario: UPSERT race condition prevention
    Given two concurrent requests for the same doctype+name
    When both attempt to create
    Then only one succeeds; the other returns 409 Conflict or 409+retry
    (See FR-ING-004b: distributed lock via Redis)
```

---

### S2-003 - Ingestion Job Status & DLQ Admin

| Field | Value |
|-------|-------|
| Title | Implement GET /api/v1/ingest/status/{job_id} and DLQ admin endpoints |
| Story points | 5 |
| Priority | P1 |
| Dependencies | S2-001 |
| FRD Ref | FR-ING-007 |

**Acceptance criteria**

```gherkin
Feature: Ingestion status tracking

  Scenario: Check job status
    Given a valid job_id
    When the client calls GET /api/v1/ingest/status/{job_id}
    Then the API returns the job status (pending/processing/completed/failed)

  Scenario: List DLQ messages
    Given the client has api-hub:admin permission
    When the client calls GET /api/v1/ingest/dlq
    Then the API returns paginated dead-lettered messages

  Scenario: Replay DLQ message
    Given the client has api-hub:admin permission
    When the client posts to /api/v1/ingest/dlq/{id}/replay
    Then the message is re-published to the ingestion queue
```

---

### S2-004 - Query API Endpoint

| Field | Value |
|-------|-------|
| Title | Implement GET /api/v1/query/{doctype} with pagination, filters, cache, and audit |
| Story points | 8 |
| Priority | P0 |
| Dependencies | S1-002, S1-003, S1-004 (all done) |
| FRD Ref | FR-QRY-001, FR-QRY-002, FR-QRY-003, FR-QRY-004, FR-QRY-005, FR-QRY-006, FR-QRY-007 |

**Acceptance criteria**

```gherkin
Feature: Document query

  Scenario: Query list documents
    Given the client has api-hub:read permission
    And the doctype is allowed
    When the client calls GET /api/v1/query/{doctype} with pagination, filters, fields, orderBy
    Then the API calls ERPNext using the resolved tenant context
    And returns a paginated response with X-Total-Count header
    And an audit log is recorded

  Scenario: Get single document
    Given the client has api-hub:read permission
    When the client calls GET /api/v1/query/{doctype}/{name}
    Then the API returns the ERPNext document

  Scenario: Count documents
    Given the client has api-hub:read permission
    When the client calls GET /api/v1/query/{doctype}/count
    Then the API returns { "count": N }

  Scenario: Redis cache hit
    Given a query response was cached with key erphub:query:{tenant}:{doctype}:{hash}
    When the same query is requested before TTL expiry
    Then the API returns cached response
    And ERPNext is not called

  Scenario: Cache bypass
    Given the client sends Cache-Control: no-cache header
    When the query is requested
    Then the API bypasses cache and calls ERPNext directly

  Scenario: Multi-tenant isolation
    Given the JWT contains BranchId for tenant A
    When the query is executed
    Then X-Frappe-Site-Name header is set to tenant A's ERPNext site
    And only tenant A's data is returned
```

**Implementation notes:**
- Create `QueryService` in Application layer (CQRS query handler pattern)
- Create `QueryRequest` / `QueryResponse<T>` / `PaginatedResponse<T>` DTOs
- Implement real `ErpNextClient` (HTTP) replacing `NoOpErpNextClient`
- Cache key pattern: `erphub:query:{tenant}:{doctype}:{hash_of_params}`
- Default TTL: 5 min (list), 1 min (single doc) — configurable via RedisOptions
- Pagination: `limit_start` / `limit_page_length` with `X-Total-Count`, `X-Page-Count`, `Link` headers
- Filtering: ERPNext filter syntax `[["Doctype", "field", "op", "value"]]`
- Field selection: `fields=["name", "customer_name"]`
- Audit: write to `audit_logs`

---

### S2-005 - Real ERPNext Client Implementation

| Field | Value |
|-------|-------|
| Title | Replace NoOpErpNextClient with real HTTP client calling ERPNext REST API |
| Story points | 5 |
| Priority | P0 |
| Dependencies | S1-002, S1-004 |
| FRD Ref | FR-ING-004, FR-QRY-001 |

**Acceptance criteria**

```gherkin
Feature: ERPNext HTTP client

  Scenario: Authenticate with ERPNext
    Given the tenant has an API key mapping in api_key_mapping table
    When the client makes a request to ERPNext
    Then the request includes the decrypted API key in Authorization header
    And includes X-Frappe-Site-Name header for multi-tenancy

  Scenario: Handle ERPNext errors
    Given ERPNext returns 4xx or 5xx
    When the client receives the error
    Then the error is wrapped in ErpNextResponse with appropriate StatusCode
    And transient errors (5xx, timeout) are retryable

  Scenario: Health check
    Given ERPNext is configured
    When the health endpoint is called
    Then the readiness check reports ERPNext connectivity
```

**Implementation notes:**
- `ErpNextHttpClient` implements `IErpNextClient`
- Uses `IHttpClientFactory` with typed client
- API key decryption using `IDataProtector` (AES-256)
- Resolves tenant site from `TenantRegistry` → `X-Frappe-Site-Name`
- Retry policy: Polly with exponential backoff, 3 retries for transient errors
- Register in DI, replace `NoOpErpNextClient`

---

### S2-006 - Webhook Outbound Foundation

| Field | Value |
|-------|-------|
| Title | Implement webhook subscription management and delivery engine |
| Story points | 8 |
| Priority | P1 |
| Dependencies | S1-002, S1-009 |
| FRD Ref | FR-WHK-001, FR-WHK-002, FR-WHK-003, FR-WHK-004, FR-WHK-005 |

**Acceptance criteria**

```gherkin
Feature: Webhook outbound

  Scenario: Manage subscriptions
    Given the client has api-hub:admin permission
    When CRUD operations are performed on /api/v1/webhooks/subscriptions
    Then subscriptions are persisted in webhook_subscriptions table

  Scenario: Deliver webhook
    Given an ERPNext event matches a subscription's event_types
    When the event is received
    Then the webhook is delivered to the subscription URL
    And the delivery is recorded in webhook_deliveries
    And signature is included (HMAC-SHA256)

  Scenario: Retry failed delivery
    Given a webhook delivery fails (4xx/5xx/timeout)
    When the retry policy processes it
    Then up to 3 retries with exponential backoff
    And final failure is recorded with next_retry_at = null

  Scenario: Verify webhook signature
    Given the subscription has a secret
    When the webhook is delivered
    Then X-ERP-Hub-Signature header contains HMAC-SHA256 hex digest
```

---

### S2-007 - Audit API & PII Masking

| Field | Value |
|-------|-------|
| Title | Implement audit log query endpoint with PII masking |
| Story points | 5 |
| Priority | P1 |
| Dependencies | S1-002, S1-004 |
| FRD Ref | FR-AUD-001, FR-AUD-002, FR-AUD-003, FR-AUD-004 |

**Acceptance criteria**

```gherkin
Feature: Audit API

  Scenario: Query audit logs
    Given the client has api-hub:admin permission
    When the client calls GET /api/v1/audit/logs with filters
    Then the API returns paginated audit logs

  Scenario: PII masking
    Given an audit log contains PII (email, phone, name)
    When the log is returned via API
    Then PII fields are masked (e.g., j***@gmail.com, 0***1234)

  Scenario: Export audit logs
    Given the client has api-hub:admin permission
    When the client calls GET /api/v1/audit/logs/export
    Then the API returns a CSV file download
```

---

### S2-008 - Batch Ingestion

| Field | Value |
|-------|-------|
| Title | Implement POST /api/v1/ingest/batch for bulk document ingestion |
| Story points | 5 |
| Priority | P2 |
| Dependencies | S2-001 |
| FRD Ref | FR-ING-008 |

**Acceptance criteria**

```gherkin
Feature: Batch ingestion

  Scenario: Accept batch request
    Given the client has api-hub:write permission
    And the batch contains up to 100 documents
    When the client posts to /api/v1/ingest/batch
    Then the API returns 202 Accepted with a batch job_id
    And each document is published as a separate RabbitMQ message

  Scenario: Reject oversized batch
    Given the batch contains more than 100 documents
    When the client posts to /api/v1/ingest/batch
    Then the API returns 413 Payload Too Large
```

---

### S2-009 - Integration Tests

| Field | Value |
|-------|-------|
| Title | Integration tests for Ingestion, Query, and Webhook flows |
| Story points | 8 |
| Priority | P0 |
| Dependencies | S2-001, S2-004, S2-005 |

**Acceptance criteria**

```gherkin
Feature: Integration tests

  Scenario: End-to-end ingestion flow
    Given a valid ingestion request
    When the full flow runs (API → RabbitMQ → Worker → ERPNext)
    Then the document exists in ERPNext
    And audit log is recorded
    And idempotency works correctly

  Scenario: End-to-end query flow
    Given data exists in ERPNext
    When a query is made
    Then the response is correct
    And caching behavior is verified

  Scenario: Webhook delivery
    Given a webhook subscription exists
    When an event matches
    Then delivery is recorded with correct signature
```

---

## Sprint Summary

| Story | Title | SP | Priority | Dependencies |
|-------|-------|-----|----------|-------------|
| S2-001 | Ingestion API Endpoint | 8 | P0 | — |
| S2-002 | Ingestion Update/Delete | 5 | P1 | S2-001 |
| S2-003 | Ingestion Job Status & DLQ | 5 | P1 | S2-001 |
| S2-004 | Query API Endpoint | 8 | P0 | — |
| S2-005 | Real ERPNext Client | 5 | P0 | — |
| S2-006 | Webhook Outbound Foundation | 8 | P1 | — |
| S2-007 | Audit API & PII Masking | 5 | P1 | — |
| S2-008 | Batch Ingestion | 5 | P2 | S2-001 |
| S2-009 | Integration Tests | 8 | P0 | S2-001, S2-004, S2-005 |
| **Total** | | **57 SP** | | |

### Priority Order (P0 first)

1. **S2-005** — Real ERPNext Client (foundation cho mọi API)
2. **S2-001** — Ingestion API (P0, parallel với S2-005)
3. **S2-004** — Query API (P0, parallel với S2-001)
4. **S2-009** — Integration Tests (P0, sau khi APIs xong)
5. S2-002 + S2-003 — Ingestion extensions (P1)
6. S2-006 + S2-007 — Webhook + Audit (P1)
7. S2-008 — Batch (P2, nếu còn thời gian)

### Agent Assignment Strategy

| Agent | Parallel Track | Stories |
|-------|---------------|---------|
| Codex | Track A: Ingestion | S2-005 → S2-001 → S2-002 → S2-003 → S2-008 |
| Gemini | Track B: Query + Audit | S2-004 → S2-007 → S2-006 |
| Claude | Track C: Tests | S2-009 (sau khi Track A/B có code) |

2 worktrees, 2 agents chạy song song, Claude review + test sau.

## Definition of Done

- [x] Same as Sprint 1 DoD, plus:
- ERPNext client calls real ERPNext API (not NoOp)
- Idempotency works correctly with Redis
- Cache invalidation on write operations
- Audit logs cover both success and failure paths
- PII masking in audit responses
- Webhook signature verification (HMAC-SHA256)
- All endpoints return proper HTTP status codes
- OpenAPI docs include all new endpoints

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| ERPNext dev endpoint unavailable | S2-005 blocked; Query/Ingestion can't test real calls | Build `ErpNextHttpClient` behind existing `IErpNextClient` interface; keep `NoOpErpNextClient` as fallback for CI; use WireMock for integration tests |
| API key encryption complexity | S2-005 delayed; can't authenticate with ERPNext | Use `IDataProtector` from ASP.NET Core; store encrypted bytes in `api_key_mapping.erpnext_api_key_enc`; decrypt at runtime |
| Idempotency key collision | Duplicate processing; data inconsistency | Use composite key: `erphub:idempotency:{tenant}:{key}`; atomic Redis SETNX with TTL |
| Query cache staleness | Stale data returned; business impact | Default low TTL (1-5 min); support `Cache-Control: no-cache` bypass; invalidate on ingestion events |
| Webhook secret management | Security risk if secrets leaked | Store encrypted (`bytea`); HMAC-SHA256 signing; HTTPS-only delivery |