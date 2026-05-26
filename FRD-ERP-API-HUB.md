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
13. [Vietnam Compliance Requirements](#13-vietnam-compliance-requirements)
14. [Security Requirements](#14-security-requirements)
15. [Performance Requirements](#15-performance-requirements)
16. [Non-Functional Requirements](#16-non-functional-requirements)
17. [Solution Approach — Ý tưởng giải pháp](#17-solution-approach--ý-tưởng-giải-pháp)

---

## 1. Introduction

### 1.1 Purpose
This FRD defines the functional requirements for the ERP API Hub — a standalone integration service that acts as the **sole gateway** between HNH Travel's business systems and the **ERPNext v15 Core Master Data** system.

**Core Paradigm:** ERPNext operates as the **Core Master Data** hub of HNH Travel:
- **Inbound (Push):** Business systems push transactional/master data into ERP via API Hub → ERP stores as single source of truth
- **Outbound (Pull):** Business systems query ERP via API Hub → retrieve data originated from other systems
- This means: CRM data, ticketing data, payment data — all flow **into** ERP for consolidation, and can be **queried back** by any authorized system

### 1.2 Scope
The ERP API Hub provides:
- **Inbound data ingestion (Push):** Business systems push data → API Hub → ERP stores as Core Master Data
- **Outbound data queries (Pull):** Business systems query → API Hub → ERP returns consolidated data from other systems
- **Event-driven webhooks:** ERP notifies external systems of data changes
- **Cross-cutting concerns**: authentication, rate limiting, audit logging, retry mechanisms

### 1.3 Definitions

| Term | Definition |
|------|-----------|
| Term | Definition |
|------|-----------|
| API Hub | The standalone integration service described in this document |
| Core Master Data | ERPNext as the single source of truth — all business systems push data into ERP, and query data from other systems via ERP |
| Doctype | ERPNext's document type system (equivalent to database tables) |
| ERPNext | The open-source ERP system (v15) powering HNH Travel |
| External System | Any business system (CRM, Ticketing, Payment, etc.) connecting to the API Hub |
| Frappe | The Python web framework underlying ERPNext |
| Ingestion (Push) | Process of pushing transactional/master data from business systems into ERP for storage as Core Master Data |
| Query (Pull) | Process of retrieving consolidated data from ERP — data that may have originated from other business systems |
| Idempotency Key | Unique identifier preventing duplicate processing |
| Tenant | A business branch/site within the multi-tenant ERP |

### 1.4 References

- BRD-ERP-API-HUB-v1.0
- **HNH-TDD-SA-002 v1.0** — HNH Travel Workspace System Architecture Document
- ERPNext REST API Documentation: https://frappeframework.com/docs/user/en/api/rest
- Keycloak Documentation: https://www.keycloak.org/documentation
- Kong API Gateway Documentation: https://docs.konghq.com/
- YARP API Gateway: https://microsoft.github.io/reverse-proxy/

---

## 2. System Overview

### 2.1 Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           HNH TRAVEL SYSTEM ECOSYSTEM                         │
│                                                                               │
│   ┌──────────────┐      ┌──────────────┐      ┌──────────────────────────┐   │
│   │   External   │      │   External   │      │      External Systems     │   │
│   │   Systems    │      │   Systems    │      │   (CRM, E-commerce, ...)  │   │
│   │  (Website)   │      │  (Partner)   │      │                           │   │
│   └──────┬───────┘      └──────┬───────┘      └─────────────┬─────────────┘   │
│          │                      │                            │                │
│          └──────────────────────┼────────────────────────────┘                │
│                                 │                                             │
│                          ┌──────▼──────┐                                      │
│    Kong (Public:8000)  ←→│ YARP API Gateway │  ← Internal Workspace                 │
│                          │   3.x       │     JWT Validation                     │
│                          │ DB-less     │     Rate Limiting                      │
│                          └──────┬──────┘                                      │
│                                 │                                             │
│          ┌──────────────────────┼──────────────────────┐                       │
│          │                      │                      │                       │
│   ┌──────▼──────┐      ┌─────▼─────┐      ┌──────▼──────┐                │
│   │   Workspace   │      │  ERP API  │      │   Module    │                │
│   │   Services    │      │   Hub     │      │    FIN      │                │
│   │ (.NET Core)   │      │(.NET Core│      │  (Độc lập)  │                │
│   │               │      │    8)     │      │             │                │
│   │ /api/core/*   │      │/api/erp/* │      │ /api/fin/*  │                │
│   │ /api/crm/*    │      │           │      │             │                │
│   │ /api/ticketing│      │           │      │             │                │
│   └──────┬────────┘      └─────┬─────┘      └──────┬──────┘                │
│          │                      │                     │                        │
│          │              ┌───────┴───────┐            │                        │
│          │              │               │            │                        │
│   ┌──────▼──────┐ ┌────▼─────┐  ┌────▼─────┐ ┌────▼─────┐                 │
│   │ PostgreSQL  │ │ RabbitMQ │  │  Redis   │ │ERPNext v15│                 │
│   │  (Shared)  │ │(Shared/  │  │ (Shared) │ │ (Internal│                 │
│   │             │ │ Separate)│  │          │ │  API)    │                 │
│   └─────────────┘ └─────────┘  └──────────┘ └──────────┘                  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 Component Interaction

| Component | Technology | Responsibility | Shared? |
|-----------|-----------|---------------|---------|
| Kong API Gateway | Kong 3.x (DB-less) | Public-facing API gateway cho ERP API Hub: JWT, rate limiting, API key auth, transformation | ✅ New |
| YARP API Gateway | YARP (.NET 9) | Internal gateway cho 1StopShop: JWT, routing, branch_id injection | ✅ Shared với Workspace |
| ERP API Hub | .NET Core 8 | Business logic, transformation, orchestration | ❌ Dedicated |
| PostgreSQL | PostgreSQL 18 | Audit logs, system registry, config | ✅ Shared (DB riêng: `erphub_api_db`) |
| Redis | Redis 7.x | Response cache, rate limit counters, sessions | ✅ Shared (prefix: `erphub:`) |
| RabbitMQ | RabbitMQ 3.12 | Async ingestion, event bus, retry/DLQ | ✅ Shared (exchange riêng: `1stopshop_event_bus`) |
| ERPNext | ERPNext v15 | Core ERP, REST API, business data | ❌ Dedicated (internal) |

### 2.3 Integration với HNH Travel Workspace

| Hướng | Chi tiết |
|-------|---------|
| **Workspace → ERP** | Không trực tiếp. Workspace services gọi ERP qua API Hub nếu cần (tương lai). |
| **ERP → Workspace** | Không trực tiếp. ERP events qua webhook có thể trigger workspace actions (tương lai). |
| **Shared Infra** | Kong (public), YARP (internal), PostgreSQL, Redis, RabbitMQ, Keycloak — dùng chung, isolate qua routing/database/prefix. |
| **Module FIN** | Độc lập. API Hub không tương tác với FIN. |

### 2.4 Data Flow Summary

**Ingestion Flow (External → ERP):**
```
External System → Kong (:8000) → API Hub → Validate → Transform 
→ Queue (RabbitMQ: 1stopshop_event_bus) → Worker → ERPNext REST API → MariaDB
```

**Query Flow (External ← ERP):**
```
External System → Kong (:8000) → API Hub → Cache Check 
→ ERPNext API → Cache Store → Response
```

**Webhook Flow (ERP → External):**
```
ERPNext Document Event → Frappe Server Script → API Hub Internal (/internal/erp/events) 
→ RabbitMQ (1stopshop_event_bus) → Webhook Dispatcher → External System
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
- **Tenant Resolution Service (FR-AUTH-004b):**
  - API Hub maintains a tenant registry in PostgreSQL:
    ```sql
    CREATE TABLE tenant_registry (
      tenant_id VARCHAR(50) PRIMARY KEY,
      site_name VARCHAR(100) NOT NULL,       -- e.g., "frontend"
      erpnext_host VARCHAR(255) NOT NULL,    -- e.g., "erpnext-frontend:8080"
      erpnext_api_key_encrypted BYTEA,
      erpnext_api_secret_encrypted BYTEA,
      is_active BOOLEAN DEFAULT true,
      health_status VARCHAR(20),             -- healthy, degraded, down
      last_health_check TIMESTAMP,
      created_at TIMESTAMP DEFAULT NOW()
    );
    ```
  - Resolution flow:
    1. Extract tenant identifier from request
    2. Lookup in `tenant_registry` (cached in Redis, TTL: 1 minute)
    3. Verify tenant `is_active = true`
    4. Validate health status (warn if degraded, reject if down)
    5. Include `X-Frappe-Site-Name: {site_name}` header in ERPNext API calls
    6. Route to `erpnext_host`
  - **Health Check:** Background job pings `/api/method/health` per tenant every 30s
  - **Fallback:** If tenant registry unreachable, use environment variable fallback mapping

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
  - `system_id` (ULID, primary key)
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

#### FR-ING-003b: ERPNext-Specific Transformation Requirements
- **Description:** Handle ERPNext-specific data structures during transformation.
- **Child Table Mapping:**
  - ERPNext uses child tables for line items (e.g., `Sales Order.items`, `Purchase Order.items`)
  - External format: flat array of objects → ERPNext format: `{..., "items": [{"item_code": "...", "qty": 1, ...}]} `
  - Validation: Ensure all required child table fields present
- **Link Field Validation:**
  - ERPNext "Link" fields reference other doctypes (e.g., `customer` in Sales Order references Customer)
  - Before CREATE/UPDATE, validate referenced document exists via `GET /api/resource/{Doctype}/{name}`
  - Return 400 with specific error if Link field references non-existent document
- **Naming Series:**
  - Documents with `autoname` or `naming_series` field will be auto-generated by ERPNext
  - API Hub must NOT provide `name` field for these doctypes during CREATE
  - Exception: If external system requires specific ID mapping, use custom naming series
- **Custom Fields:**
  - ERPNext allows admin-defined custom fields per doctype
  - API Hub must detect and support custom fields dynamically (via `GET /api/resource/DocType/{doctype}`)
  - Custom fields exposed in schema validation and mapping configuration

#### FR-ING-004: ERPNext CRUD Operations Mapping
- **Description:** Map ingestion requests to ERPNext REST API calls.
- **Operations:**
  - `CREATE` → POST /api/resource/{Doctype}
  - `UPDATE` → PUT /api/resource/{Doctype}/{name}
  - `DELETE` → DELETE /api/resource/{Doctype}/{name}
  - `UPSERT` → Check existence → CREATE or UPDATE (with distributed lock, see FR-ING-004b)

#### FR-ING-004b: UPSERT Race Condition Prevention
- **Description:** Prevent duplicate creation during concurrent UPSERT operations.
- **Problem:** ERPNext REST API không hỗ trợ atomic UPSERT. Nếu 2 requests cùng check existence → cùng decide CREATE → cả 2 đều tạo record.
- **Solution:**
  - **Option A (Recommended):** Sử dụng Redis RedLock — acquire lock trên key `upsert:{doctype}:{id}` trước khi check existence. Lock TTL: 5s.
  - **Option B:** Sử dụng idempotency key — client cung cấp `Idempotency-Key` header, API Hub cache response trong 24h. Duplicate key → return cached response.
  - **Option C:** Retry logic với exponential backoff — nếu CREATE fail do uniqueness constraint, retry với UPDATE.
- **Implementation:**
  ```
  1. Generate lock key: "upsert:Customer:CUST-EXT-001"
  2. Acquire RedLock (TTL: 5s)
  3. If lock acquired:
     a. GET /api/resource/Customer/CUST-EXT-001
     b. If exists → PUT update
     c. If not exists → POST create
     d. Release lock
  4. If lock NOT acquired → wait 100ms → retry (max 3 attempts)
  ```

#### FR-ING-005: Async Processing via RabbitMQ
- **Description:** Non-blocking ingestion using message queues.
- **Queue Design:**
  - `ingestion.default` — General ingestion messages
  - `ingestion.high` — High priority (e.g., live bookings)
  - `ingestion.low` — Low priority (e.g., bulk data sync)
- **Message Format:**
  ```json
  {
    "message_id": "ulid",
    "system_id": "salesforce-crm",
    "operation": "CREATE",
    "doctype": "Customer",
    "payload": {...},
    "idempotency_key": "ulid",
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
    "batch_id": "ulid",
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
  - Client provides `Idempotency-Key: {ulid}` header
  - API Hub stores key in Redis (TTL: 24 hours)
  - Duplicate key detected → return cached response (201 for create, 200 for update)
  - No key provided → operation proceeds without idempotency guarantee

#### FR-ING-010: Vietnam Compliance Validation
- **Description:** Validate Vietnamese business rules during ingestion.
- **Tax ID Validation:**
  - Format: 10 digits (individual/công ty) hoặc 13 digits (chi nhánh)
  - Kiểm tra MST qua API của Tổng cục Thuế (nếu có kết nối)
  - Từ chối nếu MST không hợp lệ (trừ trường hợp khách hàng nước ngoài)
- **Invoice Compliance (Thông tư 78/2021/TT-BTC):**
  - Số hóa đơn phải tuân thủ quy tắc nối tiếp không được xóa/bỏ sót
  - Ngày hóa đơn phải khớp với ngày phát hành thực tế
  - Mã hóa đơn theo quy định của Cơ quan thuế
- **E-invoice Integration:**
  - Hóa đơn GTGT phải được đăng ký với hệ thống hóa đơn điện tử
  - API Hub validate dữ liệu trước khi gửi sang ERPNext để tránh lỗi hóa đơn
- **PDPA Compliance (Nghị định 13/2023/NĐ-CP):**
  - Mask PII trong audit logs (§7.2 FR-AUD-002)
  - Consent tracking cho customer data
  - Right to deletion: hỗ trợ xóa dữ liệu cá nhân theo yêu cầu

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

### 6.2 Functional Requirements — Approach: Polling + Server Script (Hybrid)

> **Note:** ERPNext v15 không có native webhook dispatcher. FRD chọn approach lai:
> 1. **Primary:** API Hub poll ERPNext API định kỳ để phát hiện changes (v2 cursor-based)
> 2. **Supplementary:** Frappe Server Script (không sửa core) đẩy critical events vào RabbitMQ
> 3. **Future:** CDC từ MariaDB binlog (nếu cần real-time)

#### FR-WHK-001: Subscription Management
- **Description:** External systems register/unregister webhook subscriptions.
- **Subscription Fields:**
  - `subscription_id` (ULID)
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

#### FR-WHK-001b: ERPNext Server Script Setup
- **Description:** Minimal Frappe Server Script để push critical events.
- **Implementation:**
  - Tạo Server Script trong ERPNext UI: Settings → Server Script → New
  - Script type: "Document Event" (After Insert, After Update, Before Delete)
  - Script gọi API Hub webhook endpoint: POST /internal/v1/events/ingest
  - Payload: `{"event_type": "booking_created", "doctype": "Booking", "name": "BOOK-2026-00001", "timestamp": "..."}`
- **Security:**
  - API Hub endpoint /internal/v1/events chỉ accept từ ERPNext internal IP
  - HMAC signature verification giữa ERPNext và API Hub
  - Script không sửa đổi ERPNext core logic — chỉ gọi external API
- **Fallback:** Nếu Server Script không khả thi, chuyển sang polling mode

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
  - `sales_order_submitted` — Đơn hàng được xác nhận (quy trình tour)
  - `purchase_order_approved` — PO nhà cung cấp được duyệt

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
  - `log_id` (ULID)
  - `timestamp` (ISO 8601 with timezone)
  - `request_id` (correlation ID from Kong/YARP)
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
- **Description:** Leverage Kong's rate-limiting plugin for public-facing API traffic.
- **Configuration:**
  - **Kong (Public):** Edge rate limiting cho external partners (first line of defense). Kong DB-less mode — config qua declarative file (kong.yml). Tier changes require CI/CD pipeline → redeploy Kong.
  - **YARP (Internal):** Internal rate limiting cho 1StopShop services (existing, unchanged).
  - **API Hub:** Application-level rate limiting (granular control per consumer).
- **Kong Plugins (DB-less):**
  - `jwt` — Keycloak RS256 token validation
  - `rate-limiting` — Per-consumer rate limits
  - `acl` — Consumer group → tier mapping
  - `request-transformer` — Header injection (X-Request-ID, X-Consumer-ID)
  - `prometheus` — Metrics export
- **Sync Process:**
  1. Admin cập nhật tier config trong API Hub UI
  2. API Hub generates updated `kong.yml` snippet
  3. Git commit + PR → merge → CI/CD redeploys Kong
  4. Kong reloads declarative config (zero downtime)
- **Fallback:** If Kong rate limit fails, API Hub applies its own rate limiting

---

## 9. Configuration Module

### 9.1 Overview
Manage external system registrations, mapping rules, and API Hub settings.

### 9.2 Functional Requirements

#### FR-CFG-001: External System Registry
- **Description:** CRUD operations for external systems.
- **Fields:**
  - `system_id` (ULID, PK)
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
    "rule_id": "ulid",
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
| X-Idempotency-Key | No | ULID for idempotent operations |
| X-Idempotency-Key | No | ULID for idempotent operations |
| Accept | No | `application/json` (default) |
| Content-Type | Yes (POST/PUT) | `application/json` |

### 10.3 Standard Response Format

**Success (200-299):**
```json
{
  "data": { ... },
  "meta": {
    "request_id": "ulid",
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
> **Status Values:** Phải match ERPNext Select field options. VD: `Draft`, `Confirmed`, `In Progress`, `Completed`, `Cancelled`. Tùy custom doctype configuration.

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
     │ {job_id: "ulid"}             │                                 │
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
     │ GET /api/v1/ingest/status/ulid                               │
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
  "request_id": "req-ulid",
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

## 13. Vietnam Compliance Requirements

### 13.1 Overview
Các yêu cầu tuân thủ pháp luật Việt Nam cho doanh nghiệp du lịch. Xem BRD §6.1 C7.

### 13.2 Tax Compliance (Thông tư 78/2021/TT-BTC)

#### FR-VN-001: Tax ID (MST) Validation
- **Description:** Validate Vietnamese Tax ID format during customer/company ingestion.
- **Rules:**
  - Cá nhân/Công ty: 10 digits (MST 10 số)
  - Chi nhánh: 13 digits (MST 13 số, thêm `-XXX` suffix)
  - Không chứa ký tự đặc biệt, không bắt đầu bằng 0
- **Optional Enhancement:** Tích hợp API tra cứu MST của Tổng cục Thuế để validate tên công ty khớp với MST
- **Foreign Customers:** Cho phép bỏ qua MST validation nếu `country != "Vietnam"`

#### FR-VN-002: Invoice Sequence Compliance
- **Description:** Đảm bảo số hóa đơn tuân thủ quy định nối tiếp.
- **Rules:**
  - Số hóa đơn phải liên tục, không được xóa/bỏ sót (quy định của Cơ quan thuế)
  - API Hub KHÔNG cho phép xóa hóa đơn đã xuất — chỉ cho phép điều chỉnh hoặc lập hóa đơn thay thế
  - Ghi log đầy đủ mọi thao tác liên quan đến hóa đơn (audit trail cho CQT)
- **E-invoice Integration:**
  - Hóa đơn GTGT phải được đăng ký với hệ thống hóa đơn điện tử (nếu HNH sử dụng)
  - API Hub có thể tích hợp API của nhà cung cấp hóa đơn điện tử (nếu applicable)

#### FR-VN-003: Currency and Amount Formatting
- **Description:** Format số tiền theo quy định VN.
- **Rules:**
  - Đơn vị tiền tệ mặc định: VND
  - Số tiền không có phần thập phân (VND là integer)
  - Format: `7,000,000 VND` (không dùng `$` hoặc `USD` trừ khi customer yêu cầu)
  - Exchange rate: Nếu thanh toán ngoại tệ, ghi nhận tỷ giá và ngày áp dụng

### 13.3 Personal Data Protection (Nghị định 13/2023/NĐ-CP)

#### FR-VN-004: PDPA Compliance
- **Description:** Tuân thủ quy định bảo vệ dữ liệu cá nhân của Việt Nam.
- **Requirements:**
  - **Consent Management:** Ghi nhận consent của customer khi thu thập dữ liệu cá nhân
  - **Right to Access:** Customer có thể yêu cầu xem dữ liệu cá nhân của họ
  - **Right to Correction:** Customer có thể yêu cầu sửa dữ liệu sai
  - **Right to Deletion:** Customer có thể yêu cầu xóa dữ liệu (trừ dữ liệu pháp lý bắt buộc lưu trữ)
  - **PII Masking:** Tất cả PII trong logs phải được mask (§7.2 FR-AUD-002)
  - **Data Retention:** Dữ liệu cá nhân không lưu quá thời hạn cần thiết (xem BRD §6.1 C7)
- **Implementation:**
  - API endpoint `/api/v1/compliance/pdpa/consent` — ghi nhận consent
  - API endpoint `/api/v1/compliance/pdpa/export/{customer_id}` — export dữ liệu cá nhân
  - API endpoint `/api/v1/compliance/pdpa/delete/{customer_id}` — xóa dữ liệu (soft delete)

### 13.4 Travel Industry Specific

#### FR-VN-005: Tour Guide License Validation
- **Description:** Nếu ERP quản lý hướng dẫn viên, validate thẻ HDV.
- **Rules:**
  - Số thẻ HDV phải do Tổng cục Du lịch cấp
  - Kiểm tra thời hạn thẻ (không được hết hạn)
  - Ghi nhận ngôn ngữ HDV (tiếng Anh, tiếng Trung, v.v.)

#### FR-VN-006: International Tour License
- **Description:** Nếu ERP quản lý tour quốc tế, validate giấy phép kinh doanh lữ hành quốc tế.
- **Rules:**
  - Kiểm tra giấy phép kinh doanh lữ hành quốc tế còn hiệu lực
  - Ghi nhận số giấy phép trong hợp đồng tour

---

## 14. Security Requirements

### 14.1 Security Layers (Theo HNH-TDD-SA-002)

| Layer | Cơ chế | Chi tiết |
|-------|--------|----------|
| Network | Nginx + Firewall | Chỉ expose Kong port 8000/8443 ra ngoài cho ERP API Hub. YARP port 8888 cho internal 1StopShop. Internal services (ERPNext, PostgreSQL, RabbitMQ) không có public IP |
| Transport | TLS 1.3 | HTTPS bắt buộc. HTTP redirect sang HTTPS. HSTS header. Strong cipher suites only |
| Edge (Public) | Kong JWT Plugin | Validate Bearer token cho external partners. Rate limiting, API key auth, request transformation |
| Edge (Internal) | YARP + Keycloak | Validate Bearer token. Inject X-User-Id, X-Branch-Id, X-Role. Không cần services gọi lại Keycloak |
| App | API Hub Auth Proxy | Map Keycloak token → ERPNext API Key. Validate tenant context per request |
| Data at rest | AES-256-GCM | ERPNext API secrets encrypted trong PostgreSQL. PII fields encrypted |
| Data masking | Application layer | Back-stage/external systems không thấy SĐT/email/passport đầy đủ |
| Audit trail | Structured JSON logs | Mọi API call được log với correlation ID, actor, timestamp. PII masked |
| Secrets | Environment variables | Không hardcode credentials. Production dùng secrets manager |

### 14.2 Transport Security
- **TLS:** Minimum TLS 1.3 for all communications
- **HSTS:** HTTP Strict Transport Security enabled
- **Certificate:** Valid SSL certificate from trusted CA
- **Cipher suites:** Only strong ciphers (no RC4, DES, 3DES, MD5)

### 14.3 Authentication Security
- **Token storage:** Never log JWT tokens
- **Token expiry:** Access token TTL: 8 hours (working day — theo HNH Keycloak config); Refresh token TTL: 24 hours
- **Token rotation:** Refresh tokens rotated on each use
- **Revocation:** Immediate token revocation via Keycloak admin API
- **2FA:** Bắt buộc cho role `api-hub:admin` (theo HNH policy: Manager/BOD required 2FA)

### 14.4 Input Validation
- **SQL Injection:** Parameterized queries only (Entity Framework Core)
- **XSS Prevention:** Output encoding for all user-generated content
- **Command Injection:** No shell execution with user input
- **Path Traversal:** Validate and sanitize file paths
- **Content-Type:** Strict Content-Type validation

### 14.5 API Key Security
- **Encryption:** AES-256-GCM for API secrets at rest
- **Key rotation:** Support manual rotation; auto-rotation optional (quarterly recommended)
- **Storage:** PostgreSQL with encrypted column (pgcrypto extension)
- **Access:** Only API Hub backend can decrypt; no admin/direct DB access to secrets

### 14.6 Audit Security
- **Tamper-proof:** Audit logs append-only, signed with HMAC-SHA256
- **Access control:** Audit logs only accessible by `api-hub:admin` role
- **Export encryption:** Exported logs encrypted with PGP or password-protected ZIP
- **Retention:** 90 days hot (PostgreSQL), archive > 90 days (compressed) — theo HNH policy

---

## 15. Performance Requirements

### 14.1 Response Time SLAs

| Endpoint Type | P50 | P95 | P99 |
|--------------|-----|-----|-----|
| Authentication | < 50ms | < 100ms | < 200ms |
| Cached Query | < 20ms | < 50ms | < 100ms |
| Uncached Query | < 200ms | < 500ms | < 1s |
| Sync Ingestion (simple) | < 500ms | < 1s | < 2s |
| Sync Ingestion (complex*) | < 1s | < 3s | < 5s |
| Async Ingestion (accept) | < 50ms | < 100ms | < 200ms |

> **Note on SLA:* Complex operations (Sales Order with 20+ line items, multi-step approval workflows, or transactions triggering background jobs) may exceed 2s. ERPNext v15 processes some operations asynchronously via Redis Queue. For operations requiring strict latency, use async ingestion with callback webhook.
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

## 16. Non-Functional Requirements

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
- Trace ID propagation via Kong/X-Request-ID (public) + YARP correlation ID (internal)
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
    system_id ULID PRIMARY KEY DEFAULT gen_random_ulid(),
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
    log_id ULID PRIMARY KEY DEFAULT gen_random_ulid(),
    request_id VARCHAR(100),
    tenant_id VARCHAR(100) NOT NULL,
    system_id ULID REFERENCES external_systems(system_id),
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
    subscription_id ULID PRIMARY KEY DEFAULT gen_random_ulid(),
    system_id ULID NOT NULL REFERENCES external_systems(system_id),
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

## 17. Solution Approach — Ý tưởng giải pháp

### 16.1 Tổng quan kiến trúc

Dựa trên HNH Travel System Architecture Document (HNH-TDD-SA-002), ERP API Hub được thiết kế như một **microservice độc lập** trong hệ sinh thái HNH, tận dụng các infrastructure components hiện có:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           HNH TRAVEL SYSTEM ECOSYSTEM                         │
│                                                                               │
│   ┌──────────────┐      ┌──────────────┐      ┌──────────────────────────┐   │
│   │   External   │      │   External   │      │      External Systems     │   │
│   │   Systems    │      │   Systems    │      │   (CRM, E-commerce, ...)  │   │
│   │  (Website)   │      │  (Partner)   │      │                           │   │
│   └──────┬───────┘      └──────┬───────┘      └─────────────┬─────────────┘   │
│          │                      │                            │                │
│          └──────────────────────┼────────────────────────────┘                │
│                                 │                                             │
│                          ┌──────▼──────┐                                      │
│    Kong (Public:8000)  ←→│ YARP API Gateway │  ← Internal Workspace                 │
│                          │   3.x       │     JWT Validation                     │
│                          │ DB-less     │     Rate Limiting                      │
│                          └──────┬──────┘                                      │
│                                 │                                             │
│          ┌──────────────────────┼──────────────────────┐                       │
│          │                      │                      │                       │
│   ┌──────▼──────┐      ┌─────▼─────┐      ┌──────▼──────┐                │
│   │   Workspace   │      │  ERP API  │      │   Module    │                │
│   │   Services    │      │   Hub     │      │    FIN      │                │
│   │ (.NET Core)   │      │(.NET Core│      │  (Độc lập)  │                │
│   │               │      │    8)     │      │             │                │
│   │ /api/core/*   │      │/api/erp/* │      │ /api/fin/*  │                │
│   │ /api/crm/*    │      │           │      │             │                │
│   │ /api/ticketing│      │           │      │             │                │
│   └──────┬────────┘      └─────┬─────┘      └──────┬──────┘                │
│          │                      │                     │                        │
│          │              ┌───────┴───────┐            │                        │
│          │              │               │            │                        │
│   ┌──────▼──────┐ ┌────▼─────┐  ┌────▼─────┐ ┌────▼─────┐                 │
│   │ PostgreSQL  │ │ RabbitMQ │  │  Redis   │ │ERPNext v15│                 │
│   │  (Shared)  │ │(Shared/  │  │ (Shared) │ │ (Internal│                 │
│   │             │ │ Separate)│  │          │ │  API)    │                 │
│   └─────────────┘ └─────────┘  └──────────┘ └──────────┘                  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 16.2 Nguyên tắc thiết kế

#### SA-001: Tận dụng Infrastructure hiện có
- **Kong API Gateway:** Kong 3.x DB-less cho public-facing ERP API Hub. JWT, rate limiting, API key auth, request transformation qua plugins.
- **YARP (.NET 9):** Internal gateway cho 1StopShop (unchanged). Route `/api/erp/*` → ERP API Hub cho internal traffic.
- **Keycloak:** Dùng shared realm `HNHTravel-SGN`. Thêm roles `api-hub:read`, `api-hub:write`, `api-hub:admin`, `api-hub:webhook`.
- **PostgreSQL 18:** Dùng shared instance (separate database `erphub_api_db`).
- **Redis 7:** Dùng shared instance (separate key prefix `erphub:`).
- **RabbitMQ:** **Tách riêng exchange** `1stopshop_event_bus` để cách ly workload ingestion khỏi hệ thống workspace.

#### SA-002: Cách ly module — Zero coupling với Workspace
- API Hub **KHÔNG** gọi trực tiếp bất kỳ workspace service nào (Core, CRM, Ticketing, v.v.).
- API Hub chỉ giao tiếp với ERPNext v15 (qua REST API) và external systems.
- Module FIN hoạt động độc lập — API Hub không tương tác.

#### SA-003: Multi-tenancy qua ERPNext Sites
- Mỗi chi nhánh (CN1, CN2, CN3) = 1 ERPNext site.
- Tenant resolution: `branch_id` trong JWT claim → map sang `site_name` + `erpnext_host`.

#### SA-004: Auth Proxy Pattern (Option C)
```
External System ──► Kong (:8000, JWT validate) ──► API Hub ──► ERPNext
                                                  │
                                                  ▼
                                            ERPNext REST API
                                                  │
                                                  ▼
                                            MariaDB (per site)
```
- API Hub giữ bảng mapping: `keycloak_user_id` → `erpnext_api_key` + `erpnext_api_secret` (encrypted).
- Mỗi request: validate JWT → lookup API key → gọi ERPNext với `Authorization: token {api_key}:{api_secret}`.

### 16.3 Stack công nghệ

| Thành phần | Lựa chọn | Lý do |
|-----------|---------|-------|
| API Hub Service | .NET Core 8 | Team có expertise; consistency với workspace services; rich middleware ecosystem (rate limiting, auth, caching) |
| Database | PostgreSQL 18 (shared) | Infrastructure hiện có; audit logs, config, registry |
| Cache | Redis 7 (shared, prefix `erphub:`) | Response caching, rate limit counters, session store |
| Message Queue | RabbitMQ 3.12 (exchange `1stopshop_event_bus`) | Async ingestion; cách ly khỏi workspace events |
| API Gateway (Public) | Kong 3.x (DB-less) | JWT, rate limiting, API key auth, transformation |
| API Gateway (Internal) | YARP (.NET 9) | Existing; JWT validation; routing; branch_id injection |
| Identity | Keycloak (shared realm `HNHTravel-SGN`) | Single sign-on; existing realm configuration |
| ERP Backend | ERPNext v15 (Frappe Framework) | Existing system; REST API tại `/api/resource/{Doctype}` |

### 16.4 Kong & YARP Routing Configuration

#### Kong (Public) — `kong.yml`

```yaml
# Kong DB-less declarative config cho public-facing ERP API Hub
services:
  - name: erp-api-hub
    url: http://erp-api-hub:8008
    routes:
      - name: erp-api-routes
        paths:
          - /api/erp
        strip_path: true
        protocols:
          - https
    plugins:
      - name: jwt
        config:
          uri_param_names: []
          cookie_names: []
          key_claim_name: iss
          secret_is_base64: false
          claims_to_verify:
            - exp
      - name: rate-limiting
        config:
          minute: 1000
          policy: redis
          redis_host: redis
      - name: request-transformer
        config:
          add:
            headers:
              - X-Request-Source:external

  - name: erp-api-hub-internal
    url: http://erp-api-hub:8008
    routes:
      - name: erp-api-internal
        paths:
          - /internal/erp
        strip_path: true
    plugins: []  # No JWT for internal webhook callbacks from ERPNext
```

#### YARP (Internal) — `yarp.json`

```json
{
  "ReverseProxy": {
    "Routes": {
      "erp-api": {
        "ClusterId": "erp-api-hub",
        "Match": { "Path": "/api/erp/{**catch-all}" },
        "Transforms": [{ "PathRemovePrefix": "/api/erp" }]
      }
    },
    "Clusters": {
      "erp-api-hub": {
        "Destinations": {
          "destination1": { "Address": "http://erp-api-hub:8008" }
        }
      }
    }
  }
}
```

### 16.5 Keycloak Realm Configuration — Bổ sung

Thêm vào realm `HNHTravel-SGN`:

```json
{
  "realm": "HNHTravel-SGN",
  "roles": {
    "realm": [
      { "name": "api-hub:read", "description": "Query ERP data" },
      { "name": "api-hub:write", "description": "Ingest data to ERP" },
      { "name": "api-hub:admin", "description": "Manage API Hub config" },
      { "name": "api-hub:webhook", "description": "Manage webhook subscriptions" }
    ]
  },
  "clients": [
    {
      "clientId": "hnh-erp-hub",
      "name": "ERP API Hub",
      "protocol": "openid-connect",
      "publicClient": false,
      "directAccessGrantsEnabled": true,
      "serviceAccountsEnabled": true
    }
  ]
}
```

### 16.6 RabbitMQ Exchange Design

```
Exchange: 1stopshop_event_bus (topic, durable)
├── Routing Keys:
│   ├── erphub.ingestion.{doctype}.created
│   ├── erphub.ingestion.{doctype}.updated
│   ├── erphub.ingestion.{doctype}.failed
│   ├── erphub.webhook.{event_type}
│   └── erphub.audit.{action}
│
├── Queues:
│   ├── erphub.ingestion.default (durable)
│   ├── erphub.ingestion.high (durable, priority: 10)
│   ├── erphub.ingestion.low (durable)
│   ├── erphub.webhook.delivery (durable)
│   └── erphub.ingestion.dlq (dead letter, TTL: 30 days)
│
└── DLQ Policy:
    ├── Max retries: 5
    ├── Backoff: 2s → 4s → 8s → 16s → 32s
    └── Alert: Grafana alert when DLQ > 100 messages
```

### 16.7 Deployment Topology

```
┌─────────────────────────────────────────────────────────────┐
│                     DOCKER HOST (On-premise)                  │
│                                                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐   │
│  │   Nginx     │  │ Kong (Public)│  │   ERP API Hub       │   │
│  │   (443)     │──│  (:8000)    │──│   (.NET 9)          │   │
│  │             │  │  DB-less    │  │   Port: 8008        │   │
│  └─────────────┘  └─────────────┘  └─────────────────────┘   │
│         │                │                  │                 │
│         │                │                  │                 │
│  ┌──────▼──────┐  ┌────▼─────┐  ┌───────▼────────┐         │
│  │  ERPNext    │  │ RabbitMQ │  │  PostgreSQL    │         │
│  │  (nginx)    │  │(5672/    │  │  (Port: 5452)  │         │
│  │  Port: 8080 │  │ 15672)   │  │  DB: erp_api_  │         │
│  │             │  │          │  │     hub         │         │
│  └─────────────┘  └─────────┘  └────────────────┘         │
│                                                               │
│  ┌─────────────┐  ┌─────────────┐                           │
│  │   Redis     │  │  Keycloak   │                           │
│  │  (6379)     │  │  (8080)     │                           │
│  └─────────────┘  └─────────────┘                           │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

### 16.8 Giải pháp Webhook (Event-Driven)

**Vấn đề:** ERPNext v15 không có native webhook dispatcher.

**Giải pháp lai (Hybrid):**

1. **Primary — Frappe Server Script (Minimal):**
   - Không sửa ERPNext core.
   - Tạo Server Script trong ERPNext UI cho các doctype cần thiết.
   - Script gọi API Hub internal endpoint: `POST /internal/erp/events`
   - Payload: `{event_type, doctype, name, modified_by, timestamp}`

2. **Fallback — Polling (Cursor-based):**
   - API Hub worker poll `GET /api/resource/{Doctype}?filters=[["modified", ">", "{last_cursor}"]]`
   - Cursor lưu trong Redis (per doctype, per tenant).
   - Frequency: 30 giây cho critical doctypes, 5 phút cho others.

3. **Future — CDC (Change Data Capture):**
   - Nếu cần real-time hơn, dùng Debezium connector trên MariaDB binlog.
   - Phức tạp hơn, để Phase 2.

### 16.9 Security Layer

| Layer | Cơ chế | Chi tiết |
|-------|--------|----------|
| Network | Nginx + Firewall | Chỉ expose YARP API Gateway port 8888 ra ngoài. ERPNext internal (no public IP) |
| Transport | TLS 1.3 | HTTPS bắt buộc. HSTS header |
| Edge (Public) | Kong JWT Plugin | Validate Bearer token cho external partners |
| Edge (Internal) | YARP + Keycloak | Validate Bearer token. Inject X-User-Id, X-Branch-Id, X-Role |
| App | API Hub Auth | Map Keycloak token → ERPNext API Key. Validate tenant context |
| Data at rest | AES-256-GCM | ERPNext API secrets encrypted in PostgreSQL |
| Audit | Structured logs | JSON format, correlation ID, PII masking |

---

## Appendix D: Architecture Decision Records (ADR)

| # | Decision | Lựa chọn | Lý do |
|---|----------|---------|-------|
| ADR-001 | API Hub stack | .NET Core 8 | Team expertise; consistency với workspace services |
| ADR-002 | Auth pattern | Auth Proxy (Option C) | Không sửa ERPNext core; tận dụng Keycloak hiện có |
| ADR-003 | Tenant isolation | ERPNext Sites + Tenant Registry | Native ERPNext multi-tenancy; dynamic resolution |
| ADR-004 | Event sourcing | Hybrid: Server Script + Polling | ERPNext không có native webhooks; minimal intrusion |
| ADR-005 | Message Queue | Separate exchange `1stopshop_event_bus` | Cách ly workload; không ảnh hưởng workspace events |
| ADR-006 | Database | Shared PostgreSQL 18 (separate DB) | Tận dụng infrastructure; không cần thêm DB server |
| ADR-007 | Cache | Shared Redis 7 (key prefix `erphub:`) | Tận dụng infrastructure; isolation qua prefix |

---

**End of Document**
