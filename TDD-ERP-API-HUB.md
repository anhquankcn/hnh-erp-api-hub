# Technical Design Document (TDD)
# ERP API Hub — HNH Travel

**Document Version:** 1.0-Draft  
**Date:** 2026-05-26  
**Author:** HNH Technical Team  
**Status:** Draft — Pending Review  
**Related Documents:** FRD-ERP-API-HUB-v1.2 · PLAT-001 (Platform Reference Architecture)

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Architecture Overview](#2-architecture-overview)
3. [Database Schema](#3-database-schema)
4. [API Contracts](#4-api-contracts)
5. [Deployment Architecture](#5-deployment-architecture)
6. [Security Architecture](#6-security-architecture)
7. [Performance & Scalability](#7-performance--scalability)
8. [Monitoring & Observability](#8-monitoring--observability)
9. [Integration Patterns](#9-integration-patterns)
10. [Open Items](#10-open-items)

---

## 1. Introduction

### 1.1 Purpose
TDD này mô tả chi tiết kỹ thuật để implement ERP API Hub dựa trên FRD v1.2 và Platform Reference Architecture (PLAT-001).

### 1.2 Scope
- Database schema (PostgreSQL 18)
- API contracts (REST + OpenAPI)
- Deployment (Docker Compose)
- Security (TLS, JWT, encryption)
- Monitoring (Prometheus, Grafana)

### 1.3 Tech Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Runtime | .NET | 9.0 (theo hệ chuẩn) |
| Web Framework | ASP.NET Core Minimal API | 9.0 |
| ORM | Entity Framework Core | 9.0 |
| Database | PostgreSQL | 18 |
| Cache | Redis | 7.x |
| Message Queue | RabbitMQ | 3.12 |
| API Gateway (Public) | Kong | 3.x (DB-less) | JWT, rate limiting, API key auth |
| API Gateway (Internal) | YARP (Reverse Proxy) | .NET 9 | Internal routing, branch_id injection |
| Identity | Keycloak | (shared) |
| ERP Backend | ERPNext / Frappe | v15 |
| Logging | Serilog | latest |
| Metrics | Prometheus.Client | latest |
| Testing | xUnit + TestContainers | latest |

---

## 2. Architecture Overview

### 2.1 Component Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           HNH TRAVEL SYSTEM ECOSYSTEM                         │
│                                                                               │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                        YARP API Gateway (:8888)                        │   │
│   │  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────────┐  │   │
│   │  │ /api/core/* │  │ /api/crm/*  │  │ /api/erp/*  → erp-api-hub   │  │   │
│   │  │  → Core:8001│  │  → Crm:8002 │  │  → :8008 (internal traffic) │  │   │
│   │  └─────────────┘  └─────────────┘  └─────────────────────────────┘  │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                      │                                        │
│   ┌──────────────────────────────────┼──────────────────────────────────┐   │
│   │                                  │  External Traffic               │   │
│   │  ┌──────────────────────────────┼──────────────────────────────┐   │   │
│   │  │     Kong API Gateway (:8000) │                              │   │   │
│   │  │  /api/erp/* → erp-api-hub:8008 (JWT, rate limiting)        │   │   │
│   │  └──────────────────────────────────────────────────────────────┘   │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                      │                                        │
│   ┌──────────────────────────────────┼──────────────────────────────────┐   │
│   │                                  │                                  │   │
│   ▼                                  ▼                                  ▼   │
│ ┌──────────┐  ┌──────────────┐  ┌──────────────┐  ┌─────────────────┐      │
│ │ BFF:8090 │  │ CoreService  │  │  erp-api-hub │  │  Notification   │      │
│ │(.NET 9)  │  │   :8001      │  │    :8008     │  │    :8007        │      │
│ └────┬─────┘  └──────────────┘  └──────┬──────┘  └─────────────────┘      │
│      │                                   │                                    │
│      │         ┌─────────────────────────┼──────────────┐                   │
│      │         │                         │              │                   │
│   ┌──┴──┐  ┌──▼────┐  ┌────────┐  ┌────▼─────┐  ┌────▼────┐           │
│   │Redis│  │RabbitMQ│  │PostgreSQL│  │  Keycloak  │  │ERPNext  │           │
│   │:6379│  │:5672   │  │  :5452   │  │  :8443     │  │ :8080   │           │
│   └─────┘  └────────┘  └────────┘  └────────────┘  └─────────┘           │
│                                                                           │
└───────────────────────────────────────────────────────────────────────────┘
```

### 2.2 Service Decomposition

| Service | Port | Responsibility | DB |
|---------|------|---------------|-----|
| erp-api-hub | 8008 | Ingestion, Query, Webhook, Auth Proxy | erphub_api_db |
| erp-worker | 8009 | Background consumers (RabbitMQ) | erphub_api_db |

### 2.3 Ports & Hostnames (Dev)

```
React Shell         : 3458
BFF                 : 8090
Kong API Gateway    : 8000 (public, DB-less)
YARP API Gateway    : 8888 (internal)
erp-api-hub        : 8008
erp-worker          : 8009
CoreService         : 8001
CrmService          : 8002
TicketingService    : 8003
QuotationService    : 8004
PaymentService      : 8005
KpiService          : 8006
NotificationService : 8007
PostgreSQL          : 5452
RabbitMQ            : 5672 / 15672
Redis               : 6379
Keycloak            : https://quanna.tail072b2f.ts.net:8443
ERPNext             : 8080 (internal)
```

---

## 3. Database Schema

### 3.1 Entity Relationship Diagram

```
┌─────────────────────┐     ┌─────────────────────┐     ┌─────────────────────┐
│   external_systems  │     │   tenant_registry   │     │  api_key_mapping    │
├─────────────────────┤     ├─────────────────────┤     ├─────────────────────┤
│ PK system_id (ULID) │────▶│ PK tenant_id (ULID) │     │ PK mapping_id (ULID)│
│    system_name      │     │    site_name        │     │ FK system_id        │
│    system_type      │     │    erpnext_host     │     │    keycloak_user_id │
│    webhook_url      │     │    api_key_enc      │     │    erpnext_api_key  │
│    rate_limit_tier  │     │    api_secret_enc   │     │    erpnext_api_sec  │
│    is_active        │     │    is_active        │     │    created_at       │
│    tenant_id FK     │────▶│    health_status    │     │    updated_at       │
└─────────────────────┘     └─────────────────────┘     └─────────────────────┘
          │
          │
          ▼
┌─────────────────────┐     ┌─────────────────────┐     ┌─────────────────────┐
│ webhook_subscriptions│    │   webhook_deliveries │     │     audit_logs       │
├─────────────────────┤     ├─────────────────────┤     ├─────────────────────┤
│ PK sub_id (ULID)    │     │ PK delivery_id (ULID)│    │ PK log_id (ULID)    │
│ FK system_id        │     │ FK sub_id           │     │    request_id       │
│    event_types[]    │     │    status           │     │    tenant_id        │
│    webhook_url      │     │    http_status      │     │    system_id FK     │
│    secret_enc       │     │    response_body    │     │    user_id          │
│    is_active        │     │    attempted_at     │     │    method           │
└─────────────────────┘     └─────────────────────┘     │    endpoint         │
                                                        │    status_code      │
┌─────────────────────┐                                  │    duration_ms      │
│   field_mappings    │                                  │    client_ip        │
├─────────────────────┤                                  │    created_at       │
│ PK mapping_id (ULID)│                                  └─────────────────────┘
│ FK system_id        │
│    external_field   │
│    erpnext_field    │
│    mapping_type     │
│    lookup_table     │
│    transform_script │
└─────────────────────┘
```

### 3.2 Table Definitions

#### external_systems
```sql
CREATE TABLE external_systems (
    system_id VARCHAR(26) PRIMARY KEY,  -- ULID
    system_name VARCHAR(100) NOT NULL,
    system_type VARCHAR(50) NOT NULL,
    description TEXT,
    contact_email VARCHAR(255),
    webhook_url VARCHAR(500),
    rate_limit_tier VARCHAR(20) DEFAULT 'TIER_2',
    is_active BOOLEAN DEFAULT true,
    tenant_id VARCHAR(26) NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    deleted_at TIMESTAMPTZ,  -- soft delete
    created_by VARCHAR(26),
    updated_by VARCHAR(26),
    UNIQUE(system_name, tenant_id)
);
```

#### tenant_registry
```sql
CREATE TABLE tenant_registry (
    tenant_id VARCHAR(26) PRIMARY KEY,  -- ULID, = BranchId from JWT
    site_name VARCHAR(100) NOT NULL,       -- e.g., "frontend"
    erpnext_host VARCHAR(255) NOT NULL,    -- e.g., "erpnext-frontend:8080"
    erpnext_api_key_encrypted BYTEA,       -- AES-256-GCM encrypted
    erpnext_api_secret_encrypted BYTEA,    -- AES-256-GCM encrypted
    is_active BOOLEAN DEFAULT true,
    health_status VARCHAR(20) DEFAULT 'healthy',
    last_health_check TIMESTAMPTZ,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    deleted_at TIMESTAMPTZ,
    created_by VARCHAR(26),
    updated_by VARCHAR(26)
);
```

#### api_key_mapping
```sql
CREATE TABLE api_key_mapping (
    mapping_id VARCHAR(26) PRIMARY KEY,
    system_id VARCHAR(26) REFERENCES external_systems(system_id),
    keycloak_user_id VARCHAR(255) NOT NULL,
    erpnext_api_key_encrypted BYTEA NOT NULL,
    erpnext_api_secret_encrypted BYTEA NOT NULL,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(keycloak_user_id, system_id)
);
```

#### webhook_subscriptions
```sql
CREATE TABLE webhook_subscriptions (
    subscription_id VARCHAR(26) PRIMARY KEY,
    system_id VARCHAR(26) NOT NULL REFERENCES external_systems(system_id),
    event_types TEXT[] NOT NULL,
    webhook_url VARCHAR(500) NOT NULL,
    secret_encrypted BYTEA,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    deleted_at TIMESTAMPTZ,
    created_by VARCHAR(26),
    updated_by VARCHAR(26)
);
```

#### audit_logs
```sql
CREATE TABLE audit_logs (
    log_id VARCHAR(26) PRIMARY KEY,
    request_id VARCHAR(100),
    tenant_id VARCHAR(26) NOT NULL,
    system_id VARCHAR(26) REFERENCES external_systems(system_id),
    user_id VARCHAR(255),
    method VARCHAR(10) NOT NULL,
    endpoint VARCHAR(500) NOT NULL,
    status_code INTEGER,
    duration_ms INTEGER,
    request_size_bytes INTEGER,
    response_size_bytes INTEGER,
    client_ip INET,
    user_agent TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);
CREATE INDEX idx_audit_logs_created_at ON audit_logs(created_at);
CREATE INDEX idx_audit_logs_tenant ON audit_logs(tenant_id);
CREATE INDEX idx_audit_logs_system ON audit_logs(system_id);
CREATE INDEX idx_audit_logs_user ON audit_logs(user_id);
```

### 3.3 Indexes

```sql
-- Performance indexes
CREATE INDEX idx_external_systems_tenant ON external_systems(tenant_id);
CREATE INDEX idx_external_systems_type ON external_systems(system_type);
CREATE INDEX idx_api_key_mapping_user ON api_key_mapping(keycloak_user_id);
CREATE INDEX idx_webhook_sub_system ON webhook_subscriptions(system_id);
CREATE INDEX idx_field_mappings_system ON field_mappings(system_id);

-- Partitioning: audit_logs by month (optional for scale)
-- CREATE TABLE audit_logs_2026_05 PARTITION OF audit_logs
--     FOR VALUES FROM ('2026-05-01') TO ('2026-06-01');
```

---

## 4. API Contracts

### 4.1 OpenAPI Spec (excerpt)

```yaml
openapi: 3.0.3
info:
  title: ERP API Hub
  version: 1.0.0
  description: API Gateway for ERPNext v15 integration

servers:
  - url: https://api.hnhtravel.work/api/erp/v1

paths:
  /auth/token:
    post:
      summary: Exchange Keycloak token for API Hub session
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              properties:
                keycloak_token:
                  type: string
      responses:
        200:
          description: Session created
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TokenResponse'

  /ingest/{doctype}:
    post:
      summary: Ingest single document
      parameters:
        - name: doctype
          in: path
          required: true
          schema:
            type: string
        - name: X-Idempotency-Key
          in: header
          required: false
          schema:
            type: string
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
      responses:
        202:
          description: Accepted for async processing
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/JobResponse'

  /query/{doctype}:
    get:
      summary: List documents
      parameters:
        - name: doctype
          in: path
          required: true
          schema:
            type: string
        - name: page
          in: query
          schema:
            type: integer
            default: 1
        - name: pageSize
          in: query
          schema:
            type: integer
            default: 20
        - name: filters
          in: query
          schema:
            type: string
        - name: fields
          in: query
          schema:
            type: array
            items:
              type: string
      responses:
        200:
          description: Document list
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PaginatedResponse'

components:
  schemas:
    TokenResponse:
      type: object
      properties:
        access_token:
          type: string
        refresh_token:
          type: string
        expires_in:
          type: integer
    JobResponse:
      type: object
      properties:
        job_id:
          type: string
        status:
          type: string
          enum: [pending, processing, completed, failed]
    PaginatedResponse:
      type: object
      properties:
        items:
          type: array
          items:
            type: object
        totalCount:
          type: integer
        page:
          type: integer
        pageSize:
          type: integer
```

### 4.2 Error Response Format

```json
{
  "error": {
    "code": "VALIDATION_FAILED",
    "message": "The request body contains validation errors",
    "details": [
      {
        "field": "customer_name",
        "message": "Customer name is required",
        "code": "required"
      }
    ],
    "request_id": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
    "timestamp": "2026-05-26T14:30:00+07:00"
  }
}
```

---

## 5. Deployment Architecture

### 5.1 Docker Compose

```yaml
version: '3.8'

services:
  kong:
    image: kong:3.6
    environment:
      - KONG_DATABASE=off
      - KONG_DECLARATIVE_CONFIG=/etc/kong/kong.yml
      - KONG_PROXY_ACCESS_LOG=/dev/stdout
      - KONG_ADMIN_ACCESS_LOG=/dev/stdout
      - KONG_PROXY_ERROR_LOG=/dev/stderr
      - KONG_ADMIN_ERROR_LOG=/dev/stderr
    volumes:
      - ./kong/kong.yml:/etc/kong/kong.yml:ro
    ports:
      - "8000:8000"  # Proxy (public)
      - "8443:8443"  # Proxy SSL (public)
      - "8001:8001"  # Admin API (internal only)
    networks:
      - hnh-network
    restart: unless-stopped

  erp-api-hub:
    build:
      context: ./src/ERPApiHub
      dockerfile: Dockerfile
    ports:
      - "8008:8008"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__Default=Host=postgres;Port=5432;Database=erphub_api_db;Username=erphub;Password=${ERPHUB_DB_PASSWORD}
      - Redis__ConnectionString=redis:6379,password=${REDIS_PASSWORD}
      - RabbitMQ__Host=rabbitmq
      - RabbitMQ__Username=${RABBIT_USER}
      - RabbitMQ__Password=${RABBIT_PASS}
      - Keycloak__RealmUrl=https://quanna.tail072b2f.ts.net:8443/realms/HNHTravel-SGN
      - ERPNext__DefaultHost=erpnext:8080
      - Encryption__MasterKey=${MASTER_KEY}
    depends_on:
      - postgres
      - redis
      - rabbitmq
    networks:
      - hnh-network
    restart: unless-stopped

  erp-worker:
    build:
      context: ./src/ERPApiHub
      dockerfile: Dockerfile.worker
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ConnectionStrings__Default=Host=postgres;Port=5432;Database=erphub_api_db;Username=erphub;Password=${ERPHUB_DB_PASSWORD}
      - Redis__ConnectionString=redis:6379,password=${REDIS_PASSWORD}
      - RabbitMQ__Host=rabbitmq
      - Keycloak__RealmUrl=https://quanna.tail072b2f.ts.net:8443/realms/HNHTravel-SGN
      - ERPNext__DefaultHost=erpnext:8080
      - Encryption__MasterKey=${MASTER_KEY}
    depends_on:
      - postgres
      - redis
      - rabbitmq
    networks:
      - hnh-network
    restart: unless-stopped

networks:
  hnh-network:
    external: true
```

### 5.2 Kong Configuration (kong.yml) — Public API Gateway

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

  - name: erp-api-hub-webhook
    url: http://erp-api-hub:8008
    routes:
      - name: erp-api-internal
        paths:
          - /internal/erp
        strip_path: true
    plugins: []  # No JWT for internal webhook callbacks
```

### 5.3 YARP Configuration (yarp.json) — Internal API Gateway

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

---

## 6. Security Architecture

### 6.1 Threat Model

| Threat | Mitigation |
|--------|-----------|
| JWT theft | Short TTL (8h), rotation, revocation via Keycloak |
| Replay attack | `X-Request-ID` ULID + dedupe (Redis 5min TTL) |
| SQL injection | EF Core parameterized queries |
| Data leak | AES-256-GCM at rest, TLS 1.3 in transit, PII masking |
| Privilege escalation | ABAC branch isolation, role checks |
| DDoS | Rate limiting per consumer (Redis sliding window) |

### 6.2 Encryption Key Management

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Master Key     │────▶│  DEK (per key)  │────▶│  Encrypted Secret│
│  (HSM / Env var)│     │  (AES-256-GCM)  │     │  (DB storage)    │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

- Master key: stored in environment variable or HashiCorp Vault
- Data Encryption Key (DEK): derived per record using PBKDF2
- Never log or expose decrypted secrets

### 6.3 PII Masking Rules

| Field | Mask Pattern | Example |
|-------|-------------|---------|
| email | `a***@domain.com` | `q***@hnh.vn` |
| phone | `+84-***-***-789` | `+84-912-***-***` |
| passport | `***********` | `***********` |
| tax_id | `***1234567` | `***1234567` |
| bank_account | `****1234` | `****1234` |

---

## 7. Performance & Scalability

### 7.1 Caching Strategy

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Request   │────▶│ Redis Cache │────▶│ ERPNext API │
└─────────────┘     └─────────────┘     └─────────────┘
       │                   │
       ▼                   ▼
  Cache miss         Cache hit
  (200-500ms)        (5-20ms)
```

| Cache Type | Key Pattern | TTL | Invalidation |
|-----------|-------------|-----|--------------|
| Query result | `query:{tenant}:{doctype}:{hash}` | 5 min | Webhook event or explicit purge |
| Auth token | `auth:{token_hash}` | 15 min | Logout or expiry |
| Rate limit | `ratelimit:{system_id}:{window}` | 1 min | Window expiry |
| Tenant registry | `tenant:{tenant_id}` | 1 min | Config update |

### 7.2 Horizontal Scaling

```
                    ┌──────────────┐
                    │   YARP LB    │
                    │   (:8888)    │
                    └──────┬───────┘
                           │
           ┌───────────────┼───────────────┐
           │               │               │
    ┌──────▼──────┐ ┌─────▼─────┐ ┌──────▼──────┐
    │ erp-api-hub │ │ erp-api-  │ │ erp-api-    │
    │   :8008     │ │  hub-2    │ │   hub-3     │
    │  (instance) │ │ :8008     │ │  :8008      │
    └──────┬──────┘ └─────┬─────┘ └──────┬──────┘
           │               │               │
           └───────────────┼───────────────┘
                           │
                    ┌──────▼──────┐
                    │  PostgreSQL │
                    │   :5452     │
                    └─────────────┘
```

- API Hub: stateless → scale horizontally
- Worker: queue consumer → scale by partition
- Redis: use Redis Sentinel or Cluster
- PostgreSQL: read replicas for query load

---

## 8. Monitoring & Observability

### 8.1 Metrics (Prometheus)

```csharp
// Custom metrics
var requestCounter = Metrics.CreateCounter(
    "erphub_requests_total",
    "Total requests",
    new CounterConfiguration { LabelNames = new[] { "endpoint", "method", "status" } }
);

var requestDuration = Metrics.CreateHistogram(
    "erphub_request_duration_seconds",
    "Request duration",
    new HistogramConfiguration { LabelNames = new[] { "endpoint" } }
);

var cacheHitRatio = Metrics.CreateGauge(
    "erphub_cache_hit_ratio",
    "Cache hit ratio"
);

var erpnextErrors = Metrics.CreateCounter(
    "erphub_erpnext_errors_total",
    "ERPNext errors",
    new CounterConfiguration { LabelNames = new[] { "error_type" } }
);

var queueDepth = Metrics.CreateGauge(
    "erphub_queue_depth",
    "Queue depth",
    new GaugeConfiguration { LabelNames = new[] { "queue_name" } }
);
```

### 8.2 Health Check Endpoint

```csharp
// GET /health
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        var result = new
        {
            status = report.Status.ToString(),
            service = "ERPApiHub",
            version = "1.0.0",
            timestamp = DateTime.UtcNow,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                duration = e.Value.Duration.TotalMilliseconds
            })
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});
```

### 8.3 Log Format (Serilog)

```json
{
  "timestamp": "2026-05-26T14:30:00+07:00",
  "level": "INFO",
  "service": "ERPApiHub",
  "trace_id": "01ARZ3NDEKTSV4RRFFQ69G5FAV",
  "user_id": "01ARZ...",
  "branch_id": "01ARZ...",
  "action": "IngestCustomer",
  "duration_ms": 45,
  "status": "success",
  "message": "Customer CUST-2026-00001 ingested"
}
```

---

## 9. Integration Patterns

### 9.1 Auth Pattern (A)

```
External System → Kong (:8000)
                      │
                      ▼
              JWT Validation (Kong JWT Plugin, RS256 via JWKS)
                      │
                      ▼
              Extract Claims (BranchId, roles)
                      │
                      ▼
              API Hub (map to ERPNext API Key)
                      │
                      ▼
              ERPNext REST API (/api/resource/{Doctype})
```

### 9.2 Event Pattern (B)

```
Publisher                         Consumer
   │                                │
   │──► exchange: 1stopshop_event_bus ◄──┤
   │    routing: erphub.ingestion.created  │
   │                                │
   │                                ▼
   │                         ┌─────────────┐
   │                         │  erp-worker │
   │                         │  :8009      │
   │                         └─────────────┘
```

### 9.3 Webhook Pattern (D)

```
ERPNext Document Event
        │
        ▼
Frappe Server Script
        │
        ▼
POST /internal/erp/events (Kong/YARP → no auth, HMAC validated)
        │
        ▼
API Hub → validate HMAC
        │
        ▼
Queue webhook delivery (1stopshop_event_bus)
        │
        ▼
Dispatch to external system (with retry + DLQ)
```

---

## 10. Open Items

| # | Item | Owner | Deadline | Status |
|---|------|-------|----------|--------|
| 1 | ELK Stack cho centralized log | TBD | Phase 2 | Open |
| 2 | Schema registry cho event payload | TBD | Phase 2 | Open |
| 3 | mTLS service-to-service | TBD | Phase 2 | Open |
| 4 | Redis Sentinel/Cluster | TBD | Phase 2 | Open |
| 5 | PostgreSQL read replicas | TBD | Phase 3 | Open |
| 6 | Hard cut-over realm Keycloak | TBD | Phase 3 | Open |
| 7 | Debezium CDC (MariaDB binlog) | TBD | Phase 3 | Open |

---

**End of Document**
