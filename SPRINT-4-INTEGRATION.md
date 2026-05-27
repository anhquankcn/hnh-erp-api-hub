# Sprint Planning Document
# ERP API Hub - Phase 4: Integration & Production Readiness

## Sprint Info

| Field | Value |
|-------|-------|
| Sprint name | Sprint 4 - Integration & Production Readiness |
| Duration | 2 weeks |
| Dates | 2026-05-28 to 2026-06-10 |
| Product Owner | IT Manager / HNH Travel |
| Scrum Master / Tech Lead | Tech Lead ERP API Hub |
| Main references | `FRD-ERP-API-HUB.md`, `CLAUDE.md` |

### Sprint Goal

Triển khai **Integration & Production Readiness** — Tenant Registry với health check, Data Transformation & Mapping engine, UPSERT race condition prevention, Token lifecycle (refresh/revoke), DLQ management API, Kong/YARP config hoàn chỉnh, và Frappe Server Script example. Cuối sprint, API Hub sẵn sàng production: multi-tenant routing hoạt động, data mapping linh hoạt, UPSERT an toàn, và Kong gateway được cấu hình đúng.

### Sprint 3 Recap

| Story | Status | Notes |
|-------|--------|-------|
| S3-001 Rate Limiting | ✅ Done | Redis fixed-window + burst, 429 RFC 7807 |
| S3-002 External System Config | ✅ Done | CRUD + DataProtection + key rotation |
| S3-003 ERPNext Event Ingestion | ✅ Done | HMAC signature + RabbitMQ dispatch |
| S3-004 RFC 7807 Problem Details | ✅ Done | Global exception handler + helper factory |
| S3-005 Observability Foundation | ✅ Done | OpenTelemetry traces/metrics + request logging |
| S3-006 Vietnam Compliance | ✅ Done | MST validation + e-invoice + PDPA masking |
| S3-007 Integration Tests | ✅ Done | Rate limit, ProblemDetails, compliance tests |
| Review fixes (3 rounds) | ✅ Done | Atomic Redis, long-lived MQ, custom exceptions |

### Current Codebase State

- **65+ .cs source files**, **12 test .cs files**
- All Sprint 1-3 endpoints wired in Program.cs
- Main branch at `3fe779b`

---

## Gap Analysis: FRD vs Implementation

### Already Implemented (Sprint 1-3)
- ✅ JWT auth via Keycloak (S1)
- ✅ RBAC policy handlers (S1)
- ✅ ERPNext Client with Polly retry (S2)
- ✅ Ingestion CRUD + batch + status (S2)
- ✅ Query API with Redis caching (S2)
- ✅ Webhook CRUD + delivery + HMAC signing (S2)
- ✅ Audit API + PII masking (S2)
- ✅ Rate Limiting with Redis (S3)
- ✅ External System Config (S3)
- ✅ ERPNext Event Ingestion + dispatch (S3)
- ✅ RFC 7807 Problem Details (S3)
- ✅ Observability (OpenTelemetry) (S3)
- ✅ VN Compliance validation (S3)
- ✅ Integration Tests (S3)

### Not Yet Implemented (FRD Gaps)

| Gap | FRD Ref | Priority | Complexity |
|-----|---------|----------|-------------|
| Tenant Registry service + health check | FR-AUTH-004b | P0 | Medium |
| Token refresh/revoke endpoints | FR-AUTH-005 | P0 | Medium |
| Data Transformation & Mapping engine | FR-ING-003, FR-CFG-002/003 | P1 | High |
| UPSERT race condition (RedLock) | FR-ING-004b | P1 | Medium |
| DLQ management API (list/replay/purge) | FR-ING-007 | P1 | Low |
| Webhook subscription limit (10/system) | FR-WHK-001 | P2 | Low |
| Frappe Server Script example | FR-WHK-001b | P2 | Low |
| Invoice deletion block | FR-ING-010 (partial) | P2 | Low |
| Kong config (JWT, rate limiting, ACL) | FR-RLM-005, §16.4 | P1 | Medium |
| PDPA consent/export/delete endpoints | FR-VN-004 | P2 | Medium |
| Audit log retention/archive | FR-AUD-003 | P2 | Medium |
| Async audit export with download link | FR-AUD-005 | P2 | Medium |
| Transformation Rules (sandboxed scripts) | FR-CFG-004 | P3 | High |

---

## Sprint Backlog

### S4-001 - Tenant Registry Service (Multi-Tenant Routing)

| Field | Value |
|-------|-------|
| Title | Implement TenantRegistry service with health checks, Redis caching, and dynamic ERPNext host resolution |
| Story points | 8 |
| Priority | P0 |
| Dependencies | S1-002 (PostgreSQL), S1-003 (Redis) |
| FRD Ref | FR-AUTH-004b, FR-QRY-007 |

**Acceptance criteria**

```gherkin
Feature: Tenant Registry

  Scenario: Resolve tenant from JWT BranchId claim
    Given a request with JWT containing BranchId = "frontend"
    When the TenantRegistry resolves the tenant
    Then it returns erpnext_host = "erpnext-frontend:8080"
    And site_name = "frontend"
    And the result is cached in Redis for 60 seconds

  Scenario: Reject request for inactive tenant
    Given a tenant with is_active = false
    When the TenantRegistry resolves the tenant
    Then the API returns 403 Forbidden with ProblemDetails

  Scenario: Health check background job
    Given the TenantRegistry is running
    Then every 30 seconds it pings GET /api/method/health for each active tenant
    And updates health_status (healthy/degraded/down) in tenant_registry table

  Scenario: Tenant health degraded warning
    Given a tenant with health_status = "degraded"
    When a request targets that tenant
    Then the request proceeds but a warning header is added: X-Tenant-Health: degraded

  Scenario: Tenant health down rejection
    Given a tenant with health_status = "down"
    When a request targets that tenant
    Then the API returns 503 Service Unavailable with ProblemDetails type "tenant-unavailable"

  Scenario: X-Frappe-Site-Name header propagation
    Given any ERPNext API call
    Then the TenantRegistry ensures X-Frappe-Site-Name header is set
    And requests are routed to the correct erpnext_host

  Scenario: Cache miss falls through to DB
    Given Redis is unavailable or cache miss
    Then the TenantRegistry queries PostgreSQL directly
    And logs a warning about cache miss
```

**Implementation notes:**
- `TenantRegistryService` in Application layer
- Entity `TenantRegistry` already exists in Domain
- Redis cache: `erphub:tenant:{tenant_id}` TTL 60s
- Background health check: `BackgroundService` pings `/api/method/health` per tenant every 30s
- Update `ErpNextHttpClient` to use TenantRegistry for host resolution
- BranchId from JWT claim (never trust client header — existing policy)
- Fallback: environment variable mapping if DB unreachable

---

### S4-002 - Token Lifecycle (Refresh & Revoke)

| Field | Value |
|-------|-------|
| Title | Implement /auth/refresh and /auth/revoke endpoints with Keycloak integration |
| Story points | 5 |
| Priority | P0 |
| Dependencies | S1-002 (Keycloak auth) |
| FRD Ref | FR-AUTH-005 |

**Acceptance criteria**

```gherkin
Feature: Token lifecycle

  Scenario: Refresh access token
    Given a valid refresh token
    When POST /api/v1/auth/refresh with the refresh token
    Then the API exchanges it with Keycloak
    And returns new access_token + refresh_token pair
    And the old refresh token is invalidated

  Scenario: Revoke session
    Given a valid access token
    When POST /api/v1/auth/revoke
    Then the API calls Keycloak logout endpoint
    And the session is invalidated
    And the access token is blacklisted in Redis (TTL = remaining token lifetime)

  Scenario: Verify token validity
    Given a valid access token
    When GET /api/v1/auth/verify
    Then returns 200 with token claims (sub, roles, tenant, expiry)

  Scenario: Refresh with expired/invalid token
    Given an expired or invalid refresh token
    When POST /api/v1/auth/refresh
    Then returns 401 Unauthorized with ProblemDetails

  Scenario: Revoke already-revoked token
    Given a token that has been revoked
    When POST /api/v1/auth/revoke
    Then returns 200 (idempotent)

  Scenario: Token exchange endpoint
    Given a valid Keycloak JWT
    When POST /api/v1/auth/token
    Then the API validates the JWT
    And returns an API Hub session token (if needed) or confirms the Keycloak token is valid
```

**Implementation notes:**
- `TokenService` in Application layer (or Infrastructure)
- Keycloak token refresh: POST to `/realms/{realm}/protocol/openid-connect/token`
- Keycloak logout: POST to `/realms/{realm}/protocol/openid-connect/logout`
- Redis blacklist: `erphub:token-blacklist:{jti}` TTL = token remaining lifetime
- Middleware: check blacklist before allowing request through
- All endpoints require HTTPS

---

### S4-003 - Data Transformation & Mapping Engine

| Field | Value |
|-------|-------|
| Title | Implement field mapping configuration and data transformation pipeline for ingestion |
| Story points | 13 |
| Priority | P1 |
| Dependencies | S2-001 (Ingestion API), S3-002 (External System Config) |
| FRD Ref | FR-ING-003, FR-ING-003b, FR-CFG-002, FR-CFG-003 |

**Acceptance criteria**

```gherkin
Feature: Data Transformation & Mapping

  Scenario: Direct field mapping
    Given a mapping rule: external "customer_name" → ERPNext "customer_name"
    When ingestion processes payload {"customer_name": "ABC Travel"}
    Then the transformed payload has {"customer_name": "ABC Travel"}

  Scenario: Lookup mapping
    Given a mapping rule: external "type" → ERPNext "customer_type" with lookup table
      | external    | erpnext    |
      | enterprise  | Company    |
      | individual  | Individual |
    When ingestion processes {"type": "enterprise"}
    Then the transformed payload has {"customer_type": "Company"}

  Scenario: Constant mapping
    Given a mapping rule: constant "territory" = "Vietnam"
    When ingestion processes any payload
    Then the transformed payload includes {"territory": "Vietnam"}

  Scenario: Calculated mapping
    Given a mapping rule: "total" = "qty * rate"
    When ingestion processes {"qty": 2, "rate": 3500000}
    Then the transformed payload includes {"total": 7000000}

  Scenario: Conditional mapping
    Given a mapping rule: IF country = "Vietnam" THEN tax_id_required = true
    When ingestion processes {"country": "Vietnam"}
    Then tax_id_required = true in the transformed payload

  Scenario: Child table mapping (ERPNext)
    Given a Sales Order with flat "items" array
    When transformation maps items to ERPNext child table format
    Then each item has required fields: item_code, qty, rate
    And the payload structure matches ERPNext expected format

  Scenario: Link field validation
    Given a mapping that references "Customer" in a Sales Order
    When ingestion processes the payload
    Then the API validates the referenced Customer exists in ERPNext
    And returns 400 if the linked document doesn't exist

  Scenario: Create mapping rules via API
    Given admin permission
    When POST /api/v1/systems/{id}/mappings with mapping configuration
    Then the mapping is stored and applied to future ingestions

  Scenario: List mapping rules
    Given admin permission
    When GET /api/v1/systems/{id}/mappings
    Then returns all mapping rules for the system

  Scenario: Update mapping rule
    Given admin permission
    When PUT /api/v1/mappings/{id}
    Then the mapping is updated and takes effect immediately

  Scenario: Delete mapping rule
    Given admin permission
    When DELETE /api/v1/mappings/{id}
    Then the mapping is soft-deleted and no longer applied
```

**Implementation notes:**
- `MappingService` in Application layer
- `FieldMapping` entity already exists in Domain
- Mapping types: Direct, Lookup, Constant, Calculated, Conditional
- Mapping rules stored in PostgreSQL as JSON
- Transformation pipeline: validate → map → transform → validate ERPNext format → enqueue
- `MappingRule` entity with `mapping_type` enum and `configuration` JSON column
- Calculated expressions: simple arithmetic only (no sandboxed scripts in S4)
- Link field validation: call `ErpNextClient.GetAsync(doctype, name)` before CREATE/UPDATE
- Child table mapping: transform flat arrays to ERPNext nested format
- Add CRUD endpoints: POST/GET/PUT/DELETE /api/v1/systems/{id}/mappings

---

### S4-004 - UPSERT Race Condition Prevention

| Field | Value |
|-------|-------|
| Title | Implement Redis RedLock for UPSERT operations and idempotency key enforcement |
| Story points | 5 |
| Priority | P1 |
| Dependencies | S2-001 (Ingestion API), S1-003 (Redis) |
| FRD Ref | FR-ING-004b, FR-ING-009 |

**Acceptance criteria**

```gherkin
Feature: UPSERT race condition prevention

  Scenario: Concurrent UPSERT with RedLock
    Given two concurrent requests for UPSERT Customer "CUST-EXT-001"
    When both requests arrive within 100ms
    Then only one acquires the lock and proceeds
    And the other waits and gets the cached/existing result
    And no duplicate record is created

  Scenario: Lock timeout
    Given a RedLock acquired for UPSERT
    When the lock holder takes longer than 5 seconds
    Then the lock expires
    And another request can acquire it

  Scenario: Idempotency key enforcement
    Given a request with Idempotency-Key: {ulid}
    When the same Idempotency-Key is used within 24 hours
    Then the API returns the cached response (201 for create, 200 for update)
    And no duplicate processing occurs

  Scenario: UPSERT resolves to CREATE
    Given a document that doesn't exist in ERPNext
    When UPSERT is called
    Then it checks existence → not found → CREATE (POST)

  Scenario: UPSERT resolves to UPDATE
    Given a document that exists in ERPNext
    When UPSERT is called
    Then it checks existence → found → UPDATE (PUT)
```

**Implementation notes:**
- Use `RedLock.net` NuGet or implement simple Redis lock with Lua script
- Lock key: `erphub:upsert:{doctype}:{name}`, TTL: 5s
- Idempotency: `erphub:idempotency:{key}`, TTL: 24h, value = serialized response
- Add to `IngestionService.ProcessAsync` flow
- UPSERT: acquire lock → check existence → CREATE or UPDATE → release lock
- Idempotency: check key → if exists, return cached response → if not, process and store

---

### S4-005 - DLQ Management API + Webhook Limits + Frappe Script

| Field | Value |
|-------|-------|
| Title | Implement DLQ list/replay/purge API, webhook subscription limits (10/system), and Frappe Server Script example |
| Story points | 8 |
| Priority | P1 |
| Dependencies | S2-006 (Webhooks), S2-003 (DLQ placeholder) |
| FRD Ref | FR-ING-007, FR-WHK-001, FR-WHK-001b |

**Acceptance criteria**

```gherkin
Feature: DLQ Management API

  Scenario: List DLQ messages
    Given admin permission
    When GET /api/v1/ingest/dlq?page=1&pageSize=20
    Then returns paginated list of dead-letter messages
    With failure reason, original message, and timestamp

  Scenario: Replay DLQ message
    Given admin permission
    When POST /api/v1/ingest/dlq/{id}/replay
    Then the message is re-queued for processing
    And removed from DLQ

  Scenario: Purge DLQ
    Given admin permission
    When DELETE /api/v1/ingest/dlq
    Then all DLQ messages are purged
    And returns count of purged messages

  Scenario: DLQ depth alert threshold
    Given the DLQ has more than 100 messages
    Then a warning is logged with DLQ depth
    And the metric api_hub_queue_depth{queue="dlq"} is updated

Feature: Webhook subscription limits

  Scenario: Max 10 subscriptions per system
    Given an external system with 10 active subscriptions
    When POST /api/v1/webhooks/subscriptions for the same system
    Then returns 400 with ProblemDetails type "conflict"
    And error message: "Maximum of 10 subscriptions per system reached"

Feature: Frappe Server Script example

  Scenario: Documentation exists
    Given the project repository
    Then docs/erpnext-server-script-example.py exists
    And contains a working example of Frappe Server Script
    That POSTs events to API Hub /internal/v1/events/ingest
    With HMAC signature
```

**Implementation notes:**
- DLQ: Read from RabbitMQ DLQ queue (`erphub.ingestion.dlq`) via RabbitMQ HTTP API or basic.consume
- Replay: Re-publish to original queue, acknowledge from DLQ
- Purge: Queue purge via RabbitMQ management API
- Webhook limit: Check `webhook_subscriptions` count before creating
- Frappe script: Python script example with HMAC-SHA256 signing
- Create `docs/` directory if not exists

---

### S4-006 - Kong & YARP Configuration

| Field | Value |
|-------|-------|
| Title | Implement complete Kong DB-less configuration with JWT, rate limiting, ACL, and YARP reverse proxy config |
| Story points | 8 |
| Priority | P1 |
| Dependencies | S3-001 (Rate Limiting), S1-002 (Keycloak auth) |
| FRD Ref | FR-RLM-005, §16.4, §16.6 |

**Acceptance criteria**

```gherkin
Feature: Kong Configuration

  Scenario: Kong routes ERP API Hub traffic
    Given Kong is running with DB-less config
    When a request hits /api/erp/* on Kong
    Then it strips the prefix and forwards to ERP API Hub:8008
    And adds X-Request-Source: external header

  Scenario: Kong JWT plugin validates Keycloak tokens
    Given Kong JWT plugin is configured
    When a request with valid Keycloak JWT hits Kong
    Then the request is forwarded to API Hub
    And the JWT claims are available in headers

  Scenario: Kong rate limiting plugin
    Given Kong rate-limiting plugin is configured
    When a consumer exceeds their tier limit
    Then Kong returns 429 before reaching API Hub

  Scenario: Kong ACL plugin for consumer groups
    Given Kong ACL plugin with consumer groups (premium, standard, basic)
    When a consumer in the "standard" group makes a request
    Then Kong attaches X-Consumer-Group: standard header
    And API Hub can use this for rate limit tier resolution

Feature: YARP Configuration

  Scenario: YARP routes internal traffic
    Given YARP is running on port 8888
    When internal 1StopShop traffic hits /api/erp/*
    Then YARP strips prefix and forwards to API Hub:8008
    And injects X-User-Id, X-Branch-Id, X-Role headers from JWT

  Scenario: YARP health check
    Given YARP is configured
    When API Hub health endpoint is checked
    Then YARP correctly routes to /health on API Hub
```

**Implementation notes:**
- Update `kong.yml` in `infrastructure/kong/`
- Kong plugins: jwt, rate-limiting, acl, request-transformer, prometheus
- Kong consumer groups map to rate limit tiers
- Update `ERPApiHub.Gateway/appsettings.json` for YARP routes
- Add YARP health check destination
- Docker-compose: ensure Kong service config is complete
- No need to implement sync process yet (manual CI/CD for tier changes)

---

### S4-007 - Integration Tests for Sprint 4

| Field | Value |
|-------|-------|
| Title | Integration tests for tenant registry, token lifecycle, mapping engine, UPSERT, DLQ management |
| Story points | 5 |
| Priority | P1 |
| Dependencies | S4-001, S4-002, S4-003, S4-004, S4-005, S4-006 |
| FRD Ref | All Sprint 4 stories |

**Acceptance criteria**

```gherkin
Feature: Sprint 4 integration tests

  Scenario: Tenant registry resolution tests
    Given a tenant in the database
    When TenantRegistry resolves the tenant
    Then correct erpnext_host and site_name are returned
    And Redis cache is populated

  Scenario: Token refresh tests
    Given a valid refresh token
    When POST /api/v1/auth/refresh
    Then a new token pair is returned

  Scenario: Mapping engine tests
    Given mapping rules of type direct, lookup, constant, calculated, conditional
    When the transformation pipeline processes a payload
    Then the output matches the expected ERPNext format

  Scenario: UPSERT concurrency tests
    Given two concurrent UPSERT requests for the same document
    Then no duplicate is created
    And one resolves to CREATE, one to UPDATE

  Scenario: DLQ management tests
    Given messages in the DLQ
    When GET /api/v1/ingest/dlq
    Then paginated results are returned
    And POST replay re-queues the message
```

---

## Sprint Summary

| Story | Title | SP | Priority | Dependencies |
|-------|-------|-----|----------|-------------|
| S4-001 | Tenant Registry Service | 8 | P0 | S1-002, S1-003 |
| S4-002 | Token Lifecycle (Refresh & Revoke) | 5 | P0 | S1-002 |
| S4-003 | Data Transformation & Mapping Engine | 13 | P1 | S2-001, S3-002 |
| S4-004 | UPSERT Race Condition Prevention | 5 | P1 | S2-001, S1-003 |
| S4-005 | DLQ Management API + Webhook Limits + Frappe Script | 8 | P1 | S2-006, S2-003 |
| S4-006 | Kong & YARP Configuration | 8 | P1 | S3-001, S1-002 |
| S4-007 | Integration Tests | 5 | P1 | S4-001~006 |
| **Total** | | **52 SP** | | |

### Priority Order (P0 first)

1. **S4-001** — Tenant Registry (multi-tenant routing foundation)
2. **S4-002** — Token Lifecycle (auth completeness)
3. **S4-003** — Data Transformation Engine (core business value)
4. **S4-004** — UPSERT Prevention (data integrity)
5. **S4-005** — DLQ + Webhook Limits + Frappe Script (operational readiness)
6. **S4-006** — Kong & YARP Config (deployment readiness)
7. **S4-007** — Integration Tests

### Agent Assignment Strategy

| Agent | Track | Stories |
|-------|-------|---------|
| Codex | Track A: Core | S4-001 → S4-004 |
| Claude | Track B: Ops + Config | S4-002 → S4-005 → S4-006 |
| Gemini | Cross-review | Review both tracks after merge |

2 worktrees, 2 agents chạy song song, Gemini cross-review sau merge.

### Deferred to Sprint 5+

| Item | FRD Ref | Reason |
|------|---------|--------|
| PDPA consent/export/delete endpoints | FR-VN-004 | Medium complexity, depends on data model |
| Audit log retention tiers + auto-archive | FR-AUD-003 | Needs background job framework |
| Async audit export with download link | FR-AUD-005 | Depends on storage abstraction |
| Transformation Rules (sandboxed scripts) | FR-CFG-004 | High complexity, security risk |
| Invoice deletion block (enforcement) | FR-ING-010 | Needs ingestion pipeline integration |
| Polling fallback for ERPNext events | §16.8 | Depends on Worker architecture |

---

## Definition of Done

- Same as Sprint 1-3 DoD, plus:
- Tenant Registry resolves tenants from JWT with Redis caching
- Token refresh/revoke works with Keycloak
- Data mapping engine supports 5 mapping types (direct, lookup, constant, calculated, conditional)
- UPSERT uses RedLock to prevent duplicates
- DLQ API supports list/replay/purge
- Webhook subscriptions limited to 10 per system
- Kong kong.yml has JWT, rate-limiting, ACL plugins
- YARP appsettings.json has ERP API Hub routes
- Frappe Server Script example provided in /docs
- All Sprint 4 stories have passing integration tests