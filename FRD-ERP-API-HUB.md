# Functional Requirements Document (FRD)
# ERP API Hub — HNH Travel

**Document Version:** 1.0-Draft  
**Date:** 2026-05-26  
**Author:** HNH Technical Team  
**Status:** Draft — Pending Review  
**Related Documents:** BRD-ERP-API-HUB-v1.0

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [System Overview](#2-system-overview)
3. [Authentication & Authorization Module](#3-authentication--authorization-module)
4. [Ingestion Module](#4-ingestion-module)
5. [Query Module](#5-query-module)
6. [Webhook Module](#6-webhook-module)
7. [Audit & Logging Module](#7-audit--logging-module)
8. [Rate Limiting Module](#8-rate-limiting-module)
9. [Configuration Module](#9-configuration-module)
10. [API Specifications](#10-api-specifications)
11. [Data Flow Diagrams](#11-data-flow-diagrams)
12. [Error Handling](#12-error-handling)
13. [Security Requirements](#13-security-requirements)
14. [Performance Requirements](#14-performance-requirements)
15. [Non-Functional Requirements](#15-non-functional-requirements)

---

## 1. Introduction

### 1.1 Purpose
This FRD defines the functional requirements for the ERP API Hub — a standalone integration service that acts as a gateway between external systems and the ERPNext v15 ERP core of HNH Travel.

### 1.2 Scope
The ERP API Hub provides:
- **Inbound data ingestion** from external systems to ERPNext
- **Outbound data queries** from external systems to ERPNext
- **Event-driven webhooks** from ERPNext to external systems
- **Cross-cutting concerns**: authentication, rate limiting, audit logging, retry mechanisms

### 1.3 Definitions

| Term | Definition |
|------|-----------|
| API Hub | The standalone integration service described in this document |
| Doctype | ERPNext's document type system (equivalent to database tables) |
| ERPNext | The open-source ERP system (v15) powering HNH Travel |
| External System | Any third-party system connecting to the API Hub |
| Frappe | The Python web framework underlying ERPNext |
| Ingestion | Process of receiving and storing external data into ERP |
| Idempotency Key | Unique identifier preventing duplicate processing |
| Tenant | A business branch/site within the multi-tenant ERP |

### 1.4 References

- BRD-ERP-API-HUB-v1.0
- ERPNext REST API Documentation: https://frappeframework.com/docs/user/en/api/rest
- Keycloak Documentation: https://www.keycloak.org/documentation
- Kong Gateway Documentation: https://docs.konghq.com/

---

## 2. System Overview

### 2.1 Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      EXTERNAL SYSTEMS                         │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐       │
│  │   CRM    │  │E-commerce│  │  HR/     │  │  Partner │       │
│  │(Salesforce│  │(Website) │  │ Payroll  │  │   APIs   │       │
│  │ /HubSpot)│  │          │  │          │  │          │       │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘  └────┬─────┘       │
│       │             │             │             │               │
└───────┼─────────────┼─────────────┼─────────────┼───────────────┘
        │             │             │             │
        └─────────────┴──────┬──────┴─────────────┘
                             │
                    ┌────────▼────────┐
                    │   [KONG GATEWAY] │
                    │  • JWT Validation │
                    │  • Rate Limiting   │
                    │  • Correlation ID  │
                    └────────┬────────┘
                             │
                    ┌────────▼────────┐
                    │  [ERP API HUB]   │
                    │  .NET Core 8     │
                    │  Service         │
                    └────────┬────────┘
                             │
        ┌────────────────────┼────────────────────┐
        │                    │                    │
┌───────▼────────┐  ┌──────▼──────┐  ┌────────▼──────┐
│   [PostgreSQL]  │  │  [Redis]    │  │ [RabbitMQ]    │
│  Audit Logs      │  │  • Cache    │  │  Async Queue  │
│  Config          │  │  • Rate Lim │  │  • Events     │
│  System Registry │  │  • Sessions │  │  • Retry      │
└──────────────────┘  └─────────────┘  └───────────────┘
                             │
                    ┌────────▼────────┐
                    │  [ERPNEXT v15]   │
                    │  • REST API      │
                    │  • MariaDB 10.6  │
                    │  • Redis Queue   │
                    └──────────────────┘
```

### 2.2 Component Interaction

| Component | Technology | Responsibility |
|-----------|-----------|---------------|
| Kong Gateway | Kong v3.x (DB-less) | Edge proxy, JWT validation, rate limiting, routing |
| ERP API Hub | .NET Core 8 | Business logic, transformation, orchestration |
| PostgreSQL | PostgreSQL 16 | Audit logs, system registry, configuration |
| Redis | Redis 7.x | Response cache, rate limit counters, sessions |
| RabbitMQ | RabbitMQ 3.x | Async ingestion queues, event bus, retry/DLQ |
| ERPNext | ERPNext v15 | Core ERP system, REST API, business data |

### 2.3 Data Flow Summary

**Ingestion Flow (External → ERP):**
```
External System → Kong → API Hub → Validate → Transform → Queue (RabbitMQ) → ERPNext API
```

**Query Flow (External ← ERP):**
```
External System → Kong → API Hub → Cache Check → ERPNext API → Cache Store → Response
```

**Webhook Flow (ERP → External):**
```
ERPNext Event → RabbitMQ → API Hub → Webhook Dispatcher → External System
```

---

## 3. Authentication & Authorization Module

### 3.1 Overview
The API Hub acts as an **authentication proxy** between external systems and ERPNext. External systems authenticate with Keycloak; the API Hub translates tokens to ERPNext API keys.

### 3.2 Functional Requirements

#### FR-AUTH-001: Keycloak Token Validation
- **Description:** The API Hub shall validate JWT tokens issued by Keycloak shared realm.
- **Input:** Authorization: Bearer {jwt_token} header
- **Process:**
  1. Extract token from header
  2. Validate signature using Keycloak JWKS endpoint
  3. Validate token expiry (exp claim)
  4. Validate issuer (iss claim matches Keycloak realm)
  5. Validate audience (aud claim includes api-hub-client)
- **Output:** Decoded token with user claims or 401 Unauthorized

#### FR-AUTH-002: ERPNext API Key Mapping
- **Description:** After Keycloak validation, map the user to an ERPNext API key.
- **Process:**
  1. Extract user ID from Keycloak token (sub claim)
  2. Look up user in PostgreSQL mapping table: keycloak_user_id → erpnext_api_key
  3. If not found, return 403 Forbidden with message "ERP access not configured"
  4. Retrieve encrypted API secret from database
  5. Decrypt using AWS KMS / HashiCorp Vault (or local master key)
  6. Include API key/secret in ERPNext API calls
- **Security:** API secrets encrypted at rest using AES-256-GCM

#### FR-AUTH-003: Role-Based Access Control (RBAC)
- **Description:** Enforce permissions based on Keycloak realm roles.
- **Roles:**
  - `api-hub:read` — Query access
  - `api-hub:write` — Ingestion access
  - `api-hub:admin` — Configuration access
  - `api-hub:webhook` — Webhook subscription management
- **Process:**
  1. Extract roles from JWT token (realm_access.roles)
  2. Check required roles for requested endpoint
  3. Return 403 if insufficient privileges
- **Endpoint Mapping:**
  - GET /api/v1/query/* → requires `api-hub:read`
  - POST /api/v1/ingest/* → requires `api-hub:write`
  - POST /api/v1/webhooks/* → requires `api-hub:webhook`
  - POST /api/v1/systems/* → requires `api-hub:admin`

#### FR-AUTH-004: Multi-Tenant Tenant Context
- **Description:** All API calls must include tenant (site) context.
- **Tenant Identification:**
  - **Option 1:** Header `X-Tenant-ID: frontend` (default)
  - **Option 2:** JWT claim `tenant_id`
  - **Option 3:** API key prefix (e.g., `frontend_abc123`)
- **Validation:**
  1. Extract tenant identifier
  2. Verify tenant exists in ERPNext sites directory
  3. Include `X-Frappe-Site-Name` header in ERPNext API calls
  4. Reject with 400 Bad Request if tenant invalid

#### FR-AUTH-005: Token Refresh Logic
- **Description:** Support refresh token flow for long-lived sessions.
- **Process:**
  1. When access token expires, return 401 with `error: "token_expired"`
  2. Client calls `/api/v1/auth/refresh` with refresh token
  3. API Hub validates refresh token with Keycloak
  4. Returns new access token + refresh token pair
- **Security:** Refresh tokens rotated on each use, stored hashed in Redis

### 3.3 API Endpoints — Authentication

| Method | Endpoint | Description | Auth Required |
|--------|----------|-------------|---------------|
| POST | /api/v1/auth/token | Exchange Keycloak token for API Hub session | Yes (Keycloak) |
| POST | /api/v1/auth/refresh | Refresh access token | Yes (Refresh Token) |
| POST | /api/v1/auth/revoke | Revoke session | Yes (Access Token) |
| GET | /api/v1/auth/verify | Verify token validity | Yes (Access Token) |

---

## 4. Ingestion Module

### 4.1 Overview
Handles all data flowing from external systems into ERPNext. Supports both synchronous (immediate) and asynchronous (queued) processing.

### 4.2 Functional Requirements

#### FR-ING-001: External System Registration
- **Description:** Before ingestion, external systems must be registered.
- **Fields:**
  - `system_id` (UUID, primary key)
  - `system_name` (e.g., "salesforce-crm", "website-ecommerce")
  - `system_type` (enum: CRM, ECOMMERCE, HR, PAYROLL, SUPPLIER, BI, OTHER)
  - `webhook_url` (optional, for async callbacks)
  - `schema_version` (e.g., "v1.0")
  - `is_active` (boolean)
  - `rate_limit_tier` (enum: TIER_1, TIER_2, TIER_3)
  - `created_at`, `updated_at`
- **API:** POST /api/v1/systems (admin only)

#### FR-ING-002: Schema Validation
- **Description:** All ingestion payloads validated against JSON Schema.
- **Validation Rules:**
  - Required fields presence
  - Data type correctness (string, number, date, enum)
  - Field length limits
  - Regex patterns (email, phone, etc.)
  - Cross-field validation (e.g., end_date > start_date)
- **Error Response:** 400 Bad Request with detailed validation errors per field

#### FR-ING-003: Data Transformation & Mapping
- **Description:** Transform external data format to ERPNext doctype format.
- **Mapping Types:**
  - **Direct:** `external.customer_name` → `Customer.customer_name`
  - **Calculated:** `external.price + external.tax` → `SalesOrder.total`
  - **Lookup:** `external.country_code` → `Address.country` (via reference table)
  - **Constant:** Static value assigned to ERPNext field
  - **Conditional:** IF/ELSE logic based on external values
- **Configuration:** Stored in PostgreSQL as JSON mapping rules

#### FR-ING-004: ERPNext CRUD Operations Mapping
- **Description:** Map ingestion requests to ERPNext REST API calls.
- **Operations:**
  - `CREATE` → POST /api/resource/{Doctype}
  - `UPDATE` → PUT /api/resource/{Doctype}/{name}
  - `DELETE` → DELETE /api/resource/{Doctype}/{name}
  - `UPSERT` → Check existence → CREATE or UPDATE

#### FR-ING-005: Async Processing via RabbitMQ
- **Description:** Non-blocking ingestion using message queues.
- **Queue Design:**
  - `ingestion.default` — General ingestion messages
  - `ingestion.high` — High priority (e.g., live bookings)
  - `ingestion.low` — Low priority (e.g., bulk data sync)
- **Message Format:**
  ```json
  {
    "message_id": "uuid",
    "system_id": "salesforce-crm",
    "operation": "CREATE",
    "doctype": "Customer",
    "payload": {...},
    "idempotency_key": "uuid",
    "tenant_id": "frontend",
    "timestamp": "2026-05-26T12:00:00Z",
    "retry_count": 0
  }
  ```

#### FR-ING-006: Retry Mechanism
- **Description:** Automatic retry for failed operations.
- **Retry Policy:**
  - Max retries: 5
  - Backoff: Exponential (2^n seconds: 2s, 4s, 8s, 16s, 32s)
  - Jitter: ±25% randomization
  - Retryable errors: 5xx, timeout, network error
  - Non-retryable: 4xx (client errors), schema validation failure

#### FR-ING-007: Dead Letter Queue (DLQ)
- **Description:** Store messages that exhaust retry attempts.
- **DLQ Structure:**
  - Queue: `ingestion.dlq`
  - Retention: 30 days
  - Fields: original message, failure reason, final error, timestamp
- **DLQ Management:**
  - API to list DLQ messages: GET /api/v1/ingest/dlq
  - API to replay DLQ message: POST /api/v1/ingest/dlq/{message_id}/replay
  - API to purge DLQ: DELETE /api/v1/ingest/dlq (admin only)
  - Alert when DLQ depth > 100 messages

#### FR-ING-008: Batch Ingestion
- **Description:** Support bulk operations for efficiency.
- **Batch Format:**
  ```json
  {
    "batch_id": "uuid",
    "operations": [
      {"operation": "CREATE", "doctype": "Customer", "payload": {...}},
      {"operation": "CREATE", "doctype": "Customer", "payload": {...}}
    ]
  }
  ```
- **Limits:** Max 1000 operations per batch, max 10MB payload
- **Response:** Individual success/failure per operation

#### FR-ING-009: Idempotency Keys
- **Description:** Prevent duplicate processing.
- **Mechanism:**
  - Client provides `Idempotency-Key: {uuid}` header
  - API Hub stores key in Redis (TTL: 24 hours)
  - Duplicate key detected → return cached response (201 for create, 200 for update)
  - No key provided → operation proceeds without idempotency guarantee

### 4.3 API Endpoints — Ingestion

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | /api/v1/ingest/{doctype} | Ingest single document | api-hub:write |
| POST | /api/v1/ingest/batch | Ingest batch documents | api-hub:write |
| POST | /api/v1/ingest/{doctype}/{name} | Update document | api-hub:write |
| DELETE | /api/v1/ingest/{doctype}/{name} | Delete document | api-hub:write |
| GET | /api/v1/ingest/status/{job_id} | Check async job status | api-hub:read |
| GET | /api/v1/ingest/dlq | List dead letter queue | api-hub:admin |
| POST | /api/v1/ingest/dlq/{id}/replay | Replay DLQ message | api-hub:admin |

---

## 5. Query Module

### 5.1 Overview
Handles all data queries from external systems to ERPNext. Optimized with caching and supports complex filtering.

### 5.2 Functional Requirements

#### FR-QRY-001: Proxy Queries to ERPNext
- **Description:** Forward queries to ERPNext REST API and return results.
- **Supported Operations:**
  - List documents: GET /api/resource/{Doctype}
  - Get single document: GET /api/resource/{Doctype}/{name}
  - Count: GET /api/resource/{Doctype}?limit_page_length=1&fields=["count(*)"]

#### FR-QRY-002: Response Caching
- **Description:** Cache ERPNext responses to reduce load.
- **Cache Strategy:**
  - Redis cache with TTL (Time To Live)
  - Default TTL: 5 minutes for list queries, 1 minute for single document
  - Cache key: `query:{tenant}:{doctype}:{hash_of_params}`
  - Cache invalidation: Webhook events or explicit purge
- **Cache Bypass:** Header `Cache-Control: no-cache`

#### FR-QRY-003: Pagination
- **Description:** Standard pagination for list queries.
- **Parameters:**
  - `limit_start` (default: 0)
  - `limit_page_length` (default: 20, max: 100)
  - `page` (alternative to limit_start, 1-based)
- **Response Headers:**
  - `X-Total-Count`: Total matching records
  - `X-Page-Count`: Total pages
  - `X-Current-Page`: Current page number
  - `Link`: RFC 5988 pagination links (first, prev, next, last)

#### FR-QRY-004: Filtering
- **Description:** Filter results by field values.
- **Operators:**
  - `=` (equal)
  - `!=` (not equal)
  - `>` (greater than)
  - `<` (less than)
  - `>=` (greater or equal)
  - `<=` (less or equal)
  - `like` (substring match)
  - `in` (value in list)
  - `between` (range)
  - `is` (null check)
- **Example:** `?filters=[["Customer", "customer_type", "=", "Company"], ["Customer", "creation", ">=", "2026-01-01"]]`

#### FR-QRY-005: Sorting
- **Description:** Order results by fields.
- **Parameters:**
  - `order_by` (e.g., `creation desc`, `customer_name asc`)
  - `order_by` supports multiple fields: `creation desc, modified desc`

#### FR-QRY-006: Field Selection
- **Description:** Return only requested fields (partial response).
- **Parameters:**
  - `fields` (JSON array): `["name", "customer_name", "email_id"]`
  - `fields=["*"]` returns all fields (default behavior)

#### FR-QRY-007: Multi-Tenant Data Isolation
- **Description:** Ensure queries only return data for the authenticated tenant.
- **Implementation:**
  - All queries include `X-Frappe-Site-Name` header
  - ERPNext handles site-level isolation
  - API Hub validates tenant access in token claims

### 5.3 API Endpoints — Query

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | /api/v1/query/{doctype} | List documents | api-hub:read |
| GET | /api/v1/query/{doctype}/{name} | Get single document | api-hub:read |
| GET | /api/v1/query/{doctype}/count | Count documents | api-hub:read |
| DELETE | /api/v1/cache/{doctype} | Purge cache (admin) | api-hub:admin |

---

## 6. Webhook Module

### 6.1 Overview
Enables ERPNext to push event notifications to external systems via HTTP callbacks.

### 6.2 Functional Requirements

#### FR-WHK-001: Subscription Management
- **Description:** External systems register/unregister webhook subscriptions.
- **Subscription Fields:**
  - `subscription_id` (UUID)
  - `system_id` (reference to registered system)
  - `event_types` (array: ["booking_created", "invoice_paid"])
  - `webhook_url` (HTTPS URL)
  - `secret` (HMAC signing secret, encrypted)
  - `is_active` (boolean)
  - `created_at`, `updated_at`
- **Validation:**
  - URL must use HTTPS
  - URL must be reachable (health check on registration)
  - Max 10 subscriptions per system

#### FR-WHK-002: Event Types
- **Description:** Standardized event types for webhook notifications.
- **Supported Events:**
  - `customer_created` — New customer added
  - `customer_updated` — Customer modified
  - `booking_created` — New tour booking
  - `booking_updated` — Booking modified
  - `booking_cancelled` — Booking cancelled
  - `invoice_created` — Sales invoice generated
  - `invoice_paid` — Payment received
  - `payment_entry_created` — Payment recorded
  - `item_price_updated` — Tour/hotel price changed
  - `employee_created` — New employee
  - `salary_slip_submitted` — Payroll processed

#### FR-WHK-003: Delivery Retry
- **Description:** Retry failed webhook deliveries.
- **Retry Policy:**
  - Max attempts: 3
  - Intervals: Immediate, 5 minutes, 15 minutes
  - Exponential backoff: 0s, 5min, 15min
- **Failure Handling:**
  - After 3 failures: mark subscription as `failed`
  - Alert admin via notification
  - Pause delivery until manually resumed

#### FR-WHK-004: Webhook Signature Verification
- **Description:** Sign webhook payloads with HMAC-SHA256.
- **Signature Format:**
  ```
  X-Hub-Signature-256: sha256={hex_digest}
  ```
  - Digest = HMAC-SHA256(secret, payload_body)
- **Client Verification:**
  - Compute HMAC with shared secret
  - Compare with X-Hub-Signature-256 header
  - Reject if mismatch

#### FR-WHK-005: Delivery Status Tracking
- **Description:** Track and query webhook delivery status.
- **Status Values:**
  - `pending` — Queued for delivery
  - `delivering` — In progress
  - `delivered` — HTTP 2xx received
  - `failed` — Max retries exceeded or non-2xx
- **Retention:** Delivery logs kept for 30 days

### 6.3 API Endpoints — Webhooks

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | /api/v1/webhooks/subscriptions | Register webhook | api-hub:webhook |
| GET | /api/v1/webhooks/subscriptions | List subscriptions | api-hub:webhook |
| GET | /api/v1/webhooks/subscriptions/{id} | Get subscription | api-hub:webhook |
| PUT | /api/v1/webhooks/subscriptions/{id} | Update subscription | api-hub:webhook |
| DELETE | /api/v1/webhooks/subscriptions/{id} | Delete subscription | api-hub:webhook |
| GET | /api/v1/webhooks/deliveries | List deliveries | api-hub:webhook |
| POST | /api/v1/webhooks/subscriptions/{id}/test | Test webhook | api-hub:webhook |

---

## 7. Audit & Logging Module

### 7.1 Overview
Comprehensive logging of all API Hub activities for security, compliance, and troubleshooting.

### 7.2 Functional Requirements

#### FR-AUD-001: API Call Logging
- **Description:** Log every API call with full context.
- **Log Fields:**
  - `log_id` (UUID)
  - `timestamp` (ISO 8601 with timezone)
  - `request_id` (correlation ID from Kong)
  - `tenant_id` (site identifier)
  - `system_id` (external system identifier)
  - `user_id` (Keycloak user ID)
  - `method` (HTTP method)
  - `endpoint` (API endpoint path)
  - `status_code` (HTTP response code)
  - `duration_ms` (request duration)
  - `request_size_bytes`
  - `response_size_bytes`
  - `client_ip`
  - `user_agent`

#### FR-AUD-002: PII Masking
- **Description:** Mask sensitive personal information in logs.
- **Masked Fields:**
  - `password`, `api_secret`, `token` → `***REDACTED***`
  - `email` → `a***@example.com`
  - `phone` → `+84-***-***-789`
  - `id_card`, `passport` → `***********`
  - `bank_account` → `****1234`

#### FR-AUD-003: Audit Log Retention
- **Description:** Retain audit logs according to policy.
- **Retention Tiers:**
  - Hot storage (PostgreSQL): 30 days
  - Warm storage (compressed): 90 days
  - Cold storage (S3/archive): 1 year
- **Auto-archive:** Daily job moves logs older than 30 days to compressed storage

#### FR-AUD-004: Audit Log Search
- **Description:** Search and filter audit logs.
- **Filters:**
  - Date range
  - Tenant
  - System
  - User
  - Endpoint
  - Status code
  - Duration threshold
- **Pagination:** Same as Query module

#### FR-AUD-005: Audit Log Export
- **Description:** Export audit logs for compliance/reporting.
- **Formats:** CSV, JSON, Excel
- **Security:** Export requires `api-hub:admin` role
- **Delivery:** Async export with download link (expires in 24 hours)

### 7.3 API Endpoints — Audit

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| GET | /api/v1/audit/logs | Query audit logs | api-hub:admin |
| POST | /api/v1/audit/export | Export audit logs | api-hub:admin |
| GET | /api/v1/audit/export/{job_id} | Download export | api-hub:admin |

---

## 8. Rate Limiting Module

### 8.1 Overview
Prevent abuse and ensure fair usage across all external systems.

### 8.2 Functional Requirements

#### FR-RLM-001: Per-Consumer Rate Limits
- **Description:** Limit requests per registered external system.
- **Tiers:**
  - **TIER_1 (Premium):** 10,000 req/min, 500,000 req/hour
  - **TIER_2 (Standard):** 1,000 req/min, 50,000 req/hour
  - **TIER_3 (Basic):** 100 req/min, 5,000 req/hour
- **Storage:** Redis counters with TTL (sliding window)

#### FR-RLM-002: Per-Endpoint Rate Limits
- **Description:** Different limits for different endpoint types.
- **Defaults:**
  - Ingestion: 50% of tier limit
  - Query: 100% of tier limit
  - Webhook management: 10% of tier limit
  - Admin endpoints: 5% of tier limit

#### FR-RLM-003: Burst Handling
- **Description:** Allow short bursts above sustained rate.
- **Burst Bucket:** Token bucket algorithm
  - Bucket size: 2x sustained rate
  - Refill rate: sustained rate per second
- **Example:** TIER_2 allows burst of 2,000 requests, then sustained at 1,000/min

#### FR-RLM-004: Throttling Response
- **Description:** Standard response when rate limit exceeded.
- **Response:**
  ```json
  {
    "type": "https://api.hnhtravel.work/errors/rate-limited",
    "title": "Rate Limit Exceeded",
    "status": 429,
    "detail": "Rate limit of 1000 requests per minute exceeded",
    "instance": "/api/v1/ingest/customer",
    "retry_after": 45
  }
  ```
- **Headers:**
  - `X-RateLimit-Limit`: Max requests allowed
  - `X-RateLimit-Remaining`: Remaining requests
  - `X-RateLimit-Reset`: Unix timestamp when limit resets
  - `Retry-After`: Seconds until next request allowed

#### FR-RLM-005: Kong Integration
- **Description:** Leverage Kong's rate-limiting plugin where applicable.
- **Configuration:**
  - Kong handles edge rate limiting (first line of defense)
  - API Hub handles application-level rate limiting (granular control)
  - Sync tier configuration from API Hub to Kong via Admin API

---

## 9. Configuration Module

### 9.1 Overview
Manage external system registrations, mapping rules, and API Hub settings.

### 9.2 Functional Requirements

#### FR-CFG-001: External System Registry
- **Description:** CRUD operations for external systems.
- **Fields:**
  - `system_id` (UUID, PK)
  - `system_name` (unique, slug)
  - `system_type` (enum)
  - `description`
  - `contact_email`
  - `webhook_url`
  - `rate_limit_tier`
  - `is_active`
  - `created_at`, `updated_at`
- **Constraints:**
  - `system_name` unique per tenant
  - `webhook_url` must be HTTPS
  - Max 50 active systems per tenant

#### FR-CFG-002: API Endpoint Mapping Rules
- **Description:** Define how external endpoints map to ERPNext operations.
- **Rule Structure:**
  ```json
  {
    "rule_id": "uuid",
    "system_id": "salesforce-crm",
    "external_endpoint": "/customers",
    "erpnext_doctype": "Customer",
    "operation": "CREATE",
    "mapping_version": "v1.0"
  }
  ```

#### FR-CFG-003: Field Mapping Configuration
- **Description:** Define field-level transformations.
- **Mapping Types:**
  - Direct mapping
  - Calculated (formula)
  - Lookup (reference table)
  - Constant
  - Conditional
- **Example:**
  ```json
  {
    "external_field": "customer_type",
    "erpnext_field": "customer_type",
    "mapping_type": "lookup",
    "lookup_table": {
      "enterprise": "Company",
      "individual": "Individual"
    }
  }
  ```

#### FR-CFG-004: Transformation Rules
- **Description:** Complex transformations using scripting.
- **Supported:**
  - JSONPath expressions
  - JavaScript/TypeScript functions (sandboxed)
  - Template strings (Handlebars-like)
- **Security:**
  - Scripts run in isolated VM
  - Max execution time: 500ms
  - No filesystem/network access

### 9.3 API Endpoints — Configuration

| Method | Endpoint | Description | Auth |
|--------|----------|-------------|------|
| POST | /api/v1/systems | Register external system | api-hub:admin |
| GET | /api/v1/systems | List systems | api-hub:admin |
| GET | /api/v1/systems/{id} | Get system | api-hub:admin |
| PUT | /api/v1/systems/{id} | Update system | api-hub:admin |
| DELETE | /api/v1/systems/{id} | Delete system | api-hub:admin |
| POST | /api/v1/systems/{id}/mappings | Create mapping | api-hub:admin |
| GET | /api/v1/systems/{id}/mappings | List mappings | api-hub:admin |
| PUT | /api/v1/mappings/{id} | Update mapping | api-hub:admin |
| DELETE | /api/v1/mappings/{id} | Delete mapping | api-hub:admin |

---

## 10. API Specifications

### 10.1 OpenAPI Specification

The complete API specification is available at:
- **Swagger UI:** https://api.hnhtravel.work/swagger
- **OpenAPI JSON:** https://api.hnhtravel.work/openapi.json

### 10.2 Authentication Headers

| Header | Required | Description |
|--------|----------|-------------|
| Authorization | Yes | `Bearer {keycloak_jwt_token}` |
| X-Tenant-ID | Yes | Tenant/site identifier (default: `frontend`) |
| X-Idempotency-Key | No | UUID for idempotent operations |
| X-API-Version | No | API version (default: `v1`) |
| Accept | No | `application/json` (default) |
| Content-Type | Yes (POST/PUT) | `application/json` |

### 10.3 Standard Response Format

**Success (200-299):**
```json
{
  "data": { ... },
  "meta": {
    "request_id": "uuid",
    "timestamp": "2026-05-26T12:00:00Z",
    "duration_ms": 45
  }
}
```

**Error (400-599):** RFC 7807 Problem Details
```json
{
  "type": "https://api.hnhtravel.work/errors/{error-code}",
  "title": "Human-readable title",
  "status": 400,
  "detail": "Detailed error message",
  "instance": "/api/v1/ingest/customer",
  "errors": [
    {
      "field": "customer_name",
      "message": "Field is required",
      "code": "required"
    }
  ]
}
```

### 10.4 ERPNext Doctype Examples

#### Customer Doctype
```json
{
  "name": "CUST-2026-00001",
  "customer_name": "Công ty TNHH ABC Travel",
  "customer_type": "Company",
  "customer_group": "Tour Operator",
  "territory": "Vietnam",
  "email_id": "contact@abctravel.vn",
  "mobile_no": "+84-912-345-678",
  "tax_id": "0101234567",
  "website": "https://abctravel.vn"
}
```

#### Sales Order Doctype
```json
{
  "name": "SO-2026-00001",
  "customer": "CUST-2026-00001",
  "transaction_date": "2026-05-26",
  "delivery_date": "2026-06-15",
  "items": [
    {
      "item_code": "TOUR-HN-HL-001",
      "item_name": "Tour Hà Nội - Hạ Long 3N2Đ",
      "qty": 2,
      "rate": 3500000,
      "amount": 7000000
    }
  ],
  "total": 7000000,
  "currency": "VND"
}
```

#### Booking (Custom Doctype)
```json
{
  "name": "BOOK-2026-00001",
  "customer": "CUST-2026-00001",
  "tour_code": "TOUR-HN-HL-001",
  "booking_date": "2026-05-26",
  "travel_date": "2026-06-15",
  "number_of_pax": 2,
  "status": "Confirmed",
  "sales_person": "EMP-0001",
  "total_amount": 7000000,
  "paid_amount": 3500000,
  "balance_due": 3500000
}
```

---

## 11. Data Flow Diagrams

### 11.1 Ingestion Flow

```
External System                    API Hub                          ERPNext
     │                              │                                 │
     │ POST /api/v1/ingest/customer │                                 │
     │ {customer data}               │                                 │
     │─────────────────────────────>│                                 │
     │                              │ 1. Validate JWT (Keycloak)      │
     │                              │ 2. Validate tenant              │
     │                              │ 3. Validate schema              │
     │                              │ 4. Check idempotency key        │
     │                              │ 5. Transform data               │
     │                              │ 6. Queue to RabbitMQ            │
     │                              │                                 │
     │ 202 Accepted                 │                                 │
     │ {job_id: "uuid"}             │                                 │
     │<─────────────────────────────│                                 │
     │                              │                                 │
     │                              │ Consumer dequeues message       │
     │                              │────────────────────────────────>│
     │                              │ POST /api/resource/Customer   │
     │                              │ {transformed data}             │
     │                              │<────────────────────────────────│
     │                              │ 201 Created                    │
     │                              │                                 │
     │                              │ 7. Log audit                    │
     │                              │ 8. Update job status            │
     │                              │                                 │
     │ GET /api/v1/ingest/status/uuid                               │
     │<─────────────────────────────│                                 │
     │ {status: "completed"}        │                                 │
```

### 11.2 Query Flow

```
External System                    API Hub           Redis        ERPNext
     │                              │                │              │
     │ GET /api/v1/query/customer │                │              │
     │ ?filters=...                 │                │              │
     │─────────────────────────────>│                │              │
     │                              │ 1. Check cache │              │
     │                              │────────────────>│              │
     │                              │ Cache miss     │              │
     │                              │<────────────────│              │
     │                              │                │              │
     │                              │ 2. Query ERPNext            │
     │                              │─────────────────────────────>│
     │                              │ GET /api/resource/Customer │
     │                              │<─────────────────────────────│
     │                              │                │              │
     │                              │ 3. Store cache │              │
     │                              │────────────────>│              │
     │                              │                │              │
     │ 200 OK                       │                │              │
     │ {customers: [...]}           │                │              │
     │<─────────────────────────────│                │              │
```

### 11.3 Webhook Flow

```
ERPNext                    RabbitMQ                 API Hub              External
   │                          │                        │                   System
   │ 1. Document created      │                        │                   │
   │ (Hook triggered)         │                        │                   │
   │─────────────────────────>│                        │                   │
   │ POST to webhook endpoint │                        │                   │
   │                          │ 2. Queue event         │                   │
   │                          │───────────────────────>│                   │
   │                          │                        │ 3. Get subscribers│
   │                          │                        │ 4. Dispatch       │
   │                          │                        │ POST {webhook_url}│
   │                          │                        │──────────────────>│
   │                          │                        │                   │
   │                          │                        │ 5. Track delivery │
   │                          │                        │<──────────────────│
   │                          │                        │ 200 OK or retry   │
```

### 11.4 Error Handling Flow

```
External System                    API Hub                          Result
     │                              │                                 │
     │ Request                      │                                 │
     │─────────────────────────────>│                                 │
     │                              │                                 │
     │                              │ Validation Error (400)         │
     │                              │────────────────────────────────>│
     │ 400 Bad Request              │                                 │
     │ {validation details}        │                                 │
     │<─────────────────────────────│                                 │
     │                              │                                 │
     │                              │ Auth Error (401/403)           │
     │                              │────────────────────────────────>│
     │ 401/403                      │                                 │
     │<─────────────────────────────│                                 │
     │                              │                                 │
     │                              │ ERPNext Error (5xx)            │
     │                              │ Retry via RabbitMQ             │
     │                              │────────────────────────────────>│
     │                              │                                 │
     │                              │ Max retries exceeded           │
     │                              │ Move to DLQ                    │
     │                              │ Alert admin                    │
     │                              │────────────────────────────────>│
```

---

## 12. Error Handling

### 12.1 Error Categories

| Category | HTTP Status | Description | Examples |
|----------|------------|-------------|----------|
| Validation | 400 | Request data invalid | Missing fields, wrong format |
| Authentication | 401 | Missing/invalid token | Expired JWT, wrong signature |
| Authorization | 403 | Insufficient permissions | Missing role, wrong tenant |
| Not Found | 404 | Resource doesn't exist | Invalid doctype, deleted record |
| Conflict | 409 | Resource conflict | Duplicate key, concurrent update |
| Rate Limited | 429 | Too many requests | Exceeded tier limit |
| ERPNext Error | 502 | ERPNext returned error | Database constraint, business rule |
| ERPNext Timeout | 504 | ERPNext unavailable | Network issue, overload |
| Internal | 500 | Unexpected error | Bug, unhandled exception |

### 12.2 RFC 7807 Problem Details

All errors follow RFC 7807 format:

```json
{
  "type": "https://api.hnhtravel.work/errors/validation-error",
  "title": "Validation Failed",
  "status": 400,
  "detail": "The request body contains validation errors",
  "instance": "/api/v1/ingest/customer",
  "errors": [
    {
      "field": "customer_name",
      "message": "Customer name is required",
      "code": "required"
    },
    {
      "field": "email_id",
      "message": "Invalid email format",
      "code": "format"
    }
  ],
  "request_id": "req-uuid",
  "timestamp": "2026-05-26T12:00:00Z"
}
```

### 12.3 Retry Strategies

| Error Type | Retry | Strategy | Max Attempts |
|-----------|-------|----------|--------------|
| 400 Bad Request | No | — | 0 |
| 401 Unauthorized | No | — | 0 |
| 403 Forbidden | No | — | 0 |
| 404 Not Found | No | — | 0 |
| 409 Conflict | Yes | Immediate, then 1s, 5s | 3 |
| 429 Rate Limited | Yes | After Retry-After header | 5 |
| 500 Internal | Yes | Exponential backoff | 5 |
| 502 Bad Gateway | Yes | Exponential backoff | 5 |
| 503 Service Unavailable | Yes | Exponential backoff | 5 |
| 504 Gateway Timeout | Yes | Exponential backoff | 5 |
| Network Error | Yes | Exponential backoff | 5 |

### 12.4 Alerting Thresholds

| Alert | Threshold | Severity | Channel |
|-------|-----------|----------|---------|
| High error rate | > 5% in 5 minutes | Warning | Slack/Email |
| ERPNext down | > 3 failures in 1 minute | Critical | PagerDuty + Slack |
| DLQ depth | > 100 messages | Warning | Slack |
| Rate limit hit | > 10% of requests | Info | Dashboard |
| Response time | P95 > 2 seconds | Warning | Slack |

---

## 13. Security Requirements

### 13.1 Transport Security
- **TLS:** Minimum TLS 1.3 for all communications
- **HSTS:** HTTP Strict Transport Security enabled
- **Certificate:** Valid SSL certificate from trusted CA
- **Cipher suites:** Only strong ciphers (no RC4, DES, 3DES, MD5)

### 13.2 Authentication Security
- **Token storage:** Never log JWT tokens
- **Token expiry:** Access token TTL: 15 minutes; Refresh token TTL: 7 days
- **Token rotation:** Refresh tokens rotated on each use
- **Revocation:** Immediate token revocation via Keycloak admin API

### 13.3 Input Validation
- **SQL Injection:** Parameterized queries only (ORM)
- **XSS Prevention:** Output encoding for all user-generated content
- **Command Injection:** No shell execution with user input
- **Path Traversal:** Validate and sanitize file paths
- **Content-Type:** Strict Content-Type validation

### 13.4 API Key Security
- **Encryption:** AES-256-GCM for API secrets at rest
- **Key rotation:** Support manual rotation; auto-rotation optional
- **Storage:** PostgreSQL with encrypted column
- **Access:** Only API Hub backend can decrypt; no admin access

### 13.5 Audit Security
- **Tamper-proof:** Audit logs append-only, signed with HMAC
- **Access control:** Audit logs only accessible by api-hub:admin role
- **Export encryption:** Exported logs encrypted with PGP

---

## 14. Performance Requirements

### 14.1 Response Time SLAs

| Endpoint Type | P50 | P95 | P99 |
|--------------|-----|-----|-----|
| Authentication | < 50ms | < 100ms | < 200ms |
| Cached Query | < 20ms | < 50ms | < 100ms |
| Uncached Query | < 200ms | < 500ms | < 1s |
| Sync Ingestion | < 500ms | < 1s | < 2s |
| Async Ingestion (accept) | < 50ms | < 100ms | < 200ms |
| Webhook dispatch | < 100ms | < 500ms | < 1s |

### 14.2 Throughput Requirements

| Metric | Target | Peak |
|--------|--------|------|
| Requests per second | 1,000 | 3,000 |
| Concurrent connections | 500 | 1,000 |
| Ingestion messages/min | 10,000 | 30,000 |
| Webhook deliveries/min | 5,000 | 15,000 |

### 14.3 Cache Targets

| Metric | Target |
|--------|--------|
| Cache hit rate (queries) | > 80% |
| Cache hit rate (auth) | > 95% |
| Cache invalidation latency | < 1s |

---

## 15. Non-Functional Requirements

### 15.1 Scalability
- **Horizontal scaling:** API Hub stateless, supports multiple instances
- **Database:** PostgreSQL read replicas for query load
- **Cache:** Redis Cluster for distributed caching
- **Queue:** RabbitMQ clustering for high availability
- **Auto-scaling:** CPU/memory-based pod scaling (Kubernetes)

### 15.2 Availability
- **Target:** 99.9% uptime (8.76 hours downtime/year)
- **Architecture:** Multi-instance with load balancer
- **Health checks:** /health and /ready endpoints
- **Graceful degradation:** Cache-only mode if ERPNext unavailable
- **Disaster recovery:** RPO < 5 minutes, RTO < 30 minutes

### 15.3 Maintainability
- **Feature toggles:** All features behind toggle flags
- **Configuration:** Externalized configuration (environment variables, config service)
- **Logging:** Structured JSON logging
- **Documentation:** OpenAPI/Swagger auto-generated
- **Versioning:** URL versioning (/api/v1/, /api/v2/)

### 15.4 Observability

#### Metrics (Prometheus)
- `api_hub_requests_total` — Counter, labeled by endpoint, method, status
- `api_hub_request_duration_seconds` — Histogram, labeled by endpoint
- `api_hub_cache_hit_ratio` — Gauge
- `api_hub_erpnext_errors_total` — Counter, labeled by error type
- `api_hub_queue_depth` — Gauge, labeled by queue name
- `api_hub_webhook_deliveries_total` — Counter, labeled by status

#### Tracing (OpenTelemetry/Jaeger)
- Distributed tracing across API Hub → ERPNext → Database
- Trace ID propagation via Kong correlation ID
- Span tags: tenant_id, system_id, operation, doctype

#### Logging (ELK Stack)
- Structured JSON logs
- Correlation ID tracking
- Log levels: ERROR, WARN, INFO, DEBUG
- Retention: Hot 7 days, Warm 30 days, Cold 90 days

---

## Appendix A: Data Models

### A.1 External System Registry

```sql
CREATE TABLE external_systems (
    system_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    system_name VARCHAR(100) NOT NULL,
    system_type VARCHAR(50) NOT NULL,
    description TEXT,
    contact_email VARCHAR(255),
    webhook_url VARCHAR(500),
    rate_limit_tier VARCHAR(20) DEFAULT 'TIER_2',
    is_active BOOLEAN DEFAULT true,
    tenant_id VARCHAR(100) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    UNIQUE(system_name, tenant_id)
);
```

### A.2 API Call Audit Log

```sql
CREATE TABLE audit_logs (
    log_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    request_id VARCHAR(100),
    tenant_id VARCHAR(100) NOT NULL,
    system_id UUID REFERENCES external_systems(system_id),
    user_id VARCHAR(255),
    method VARCHAR(10) NOT NULL,
    endpoint VARCHAR(500) NOT NULL,
    status_code INTEGER,
    duration_ms INTEGER,
    request_size_bytes INTEGER,
    response_size_bytes INTEGER,
    client_ip INET,
    user_agent TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);
```

### A.3 Webhook Subscriptions

```sql
CREATE TABLE webhook_subscriptions (
    subscription_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    system_id UUID NOT NULL REFERENCES external_systems(system_id),
    event_types TEXT[] NOT NULL,
    webhook_url VARCHAR(500) NOT NULL,
    secret_encrypted BYTEA,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);
```

---

## Appendix B: Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0-Draft | 2026-05-26 | HNH Tech Team | Initial draft |

## Appendix C: Approval

| Role | Name | Signature | Date |
|------|------|-----------|------|
| Product Owner | | | |
| Technical Lead | | | |
| Security Officer | | | |
| QA Lead | | | |

---

**End of Document**
