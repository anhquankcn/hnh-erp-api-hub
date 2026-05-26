# CLAUDE.md — ERP API Hub Onboarding

> File này là Single Source of Truth cho AI agent khi làm việc với project ERP API Hub.
> Đọc file này TRƯỚC khi viết bất kỳ dòng code nào.

---

## 1. Project Overview

**ERP API Hub** — cổng kết nối giữa hệ sinh thái HNH Travel (1StopShop) và ERPNext (Frappe v15).

- **Vai trò:** Service riêng, chạy song song với các microservice hiện có
- **Port:** 8008 (API Hub), 8009 (Worker)
- **Gateway:** YARP API Gateway (.NET 9) trên port 8888
- **ERP Backend:** ERPNext v15

### Chiều tích hợp

1. **Inbound (Ingestion):** External system → YARP → API Hub → ERPNext (ghi dữ liệu)
2. **Outbound (Query):** External system → YARP → API Hub → ERPNext (đọc dữ liệu)
3. **Event (RabbitMQ):** 1StopShop services publish event → API Hub consume → ghi vào ERPNext
4. **Webhook (Outbound):** ERPNext event → API Hub → external systems

---

## 2. Tech Stack

| Layer | Technology | Version | Notes |
|-------|-----------|---------|-------|
| Runtime | .NET | 9.0 | Theo chuẩn hệ |
| Web | ASP.NET Core Minimal API | 9.0 | |
| ORM | Entity Framework Core | 9.0 | Code-first migration |
| Database | PostgreSQL | 18 | Port 5452 (dev) |
| Cache | Redis | 7.x | Prefix: `erphub:` |
| Message Queue | RabbitMQ | 3.12 | Exchange: `1stopshop_event_bus` |
| API Gateway | YARP | .NET 9 | Port 8888 |
| Identity | Keycloak | (shared) | Realm: `HNHTravel-SGN` |
| ERP Backend | ERPNext / Frappe | v15 | |
| Logging | Serilog | latest | Structured JSON |
| Metrics | Prometheus.Client | latest | |
| Testing | xUnit + TestContainers | latest | |

---

## 3. Architecture Decisions

### 3.1 ID Format: ULID — KHÔNG dùng UUID

**Mọi ID nghiệp vụ phải là ULID 26 ký tự.** Tuyệt đối không UUID v4 hay auto-increment.

```csharp
// ✅ ĐÚNG
var id = Ulid.NewUlid().ToString(); // "01ARZ3NDEKTSV4RRFFQ69G5FAV"

// ❌ SAI
var id = Guid.NewGuid().ToString(); // UUID v4
var id = db.NextVal(); // auto-increment
```

### 3.2 Multi-tenant: branch_id từ JWT claim

**Client KHÔNG BAO GIỜ được tin để chỉ định branch_id.** Lấy từ JWT claim `BranchId`.

```csharp
// ✅ ĐÚNG
var branchId = User.FindFirst("BranchId")?.Value;

// ❌ SAI
var branchId = Request.Headers["X-Branch-Id"]; // không tin header từ client
```

### 3.3 Soft Delete: deleted_at IS NULL

```sql
-- ✅ ĐÚNG
WHERE deleted_at IS NULL

-- ❌ SAI — cấm hard delete dữ liệu nghiệp vụ
DELETE FROM customers WHERE id = @id
```

### 3.4 Timestamp: TIMESTAMPTZ UTC+7

```csharp
// ✅ ĐÚNG
CreatedAt = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified)
    .ToUniversalTime()  // lưu UTC
// Hiển thị: chuyển sang Asia/Ho_Chi_Minh (UTC+7)
```

### 3.5 RabbitMQ Exchange: 1stopshop_event_bus (SHARED)

**Không tạo exchange riêng.** Dùng exchange chung `1stopshop_event_bus` (topic type).

Routing key format: `{service}.{entity}.{action}` (snake_case)

```
✅ erphub.ingestion.customer.created
✅ erphub.ingestion.sales_invoice.posted
✅ erphub.webhook.payment.received
❌ hnh.erp.events (exchange riêng — KHÔNG dùng)
❌ erp.Ingestion.Customer.Created (PascalCase — sai format)
```

### 3.6 API Hub Placement: TRƯỚC Gateway (cho traffic ngoại)

```
✅ External → API Hub → YARP Gateway → microservices
✅ Internal → YARP Gateway → API Hub → ERPNext

❌ Frontend → BFF → API Hub → YARP → microservices (sẽ phá session)
```

### 3.7 Cross-DB Query: CẤM

```sql
-- ❌ SAI — tuyệt đối không cross-database query
SELECT * FROM 1stopshop_core_db.customers c
JOIN erphub_api_db.external_systems e ON c.id = e.customer_id

-- ✅ ĐÚNG — gọi API hoặc consume event
// Gọi: GET https://gateway:8888/v1/customers/{customerId}
// Hoặc: consume event payment.confirmed từ 1stopshop_event_bus
```

---

## 4. Project Structure

```
erp-api-hub/
├── CLAUDE.md                    ← BẠN ĐANG ĐỌNG FILE NÀY
├── BRD-ERP-API-HUB.md           ← Business Requirements
├── FRD-ERP-API-HUB.md           ← Functional Requirements
├── TDD-ERP-API-HUB.md           ← Technical Design Document
├── codex_review_FRD_v1.1_APPROVED.md  ← FRD review đã duyệt
├── ERP_API_HUB_CONTEXT.md       ← Context & background
├── src/
│   └── ERPApiHub/
│       ├── ERPApiHub.API/            ← Minimal API endpoints
│       ├── ERPApiHub.Application/    ← Use cases, CQRS handlers
│       ├── ERPApiHub.Domain/         ← Entities, value objects, events
│       ├── ERPApiHub.Infrastructure/← EF Core, Redis, RabbitMQ
│       └── ERPApiHub.Worker/        ← Background consumers
├── tests/
│   └── ERPApiHub.Tests/
├── docker-compose.yml
└── .env.example
```

---

## 5. Build & Run Commands

```bash
# Restore
dotnet restore

# Build (production)
dotnet build --configuration Release

# Run migrations
dotnet ef database update --project src/ERPApiHub/ERPApiHub.Infrastructure

# Run API
dotnet run --project src/ERPApiHub/ERPApiHub.API --urls http://0.0.0.0:8008

# Run Worker
dotnet run --project src/ERPApiHub/ERPApiHub.Worker

# Run tests
dotnet test

# Docker
docker-compose up -d

# Health check
curl http://localhost:8008/health
```

---

## 6. Keycloak Configuration

```
Realm: HNHTravel-SGN
Endpoint: https://quanna.tail072b2f.ts.net:8443/realms/HNHTravel-SGN
JWT Algorithm: RS256 (validate via JWKS)
Client ID: hnh-erp-hub
Audience: 1stopshop-api
Token Lifetime: 8 hours
Refresh Token: 24 hours

Key Claims:
- sub: user ID
- preferred_username: login name
- email: user email
- realm_access.roles[]: ["SuperAdmin", "Accounting", ...]
- BranchId: tenant ID (ULID)
- DepartmentId: department
- TeamId: team
```

### Test Token

```bash
curl -X POST \
  https://quanna.tail072b2f.ts.net:8443/realms/HNHTravel-SGN/protocol/openid-connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=hnh-erp-hub" \
  -d "client_secret=<SECRET>"
```

---

## 7. RabbitMQ Integration

```
Exchange: 1stopshop_event_bus (topic)
Queues (API Hub subscribe):
  - erphub.ingestion.customer.created
  - erphub.ingestion.sales_invoice.posted
  - erphub.ingestion.payment.confirmed
  - erphub.webhook.payment_link.paid

DLQ:
  - erphub.dlq.ingestion
  - erphub.dlq.webhook

Event Envelope (chuẩn HNH):
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

## 8. Database: erphub_api_db

```
Host: postgres (Docker) / localhost:5452 (dev)
Database: erphub_api_db
Schema: public (default)

Bắt buộc:
- ULID 26 ký tự cho PK
- branch_id NOT NULL (lấy từ JWT BranchId)
- deleted_at TIMESTAMPTZ NULL (soft delete)
- created_at/by, updated_at/by (audit)
- Index: idx_{table}_{cols}
- FK: fk_{table}_{ref}
```

---

## 9. Gotchas & Common Mistakes

1. **Đừng dùng UUID** — HNH dùng ULID. Kiểm tra kỹ mọi migration.
2. **Đừng tạo RabbitMQ exchange riêng** — Dùng `1stopshop_event_bus`.
3. **Đừng tin client gửi branch_id** — Lấy từ JWT claim `BranchId`.
4. **Đừng hard delete** — Dùng `deleted_at` soft delete.
5. **Đừng cross-DB query** — Gọi API hoặc consume event.
6. **Đừng gọi microservice trực tiếp** — Gọi qua YARP Gateway port 8888.
7. **Đừng quên X-Request-ID** — Mọi mutation phải có correlation ID.
8. **Đừng cache PII** — Trừ khi đã mask theo role-based masking rules.
9. **Đừng retry 4xx** — Chỉ retry 5xx/timeout với exponential backoff.
10. **Timestamp UTC+7** — Mọi cross-system compare phải normalize.

---

## 10. Documentation References

| Document | Location |
|----------|----------|
| BRD | `BRD-ERP-API-HUB.md` |
| FRD | `FRD-ERP-API-HUB.md` |
| TDD | `TDD-ERP-API-HUB.md` |
| FRD Review (APPROVED) | `codex_review_FRD_v1.1_APPROVED.md` |
| Context | `ERP_API_HUB_CONTEXT.md` |
| Platform Architecture | Obsidian Vault: `HNH-ERP-API-Hub/Architecture/HNH-TDD-PLAT-001_Platform_Reference_Architecture.md` |
| SOP AI Dev Process | Obsidian Vault: `HNH-ERP-API-Hub/Architecture/SOP_AI_SOFTWARE_DEVELOPMENT_PROCESS.md` |

---

## 11. Environment Variables

```bash
# .env.example
ERPHUB_DB_PASSWORD=<change-me>
REDIS_PASSWORD=<change-me>
RABBIT_USER=erphub
RABBIT_PASS=<change-me>
MASTER_KEY=<32-byte-hex-key>
KEYCLOAK_REALM_URL=https://quanna.tail072b2f.ts.net:8443/realms/HNHTravel-SGN
ERPNEXT_DEFAULT_HOST=erpnext:8080
ASPNETCORE_ENVIRONMENT=Production
```

---

**Last updated:** 2026-05-26  
**Maintained by:** HNH Technical Team