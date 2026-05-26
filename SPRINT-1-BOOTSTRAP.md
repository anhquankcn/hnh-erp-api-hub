# Sprint Planning Document
# ERP API Hub - Phase 1 Bootstrap

## Sprint Info

| Field | Value |
|-------|-------|
| Sprint name | Sprint 1 - Bootstrap |
| Duration | 2 weeks |
| Dates | 2026-05-27 to 2026-06-09 |
| Product Owner | IT Manager / HNH Travel |
| Scrum Master / Tech Lead | Tech Lead ERP API Hub |
| Team | Backend .NET Engineer, DevOps Engineer, QA Engineer, Security/Identity Engineer |
| Main references | `BRD-ERP-API-HUB.md`, `FRD-ERP-API-HUB.md`, `TDD-ERP-API-HUB.md`, `SSD-ERP-API-HUB.md`, `CLAUDE.md` |

### Sprint Goal

Bootstrap nền tảng ERP API Hub để có thể chạy local bằng Docker Compose, expose API Hub qua Kong/YARP, xác thực JWT Keycloak, kết nối PostgreSQL/Redis/RabbitMQ, và cung cấp hai luồng tối thiểu: ingestion async và query cached. Cuối sprint, team có một vertical slice có health checks, OpenAPI và test coverage đủ để làm nền cho các integration tiếp theo.

## Sprint Backlog

### S1-001 - Project Scaffold

| Field | Value |
|-------|-------|
| Title | Scaffold .NET 9 Minimal API solution structure and Docker Compose skeleton |
| Story points | 5 |
| Priority | P0 |
| Assignee suggestion | Backend .NET Engineer + DevOps Engineer |
| Dependencies | None |

**Acceptance criteria**

```gherkin
Feature: Project scaffold

  Scenario: Build solution successfully
    Given the ERP API Hub repository is checked out
    When the developer runs "dotnet restore" and "dotnet build"
    Then the solution builds successfully
    And the solution contains ERPApiHub.API, ERPApiHub.Application, ERPApiHub.Domain, ERPApiHub.Infrastructure, ERPApiHub.Worker, and ERPApiHub.Tests projects

  Scenario: Run API locally
    Given the project is built
    When the developer runs ERPApiHub.API on port 8008
    Then the API process starts without runtime errors
    And OpenAPI metadata is available in development mode

  Scenario: Start compose skeleton
    Given Docker is available
    When the developer runs "docker compose up"
    Then services for erp-api-hub, erp-worker, PostgreSQL, Redis, RabbitMQ, Kong, and YARP are defined
    And only intended dev ports are published according to the TDD
```

### S1-002 - PostgreSQL Schema

| Field | Value |
|-------|-------|
| Title | Create EF Core migrations and seed data for core registry tables |
| Story points | 8 |
| Priority | P0 |
| Assignee suggestion | Backend .NET Engineer |
| Dependencies | S1-001 |

**Acceptance criteria**

```gherkin
Feature: PostgreSQL schema

  Scenario: Apply initial migration
    Given PostgreSQL 18 is running with database erphub_api_db
    When EF Core migrations are applied
    Then tables external_systems, tenant_registry, api_key_mapping, field_mappings, audit_logs, webhook_subscriptions, webhook_deliveries, and erp_processed_events exist
    And all business IDs are stored as 26-character ULIDs
    And business tables include created_at, updated_at, and deleted_at where applicable

  Scenario: Seed bootstrap tenant and system
    Given the initial migration has been applied
    When seed data runs
    Then a bootstrap tenant registry record exists for the frontend site
    And a bootstrap external system exists with an active status and rate_limit_tier

  Scenario: Enforce no hard delete pattern
    Given a repository query reads business records
    When the query is executed
    Then records with deleted_at not null are excluded by default
```

### S1-003 - Redis Integration

| Field | Value |
|-------|-------|
| Title | Implement Redis cache service and dependency health check |
| Story points | 5 |
| Priority | P0 |
| Assignee suggestion | Backend .NET Engineer |
| Dependencies | S1-001 |

**Acceptance criteria**

```gherkin
Feature: Redis integration

  Scenario: Use ERP Hub key prefix
    Given Redis is configured
    When the API stores cache, tenant context, auth token cache, or idempotency data
    Then every key starts with "erphub:"

  Scenario: Cache service round trip
    Given Redis is running
    When the application writes a value with a TTL
    Then the same value can be read before expiry
    And the value expires after the configured TTL

  Scenario: Redis health check
    Given the API is running
    When Redis is reachable
    Then readiness reports Redis as healthy
    When Redis is unreachable
    Then readiness reports Redis as unhealthy
```

### S1-004 - Keycloak JWT Validation

| Field | Value |
|-------|-------|
| Title | Validate Keycloak JWT using JWKS and enforce tenant claims |
| Story points | 8 |
| Priority | P0 |
| Assignee suggestion | Security/Identity Engineer + Backend .NET Engineer |
| Dependencies | S1-001, S1-002, S1-003 |

**Acceptance criteria**

```gherkin
Feature: Keycloak JWT validation

  Scenario: Accept valid token
    Given a JWT issued by realm HNHTravel-SGN with RS256 signature
    And the token issuer, expiry, and audience "1stopshop-api" are valid
    And the token contains BranchId
    When the client calls a protected endpoint
    Then the request is authorized
    And user claims are available to the request context

  Scenario: Reject invalid token
    Given a JWT has an invalid signature, expired timestamp, wrong issuer, or wrong audience
    When the client calls a protected endpoint
    Then the API returns 401 Unauthorized or 403 Forbidden according to the failure

  Scenario: Resolve tenant from JWT only
    Given the JWT contains BranchId
    And the request also contains an X-Branch-Id header
    When the request is processed
    Then tenant context is resolved from the BranchId claim
    And the client-supplied branch header is ignored
```

### S1-005 - Kong DB-less Config

| Field | Value |
|-------|-------|
| Title | Configure Kong DB-less gateway with JWT plugin and rate limiting |
| Story points | 5 |
| Priority | P0 |
| Assignee suggestion | DevOps Engineer + Security/Identity Engineer |
| Dependencies | S1-001, S1-004 |

**Acceptance criteria**

```gherkin
Feature: Kong DB-less routing

  Scenario: Route public ERP API traffic
    Given Kong 3.9 is running in DB-less mode
    When a request is sent to /api/erp/v1/health through Kong port 8000
    Then Kong routes the request to erp-api-hub port 8008
    And the /api/erp prefix is stripped before reaching the .NET route map

  Scenario: Enforce JWT at the edge
    Given a public request has no valid JWT
    When the request reaches Kong
    Then Kong rejects the request before forwarding to API Hub

  Scenario: Enforce public rate limiting
    Given a consumer exceeds the configured Kong rate limit tier
    When additional requests are sent
    Then Kong returns 429 Too Many Requests
```

### S1-006 - YARP Internal Gateway Config

| Field | Value |
|-------|-------|
| Title | Configure YARP internal route for ERP API Hub |
| Story points | 3 |
| Priority | P1 |
| Assignee suggestion | DevOps Engineer + Backend .NET Engineer |
| Dependencies | S1-001, S1-004 |

**Acceptance criteria**

```gherkin
Feature: YARP internal gateway

  Scenario: Route internal ERP API traffic
    Given YARP is running on port 8888
    When an internal service calls /api/erp/v1/health
    Then YARP forwards the request to erp-api-hub port 8008
    And YARP removes the /api/erp prefix before forwarding

  Scenario: Propagate internal identity context
    Given a valid internal JWT contains BranchId and user claims
    When YARP forwards the request
    Then API Hub receives the authenticated context
    And correlation headers are preserved or created
```

### S1-007 - Ingestion API

| Field | Value |
|-------|-------|
| Title | Implement POST /v1/ingest/{doctype} with validation, idempotency, audit, and queue publish |
| Story points | 8 |
| Priority | P0 |
| Assignee suggestion | Backend .NET Engineer |
| Dependencies | S1-002, S1-003, S1-004 |

**Acceptance criteria**

```gherkin
Feature: Single document ingestion

  Scenario: Accept valid ingestion request
    Given the client has api-hub:write permission
    And the doctype is allowed
    And the payload passes schema validation
    When the client posts to /v1/ingest/{doctype}
    Then the API returns 202 Accepted
    And the response includes a ULID job_id
    And a message is published to RabbitMQ exchange 1stopshop_event_bus with routing key erphub.ingestion.{doctype}.created
    And an audit log is recorded

  Scenario: Return cached response for duplicate idempotency key
    Given a successful ingestion request used X-Idempotency-Key
    When the same request is sent again within the idempotency TTL
    Then the API returns the original response without publishing a duplicate RabbitMQ message

  Scenario: Reject invalid ingestion request
    Given the doctype is not allowed or required fields are missing
    When the client posts to /v1/ingest/{doctype}
    Then the API returns 400 Bad Request or 422 Unprocessable Entity
    And an audit log captures the failed result
```

### S1-008 - Query API

| Field | Value |
|-------|-------|
| Title | Implement GET /v1/query/{doctype} with pagination, filters, field selection, cache, and audit |
| Story points | 8 |
| Priority | P0 |
| Assignee suggestion | Backend .NET Engineer |
| Dependencies | S1-002, S1-003, S1-004 |

**Acceptance criteria**

```gherkin
Feature: Document query

  Scenario: Query list documents
    Given the client has api-hub:read permission
    And the doctype is allowed
    When the client calls /v1/query/{doctype} with page, pageSize, filters, fields, and orderBy
    Then the API calls ERPNext using the resolved tenant context
    And the API returns a paginated response
    And an audit log is recorded

  Scenario: Use Redis cache for repeated query
    Given a query response has been cached using key pattern erphub:query:{tenant}:{doctype}:{hash}
    When the same query is requested again before TTL expiry
    Then the API returns the cached response
    And ERPNext is not called again

  Scenario: Reject invalid query
    Given the filter syntax is invalid or the doctype is not allowed
    When the client calls /v1/query/{doctype}
    Then the API returns 400 Bad Request or 404 Not Found
```

### S1-009 - RabbitMQ Consumer

| Field | Value |
|-------|-------|
| Title | Implement erp-worker RabbitMQ consumer and event processing foundation |
| Story points | 8 |
| Priority | P1 |
| Assignee suggestion | Backend .NET Engineer |
| Dependencies | S1-002, S1-007 |

**Acceptance criteria**

```gherkin
Feature: RabbitMQ consumer

  Scenario: Consume ingestion event
    Given a message exists on an erphub ingestion queue bound to exchange 1stopshop_event_bus
    When erp-worker receives the message
    Then the worker validates the event envelope
    And deduplicates by eventId using erp_processed_events
    And calls the ERPNext client abstraction
    And records the processed event status

  Scenario: Retry transient failures
    Given ERPNext returns a transient error
    When the worker processes the event
    Then the worker retries with exponential backoff
    And after the maximum retry count the message is routed to erphub.dlq.ingestion

  Scenario: Expose worker health internally
    Given erp-worker is running
    When the internal health endpoint is checked
    Then it reports RabbitMQ and database dependency status
    And the worker port is not exposed publicly in Docker Compose
```

### S1-010 - Health Check Endpoints

| Field | Value |
|-------|-------|
| Title | Implement liveness, readiness, startup, and OpenAPI visibility checks |
| Story points | 3 |
| Priority | P0 |
| Assignee suggestion | Backend .NET Engineer + QA Engineer |
| Dependencies | S1-001, S1-002, S1-003, S1-009 |

**Acceptance criteria**

```gherkin
Feature: Health checks

  Scenario: Liveness is independent from dependencies
    Given the API process is running
    When GET /health/live is called
    Then the API returns 200 OK even if external dependencies are degraded

  Scenario: Readiness verifies dependencies
    Given PostgreSQL, Redis, RabbitMQ, and configured ERPNext health checks are reachable
    When GET /health/ready is called
    Then the API returns 200 OK
    When one required dependency is unreachable
    Then the API returns 503 Service Unavailable

  Scenario: Startup reports migration state
    Given the API starts
    When GET /health/startup is called
    Then the API reports whether configuration is loaded and database migrations are applied

  Scenario: OpenAPI is generated
    Given the API is running in development or CI documentation mode
    When OpenAPI JSON is requested
    Then it includes ingestion, query, and health endpoints
```

## Definition of Done

- Code reviewed by Tech Lead or delegated reviewer.
- Unit tests pass with >80% coverage for changed application/domain logic.
- Integration tests pass for PostgreSQL, Redis, RabbitMQ, auth middleware, ingestion API, query API, and health checks.
- `docker compose up` starts the bootstrap stack successfully.
- API docs are generated via OpenAPI and include request/response/error contracts.
- Structured logs include correlation/request ID for API and worker flows.
- No hard delete for business data; soft delete pattern is respected.
- All business IDs use ULID, not UUID or auto-increment.
- Redis keys use `erphub:` prefix.
- RabbitMQ uses shared topic exchange `1stopshop_event_bus`; no new exchange is introduced.
- Kong remains the public gateway and YARP remains the internal gateway.

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Keycloak JWKS / audience config mismatch | Public and internal auth cannot be validated reliably | Confirm realm `HNHTravel-SGN`, issuer URL, audience `1stopshop-api`, and BranchId mapper before coding; keep auth settings environment-driven |
| Kong DB-less JWT config and Keycloak key rotation | Edge auth may reject valid tokens after key rotation | Document public key update procedure; keep Kong config versioned; add a smoke test through Kong |
| ERPNext dev endpoint unavailable during sprint | Query and worker cannot complete true ERP integration tests | Build ERPNext client behind an interface; use WireMock/test double for CI; keep live ERP smoke test separate |
| Sprint scope is large for two weeks | Stories may spill over or be implemented shallowly | Prioritize P0 vertical slice first: scaffold, DB, Redis, auth, Kong, ingestion, query, health; keep webhook/batch/admin screens out of Sprint 1 |
| RabbitMQ retry/DLQ behavior is under-tested | Failed ingestion may be lost or duplicated | Add integration test for success, retry, DLQ, and dedupe by eventId |
| Tenant handling accidentally trusts client header | Cross-tenant data leak | Acceptance tests must prove BranchId comes from JWT claim only; ignore `X-Branch-Id` from external clients |
| Audit logging is skipped in error paths | Compliance gap; incomplete traceability | Add middleware or endpoint filter that writes audit result for both success and failure |
| Docker Compose port conflicts with existing HNH services | Local bootstrap is unstable | Follow documented ports: API 8008, worker 8009 internal, Kong 8000, YARP 8888, PostgreSQL host 5452 |

## Demo Plan

1. Start the bootstrap stack with `docker compose up` and show API Hub, worker, PostgreSQL, Redis, RabbitMQ, Kong, and YARP running.
2. Apply EF Core migrations and show seeded tenant/external system records in `erphub_api_db`.
3. Call `/health/live`, `/health/ready`, and `/health/startup` directly and through the configured gateway route.
4. Demonstrate rejected unauthenticated request and accepted valid Keycloak JWT request with BranchId-derived tenant context.
5. Submit `POST /v1/ingest/{doctype}` with `X-Idempotency-Key`, show `202 Accepted`, RabbitMQ message publish, audit log, and duplicate request returning cached response.
6. Run erp-worker against a queued ingestion event and show processed-event dedupe plus DLQ behavior for a simulated failure.
7. Call `GET /v1/query/{doctype}` with pagination, filters, and fields, then repeat the same query to show Redis cache hit.
8. Open generated OpenAPI JSON/Swagger and confirm ingestion, query, and health endpoints are documented.
