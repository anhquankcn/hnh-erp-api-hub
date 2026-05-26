# System Specification Document (SSD)
# ERP API Hub — HNH Travel

**Document Version:** 1.0-Draft  
**Date:** 2026-05-26  
**Status:** Draft — Pending Review  
**Related Documents:** BRD-ERP-API-HUB · FRD-ERP-API-HUB-v1.2 · TDD-ERP-API-HUB

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Functional Specification](#2-functional-specification)
3. [Non-Functional Requirements](#3-non-functional-requirements)
4. [Data Specification](#4-data-specification)
5. [Interface Specification](#5-interface-specification)
6. [Traceability Matrix](#6-traceability-matrix)
7. [Test Cases](#7-test-cases)

---

## 1. System Overview

### 1.1 System Context

ERP API Hub là service riêng biệt, chạy song song với các microservice hiện có trong hệ sinh thái HNH Travel. Service đóng vai trò cầu nối giữa:

- **Hệ 1StopShop** (Core, CRM, Ticketing, Quotation, Payment) — .NET 9, YARP Gateway
- **ERPNext** (Frappe v15) — hệ ERP kế toán, kho, mua hàng

### 1.2 System Boundaries

| Boundary | Direction | Protocol | Auth |
|----------|-----------|----------|------|
| External → Kong → API Hub → ERPNext | Inbound (Ingestion) | HTTPS + REST | Keycloak JWT + Kong |
| External → Kong → API Hub ← ERPNext | Outbound (Query) | HTTPS + REST | Keycloak JWT + Kong |
| 1StopShop → RabbitMQ → API Hub → ERPNext | Event-driven | AMQP | RabbitMQ |
| ERPNext → API Hub → External | Webhook | HTTPS + HMAC | HMAC-SHA256 |
| API Hub → YARP → 1StopShop | Sync API (internal) | HTTPS + REST | Service Account JWT |
| ERPNext → Kong → External | Webhook (outbound) | HTTPS + HMAC | HMAC-SHA256 |

### 1.3 Service Inventory

| Service | Port | Role | DB |
|---------|------|------|-----|
| kong | 8000 | Public API Gateway: JWT, rate limiting, API key auth, transformation | — |
| erp-api-hub | 8008 | API endpoints, auth proxy, field mapping | erphub_api_db |
| erp-worker | 8009 | RabbitMQ consumer, background processing | erphub_api_db |

---

## 2. Functional Specification

### SYS-AUTH-001: Keycloak Token Validation

| Field | Value |
|-------|-------|
| **ID** | SYS-AUTH-001 |
| **Name** | Keycloak Token Validation |
| **Source FR** | FR-AUTH-001 |
| **Description** | Validate JWT tokens issued by Keycloak shared realm `HNHTravel-SGN` |
| **Endpoint** | `https://quanna.tail072b2f.ts.net:8443/realms/HNHTravel-SGN` |
| **Algorithm** | RS256 (validate via JWKS, **không share secret**) |
| **Process** | 1. Kong validates JWT (RS256 via JWKS) on inbound request 2. API Hub extracts claims from validated token 3. Validate expiry (exp claim) 4. Validate issuer (iss = realm URL) 5. Validate audience (aud includes `1stopshop-api`) 6. Extract claims: sub, preferred_username, email, realm_access.roles[], BranchId, DepartmentId, TeamId |
| **Error Cases** | E1: Token expired → 401 Unauthorized + refresh hint E2: Invalid signature → 401 Unauthorized E3: Wrong audience → 403 Forbidden E4: Missing BranchId claim → 403 Forbidden |
| **Performance** | < 5ms per validation (JWKS cached) |

### SYS-AUTH-002: Service Account Token Exchange

| Field | Value |
|-------|-------|
| **ID** | SYS-AUTH-002 |
| **Name** | Service Account Token Exchange |
| **Source FR** | FR-AUTH-002 |
| **Description** | Exchange Keycloak JWT for ERPNext API Key/Secret |
| **Process** | 1. Validate incoming JWT (SYS-AUTH-001) 2. Lookup `api_key_mapping` table by keycloak_user_id + system_id 3. Decrypt ERPNext credentials (AES-256-GCM) 4. Return ERPNext session token |
| **Security** | ERPNext credentials encrypted at rest with AES-256-GCM. Master key in env var / Vault. |
| **Error Cases** | E1: No mapping found → 403 Forbidden E2: Decryption failure → 500 Internal Error + alert E3: ERPNext auth failed → 502 Bad Gateway |
| **Cache** | Token cached in Redis 15 min (key: `erphub:auth:{token_hash}`) |

### SYS-AUTH-003: API Key Authentication

| Field | Value |
|-------|-------|
| **ID** | SYS-AUTH-003 |
| **Name** | API Key Authentication |
| **Source FR** | FR-AUTH-003 |
| **Description** | Authenticate external systems using API key + HMAC signature |
| **Process** | 1. Extract API key from `X-API-Key` header 2. Extract timestamp from `X-Timestamp` header 3. Extract signature from `X-Signature` header 4. Reject if timestamp > 5 minutes drift (replay protection) 5. Lookup system by API key (Redis cached) 6. Compute HMAC-SHA256 of request body using system secret 7. Compare computed vs provided signature (constant-time) |
| **Error Cases** | E1: Invalid API key → 401 Unauthorized E2: Expired timestamp → 401 Unauthorized E3: Signature mismatch → 401 Unauthorized E4: Rate limit exceeded → 429 Too Many Requests |
| **Rate Limiting** | Per-system, per-tier: TIER_1=10/s, TIER_2=50/s, TIER_3=200/s |

### SYS-AUTH-004: Multi-Tenant Tenant Context

| Field | Value |
|-------|-------|
| **ID** | SYS-AUTH-004 |
| **Name** | Multi-Tenant Tenant Context |
| **Source FR** | FR-AUTH-004 |
| **Description** | Resolve tenant (branch) context from JWT claim |
| **Process** | 1. Extract `BranchId` từ JWT claim (Keycloak protocol mapper) 2. Lookup `tenant_registry` (Redis cached, TTL 1 min) 3. Verify `is_active = true` 4. Validate health status (warn if degraded, reject if down) 5. Include `X-Frappe-Site-Name: {site_name}` in ERPNext calls 6. Route to `erpnext_host` |
| **Critical Rule** | **Client KHÔNG BAO GIỜ được tin để chỉ định branch_id** — lấy từ JWT claim |
| **Error Cases** | E1: BranchId claim missing → 403 Forbidden E2: Tenant not found → 404 Not Found E3: Tenant inactive → 403 Forbidden E4: Tenant health down → 503 Service Unavailable |

### SYS-ING-001: Single Document Ingestion

| Field | Value |
|-------|-------|
| **ID** | SYS-ING-001 |
| **Name** | Single Document Ingestion |
| **Source FR** | FR-ING-001 |
| **Endpoint** | `POST /api/erp/v1/ingest/{doctype}` |
| **Auth** | Keycloak JWT (SYS-AUTH-001) or API Key (SYS-AUTH-003) |
| **Headers** | `X-Idempotency-Key: {ulid}` (optional but recommended) |
| **Process** | 1. Validate auth 2. Resolve tenant (SYS-AUTH-004) 3. Validate doctype against allowed list 4. Apply field mapping transformation 5. Check idempotency (Redis lookup by key) 6. Push to RabbitMQ queue `erphub.ingestion.{doctype}.created` 7. Return 202 Accepted with job_id |
| **Idempotency** | If same `X-Idempotency-Key` received within 5 min → return cached response |
| **Error Cases** | E1: Invalid doctype → 400 Bad Request E2: Duplicate idempotency key → 200 OK (return cached) E3: Validation failed → 422 Unprocessable Entity E4: Rate limit → 429 Too Many Requests |

### SYS-ING-002: Batch Document Ingestion

| Field | Value |
|-------|-------|
| **ID** | SYS-ING-002 |
| **Name** | Batch Document Ingestion |
| **Source FR** | FR-ING-002 |
| **Endpoint** | `POST /api/erp/v1/ingest/{doctype}/batch` |
| **Limits** | Max 100 items per batch. Max 1MB total payload. |
| **Process** | Same as SYS-ING-001 per item, but: 1. Validate all items first 2. Push individual items to queue 3. Return 202 with batch_id + per-item status |
| **Error Cases** | E1: Batch > 100 items → 400 Bad Request E2: Payload > 1MB → 413 Payload Too Large E3: Partial validation failure → 207 Multi-Status |

### SYS-QRY-001: Document Query (List)

| Field | Value |
|-------|-------|
| **ID** | SYS-QRY-001 |
| **Name** | Document Query (List) |
| **Source FR** | FR-QRY-001 |
| **Endpoint** | `GET /api/erp/v1/query/{doctype}` |
| **Parameters** | `page` (default 1), `pageSize` (default 20, max 100), `filters` (JSON), `fields` (array), `orderBy` |
| **Auth** | Keycloak JWT (SYS-AUTH-001) |
| **Process** | 1. Validate auth + tenant 2. Check Redis cache 3. If miss → call ERPNext REST API 4. Apply PII masking per role 5. Cache result (5 min TTL) 6. Return paginated response |
| **Cache Key** | `erphub:query:{tenant}:{doctype}:{hash(filters+fields+orderBy)}` |
| **Error Cases** | E1: Doctype not found → 404 Not Found E2: ERPNext down → 502 Bad Gateway E3: Filter syntax error → 400 Bad Request |

### SYS-QRY-002: Document Query (Single)

| Field | Value |
|-------|-------|
| **ID** | SYS-QRY-002 |
| **Name** | Document Query (Single) |
| **Source FR** | FR-QRY-002 |
| **Endpoint** | `GET /api/erp/v1/query/{doctype}/{name}` |
| **Process** | Same as SYS-QRY-001 but single document. Cache key: `erphub:query:{tenant}:{doctype}:{name}` |
| **Error Cases** | E1: Document not found → 404 Not Found |

### SYS-QRY-003: Aggregation Query

| Field | Value |
|-------|-------|
| **ID** | SYS-QRY-003 |
| **Name** | Aggregation Query |
| **Source FR** | FR-QRY-003 |
| **Endpoint** | `POST /api/erp/v1/query/{doctype}/aggregate` |
| **Body** | `{ "filters": {}, "group_by": ["field1"], "aggregations": [{"field": "total", "function": "sum"}] }` |
| **Process** | 1. Validate auth + tenant 2. Check Redis cache 3. Call ERPNext report API 4. Cache result (10 min TTL for aggregates) 5. Return aggregated data |

### SYS-EVT-001: Event Consumption (RabbitMQ)

| Field | Value |
|-------|-------|
| **ID** | SYS-EVT-001 |
| **Name** | RabbitMQ Event Consumption |
| **Source FR** | FR-EVT-001 |
| **Exchange** | `1stopshop_event_bus` (topic) |
| **Queues** | `erphub.ingestion.customer.created`, `erphub.ingestion.sales_invoice.posted`, `erphub.ingestion.payment.confirmed` |
| **Process** | 1. Consume event envelope (eventId, eventType, source, timestamp, version, payload) 2. Dedupe using `eventId` (table `erp_processed_events`) 3. Transform payload using field mapping 4. Call ERPNext REST API 5. On business error → DLQ `erphub.dlq.ingestion` + Prometheus alert |
| **Error Handling** | Retry 3x with exponential backoff (1s, 5s, 30s). DLQ after max retries. |
| **Critical** | **Dùng exchange chung `1stopshop_event_bus`. Không tạo exchange riêng.**

**Kong là public-facing gateway. YARP là internal gateway. Không nhầm lẫn.** |

### SYS-WBHK-001: Webhook Registration

| Field | Value |
|-------|-------|
| **ID** | SYS-WBHK-001 |
| **Name** | Webhook Registration |
| **Source FR** | FR-WBHK-001 |
| **Endpoint** | `POST /api/erp/v1/webhooks/subscriptions` |
| **Auth** | Keycloak JWT (SuperAdmin, COO, or Accounting role) |
| **Process** | 1. Validate auth (require admin role) 2. Validate event types against allowed list 3. Validate webhook URL (must be HTTPS) 4. Generate HMAC secret 5. Store subscription with encrypted secret |

### SYS-WBHK-002: Webhook Delivery

| Field | Value |
|-------|-------|
| **ID** | SYS-WBHK-002 |
| **Name** | Webhook Delivery |
| **Source FR** | FR-WBHK-002 |
| **Process** | 1. Receive ERPNext event (via Frappe Server Script or internal webhook) 2. Match event to subscriptions 3. Sign payload with HMAC-SHA256 4. POST to subscriber URL with headers: X-ERP-Signature, X-ERP-Event, X-ERP-Delivery-Id, X-Timestamp 5. Record delivery status 6. Retry on failure (3x, exponential backoff) 7. DLQ after max retries |

### SYS-RLM-001 to SYS-RLM-005: Rate Limiting

| ID | Name | Window | Limit |
|----|------|--------|-------|
| SYS-RLM-001 | TIER_1 | 1 second | 10 requests |
| SYS-RLM-002 | TIER_2 | 1 second | 50 requests |
| SYS-RLM-003 | TIER_3 | 1 second | 200 requests |
| SYS-RLM-004 | Burst protection | 1 minute | 10x tier limit |
| SYS-RLM-005 | Kong rate limiting | Config | Kong handles public-facing rate limiting (DB-less mode) |

### SYS-LOG-001: Audit Logging

| Field | Value |
|-------|-------|
| **ID** | SYS-LOG-001 |
| **Name** | Comprehensive Audit Logging |
| **Source FR** | FR-LOG-001 |
| **Table** | `audit_logs` |
| **Fields** | log_id (ULID), request_id, tenant_id, system_id, user_id, method, endpoint, status_code, duration_ms, request_size_bytes, response_size_bytes, client_ip, user_agent, created_at |
| **Retention** | 90 days hot, 1 year cold storage |
| **Query Performance** | Indexed on created_at, tenant_id, system_id, user_id |

---

## 3. Non-Functional Requirements

| ID | Category | Requirement | Target |
|----|----------|-------------|--------|
| NFR-001 | Latency | P95 response time (cache hit) | < 50ms |
| NFR-002 | Latency | P95 response time (cache miss) | < 500ms |
| NFR-003 | Throughput | Sustained concurrent requests | 100 req/s |
| NFR-004 | Availability | Uptime SLA | 99.9% (8.76h/year downtime) |
| NFR-005 | Scalability | Horizontal scaling | Stateless, scale to N instances |
| NFR-006 | Security | TLS | 1.3 mandatory for all endpoints |
| NFR-007 | Security | PII masking | AES-256-GCM at rest, role-based masking |
| NFR-008 | Security | Replay protection | X-Request-ID + dedupe (5 min Redis TTL) |
| NFR-009 | Observability | Metrics | Prometheus + Grafana dashboard |
| NFR-010 | Observability | Logging | Serilog structured JSON |
| NFR-011 | Observability | Health check | `/health` endpoint with dependency checks |
| NFR-012 | Data | Soft delete | deleted_at IS NULL, cấm hard delete |
| NFR-013 | Data | ULID IDs | 26 ký tự, sortable theo thời gian |
| NFR-014 | Data | Multi-tenant | branch_id từ JWT, không tin client |
| NFR-015 | Data | Audit | created_at/by, updated_at/by trên mọi bảng |
| NFR-016 | Data | Timestamp | TIMESTAMPTZ, lưu UTC+7, compare normalize |
| NFR-017 | Integration | No cross-DB query | Gọi API hoặc consume event |
| NFR-018 | Integration | Shared RabbitMQ | Exchange `1stopshop_event_bus`, không tạo riêng |

---

## 4. Data Specification

### 4.1 Database: erphub_api_db (PostgreSQL 18)

**Naming conventions:**
- Tables: `snake_case` số nhiều
- Indexes: `idx_{table}_{cols}`
- Foreign keys: `fk_{table}_{ref}`
- Primary keys: ULID 26 ký tự (VARCHAR(26))

**Every table must have:**
- `{table}_id VARCHAR(26) PRIMARY KEY` (ULID)
- `created_at TIMESTAMPTZ DEFAULT NOW()`
- `updated_at TIMESTAMPTZ DEFAULT NOW()`
- `deleted_at TIMESTAMPTZ` (soft delete, NULL = active)
- `created_by VARCHAR(26)`
- `updated_by VARCHAR(26)`

### 4.2 Table Summary

| Table | Purpose | Key Fields |
|-------|---------|-----------|
| external_systems | External system registry | system_id, system_name, system_type, rate_limit_tier, tenant_id |
| tenant_registry | Multi-tenant config | tenant_id (= BranchId), site_name, erpnext_host, health_status |
| api_key_mapping | Auth mapping | mapping_id, system_id, keycloak_user_id, erpnext_api_key_enc |
| webhook_subscriptions | Webhook config | subscription_id, system_id, event_types[], webhook_url, secret_enc |
| webhook_deliveries | Webhook delivery log | delivery_id, subscription_id, status, http_status, attempted_at |
| audit_logs | Audit trail | log_id, request_id, tenant_id, method, endpoint, status_code |
| field_mappings | Field transform | mapping_id, system_id, external_field, erpnext_field, transform_script |
| erp_processed_events | Event dedupe | event_id (ULID), source, event_type, processed_at |

---

## 5. Interface Specification

### 5.1 REST API Base URL

```
Production: https://api.hnhtravel.work/api/erp/v1 (via Kong :8000)
Staging:    https://erp.hnhtravel.work/api/erp/v1
Internal:   http://erp-api-hub:8008 (via YARP :8888)
```

### 5.2 Common Headers

| Header | Direction | Required | Description |
|--------|-----------|----------|-------------|
| `Authorization` | In | Yes | Bearer {JWT} |
| `X-Request-ID` | In | Recommended | ULID correlation ID |
| `X-Idempotency-Key` | In | For mutations | ULID for dedup |
| `X-Branch-Id` | Out | N/A | Injected by Gateway from JWT |
| `X-User-Id` | Out | N/A | Injected by Gateway from JWT |
| `X-Role` | Out | N/A | Injected by Gateway from JWT |
| `X-Frappe-Site-Name` | Internal | Yes | Tenant routing to ERPNext |

### 5.3 Standard Response Envelope

```json
{
  "success": true,
  "data": { ... },
  "meta": {
    "request_id": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
    "timestamp": "2026-05-26T14:30:00+07:00",
    "page": 1,
    "page_size": 20,
    "total_count": 150
  }
}
```

### 5.4 Standard Error Envelope

```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "The request body contains validation errors",
    "details": [
      { "field": "customer_name", "message": "Required", "code": "required" }
    ],
    "request_id": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
    "timestamp": "2026-05-26T14:30:00+07:00"
  }
}
```

### 5.5 RabbitMQ Event Envelope

```json
{
  "eventId": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
  "eventType": "payment.confirmed",
  "source": "PaymentService",
  "timestamp": "2026-05-26T14:30:00+07:00",
  "version": 1,
  "correlationId": "01ARZ...",
  "payload": { ... }
}
```

---

## 6. Traceability Matrix

### BR → FR → SYS → TC

| BR | FR | SYS | TC (Test Case) |
|----|-----|------|------|
| BR-01 | FR-AUTH-001 | SYS-AUTH-001 | TC-AUTH-001: Valid JWT → 200 |
| BR-01 | FR-AUTH-001 | SYS-AUTH-001 | TC-AUTH-002: Expired JWT → 401 |
| BR-01 | FR-AUTH-001 | SYS-AUTH-001 | TC-AUTH-003: Invalid signature → 401 |
| BR-01 | FR-AUTH-001 | SYS-AUTH-001 | TC-AUTH-004: Wrong audience → 403 |
| BR-01 | FR-AUTH-002 | SYS-AUTH-002 | TC-AUTH-005: Token exchange success → ERPNext session |
| BR-01 | FR-AUTH-002 | SYS-AUTH-002 | TC-AUTH-006: No mapping → 403 |
| BR-02 | FR-AUTH-003 | SYS-AUTH-003 | TC-AUTH-007: Valid API key → 200 |
| BR-02 | FR-AUTH-003 | SYS-AUTH-003 | TC-AUTH-008: Expired timestamp → 401 |
| BR-02 | FR-AUTH-003 | SYS-AUTH-003 | TC-AUTH-009: HMAC mismatch → 401 |
| BR-03 | FR-AUTH-004 | SYS-AUTH-004 | TC-AUTH-010: Missing BranchId → 403 |
| BR-03 | FR-AUTH-004 | SYS-AUTH-004 | TC-AUTH-011: Inactive tenant → 403 |
| BR-04 | FR-ING-001 | SYS-ING-001 | TC-ING-001: Valid ingestion → 202 |
| BR-04 | FR-ING-001 | SYS-ING-001 | TC-ING-002: Invalid doctype → 400 |
| BR-04 | FR-ING-001 | SYS-ING-001 | TC-ING-003: Duplicate idempotency key → 200 (cached) |
| BR-04 | FR-ING-001 | SYS-ING-001 | TC-ING-004: Rate limit exceeded → 429 |
| BR-05 | FR-ING-002 | SYS-ING-002 | TC-ING-005: Batch 50 items → 202 |
| BR-05 | FR-ING-002 | SYS-ING-002 | TC-ING-006: Batch > 100 → 400 |
| BR-05 | FR-ING-002 | SYS-ING-002 | TC-ING-007: Batch > 1MB → 413 |
| BR-06 | FR-QRY-001 | SYS-QRY-001 | TC-QRY-001: List with pagination → 200 |
| BR-06 | FR-QRY-001 | SYS-QRY-001 | TC-QRY-002: Cache hit → < 50ms |
| BR-06 | FR-QRY-001 | SYS-QRY-001 | TC-QRY-003: ERPNext down → 502 |
| BR-07 | FR-QRY-002 | SYS-QRY-002 | TC-QRY-004: Single document → 200 |
| BR-07 | FR-QRY-002 | SYS-QRY-002 | TC-QRY-005: Not found → 404 |
| BR-08 | FR-QRY-003 | SYS-QRY-003 | TC-QRY-006: Aggregate query → 200 |
| BR-09 | FR-EVT-001 | SYS-EVT-001 | TC-EVT-001: Event consume → ERPNext updated |
| BR-09 | FR-EVT-001 | SYS-EVT-001 | TC-EVT-002: Duplicate event → skip (dedupe) |
| BR-09 | FR-EVT-001 | SYS-EVT-001 | TC-EVT-003: Business error → DLQ |
| BR-10 | FR-WBHK-001 | SYS-WBHK-001 | TC-WBHK-001: Register webhook → 201 |
| BR-10 | FR-WBHK-002 | SYS-WBHK-002 | TC-WBHK-002: Deliver webhook → 200 |
| BR-10 | FR-WBHK-002 | SYS-WBHK-002 | TC-WBHK-003: HMAC verification → pass |
| BR-11 | FR-RLM-001 | SYS-RLM-001 | TC-RLM-001: TIER_1 limit → 429 after 10/s |
| BR-11 | FR-RLM-002 | SYS-RLM-002 | TC-RLM-002: TIER_2 limit → 429 after 50/s |
| BR-11 | FR-RLM-003 | SYS-RLM-003 | TC-RLM-003: TIER_3 limit → 429 after 200/s |
| BR-12 | FR-LOG-001 | SYS-LOG-001 | TC-LOG-001: Every request logged → audit_logs |

---

## 7. Test Cases

### 7.1 Auth Test Cases

#### TC-AUTH-001: Valid JWT → 200 OK

```
GIVEN: Valid Keycloak JWT with BranchId claim
WHEN: GET /api/erp/v1/query/Customer?page=1&pageSize=10
THEN: Response 200 OK with data
AND: Response contains X-Request-ID header
```

#### TC-AUTH-002: Expired JWT → 401 Unauthorized

```
GIVEN: Expired Keycloak JWT
WHEN: GET /api/erp/v1/query/Customer
THEN: Response 401 Unauthorized
AND: Error body contains refresh hint
```

#### TC-AUTH-003: Invalid Signature → 401 Unauthorized

```
GIVEN: JWT with tampered signature
WHEN: GET /api/erp/v1/query/Customer
THEN: Response 401 Unauthorized
AND: Error body: "Invalid token signature"
```

#### TC-AUTH-004: Wrong Audience → 403 Forbidden

```
GIVEN: Valid JWT but aud != "1stopshop-api"
WHEN: GET /api/erp/v1/query/Customer
THEN: Response 403 Forbidden
AND: Error body: "Invalid audience"
```

#### TC-AUTH-010: Missing BranchId Claim → 403 Forbidden

```
GIVEN: Valid JWT but no BranchId claim
WHEN: GET /api/erp/v1/query/Customer
THEN: Response 403 Forbidden
AND: Error body: "Missing tenant context"
```

### 7.2 Ingestion Test Cases

#### TC-ING-001: Valid Ingestion → 202 Accepted

```
GIVEN: Valid JWT + valid doctype + valid payload
WHEN: POST /api/erp/v1/ingest/Customer
  Body: { "customer_name": "Test", "customer_type": "Individual" }
  Headers: X-Idempotency-Key: 01ARZ3NDEKTSV4RRFFQ69G5FAV
THEN: Response 202 Accepted
AND: Body contains job_id (ULID)
AND: Event published to RabbitMQ
```

#### TC-ING-003: Duplicate Idempotency Key → 200 OK

```
GIVEN: Same X-Idempotency-Key used within 5 minutes
WHEN: POST /api/erp/v1/ingest/Customer (same key)
THEN: Response 200 OK
AND: Body contains cached response (no duplicate processing)
```

### 7.3 Query Test Cases

#### TC-QRY-001: List with Pagination → 200 OK

```
GIVEN: Valid JWT + valid doctype
WHEN: GET /api/erp/v1/query/Customer?page=2&pageSize=20
THEN: Response 200 OK
AND: Body contains items[], meta.page=2, meta.page_size=20
AND: meta.total_count > 0
```

#### TC-QRY-002: Cache Hit → < 50ms

```
GIVEN: Same query requested within cache TTL (5 min)
WHEN: GET /api/erp/v1/query/Customer (second request)
THEN: Response time < 50ms (P95)
AND: Redis hit recorded in logs
```

### 7.4 Event Test Cases

#### TC-EVT-001: Event Consume → ERPNext Updated

```
GIVEN: Event published to 1stopshop_event_bus with routing key payment.confirmed
WHEN: erp-worker consumes event
THEN: Event processed successfully
AND: ERPNext Sales Invoice status updated
AND: erp_processed_events table has record with eventId
```

#### TC-EVT-002: Duplicate Event → Skip

```
GIVEN: Same eventId received twice
WHEN: erp-worker processes second event
THEN: Event skipped (dedupe via erp_processed_events)
AND: No duplicate data in ERPNext
```

#### TC-EVT-003: Business Error → DLQ

```
GIVEN: Event with invalid business data
WHEN: erp-worker processing fails after 3 retries
THEN: Event routed to DLQ erphub.dlq.ingestion
AND: Prometheus alert fired
```

---

**End of Document**