# ERP API Hub — Context Update

## Architecture Reality Check (Updated 2026-05-26)

### ERP Core — ERPNext v15 (Frappe Framework)
- **Path:** `/Users/quanna/Sources/hnherp-docker/`
- **Stack:** Python (Frappe/ERPNext), MariaDB 10.6, Redis
- **Ports:**
  - Frontend (nginx): 8080
  - Backend: 8000
  - WebSocket: 9000
- **Built-in REST API:** ERPNext có sẵn REST API qua `/api/resource/{Doctype}`
- **Auth:** API Key/Secret hoặc OAuth2
- **Custom HRMS App:** Tích hợp HRMS module

### ERP API Hub Design — Service Riêng Song Song

**Vai trò:** Wrapper/Gateway trước ERPNext API
- Abstract ERPNext complexity cho external systems
- Quản lý external connections (multi-tenant, multi-system)
- Transform/mapping data formats
- Rate limiting, audit logging, retry
- Không sửa đổi ERPNext core

**Kiến trúc đề xuất:**
```
External Systems
       │
       ▼
   [Kong Gateway]
       │
       ▼
[ERP API Hub Service]
       │
       ├──► [ERPNext REST API] (Read/Write)
       │
       ├──► [RabbitMQ] (Async events)
       │
       └──► [PostgreSQL] (Audit, config, state)
```

### ERPNext REST API Reference

**Authentication:**
- API Key + API Secret trong header
- Hoặc OAuth2 token

**Endpoints:**
- `GET /api/resource/{Doctype}` — List documents
- `GET /api/resource/{Doctype}/{name}` — Get single document
- `POST /api/resource/{Doctype}` — Create document
- `PUT /api/resource/{Doctype}/{name}` — Update document
- `DELETE /api/resource/{Doctype}/{name}` — Delete document
- `GET /api/method/{method}` — Call whitelisted method
- `POST /api/method/{method}` — Call whitelisted method

**Key Doctypes cho Travel Industry:**
- Customer, Contact, Address
- Sales Order, Sales Invoice, Payment Entry
- Item, Item Group, Warehouse
- Employee, Attendance, Salary Slip
- Project, Task, Timesheet
- Booking (custom), Tour (custom)

**Important:** ERPNext API là CRUD-style, không phải domain-driven API. API Hub cần translate từ domain API (e.g., "CreateBooking") sang ERPNext CRUD operations.

## Multi-Tenancy

ERPNext hỗ trợ multi-tenancy qua "sites". Mỗi branch có thể là 1 site:
- `frontend` site = head office
- Có thể thêm sites cho branches khác

API Hub cần:
- Route requests đến đúng ERPNext site
- Tenant context trong mọi API call
- Data isolation giữa branches

## Keycloak Integration

ERPNext không native hỗ trợ Keycloak. Các option:
1. **Option A:** API Hub xác thực qua Keycloak, sau đó dùng API Key để gọi ERPNext
2. **Option B:** Tích hợp Keycloak SSO vào ERPNext (cần custom Frappe app)
3. **Option C:** API Hub làm "auth proxy" — translate Keycloak token → ERPNext API Key

→ Đề xuất: **Option C** — đơn giản, không sửa ERPNext

## API Hub Service Specs

**Tech Stack:**
- .NET Core 8 (hoặc Node.js/NestJS nếu đồng bộ với HRMS)
- PostgreSQL (audit log, config, external system registry)
- Redis (cache, rate limiting)
- RabbitMQ (event queue)
- Docker + Docker Compose

**Modules:**
1. **Auth Module:** Keycloak token validation, ERPNext API key mapping
2. **Ingestion Module:** Receive external data, validate, transform, push to ERPNext
3. **Query Module:** Proxy queries to ERPNext, cache responses
4. **Webhook Module:** Subscribe to ERPNext events, push to external systems
5. **Audit Module:** Log all API calls, data changes
6. **Config Module:** External system registry, mapping rules, rate limits
7. **Retry/DLQ Module:** Failed message handling

## Document Status

| Document | Status | Path |
|----------|--------|------|
| BRD | ✅ Done | `/Users/quanna/erp-api-hub/BRD-ERP-API-HUB.md` |
| FRD | 🔄 In Progress | `/Users/quanna/erp-api-hub/FRD-ERP-API-HUB.md` |
| TDD | ⏳ Pending | `/Users/quanna/erp-api-hub/TDD-ERP-API-HUB.md` |
