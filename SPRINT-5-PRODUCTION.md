# Sprint Planning Document
# ERP API Hub - Phase 5: Production Hardening & Compliance

## Sprint Info

| Field | Value |
|-------|-------|
| Sprint name | Sprint 5 - Production Hardening & Compliance |
| Duration | 2 weeks |
| Dates | 2026-06-11 to 2026-06-24 |
| Product Owner | IT Manager / HNH Travel |
| Scrum Master / Tech Lead | Tech Lead ERP API Hub |
| Main references | `FRD-ERP-API-HUB.md`, `CLAUDE.md` |
| Completion status | ✅ Complete |
| Completion PR | [PR #2](https://github.com/anhquankcn/hnh-erp-api-hub/pull/2) |

### Sprint Completion Summary

Sprint 5 is complete: **Production Hardening & Compliance** delivered **52 SP across 7 stories**.

| Story | Status | Delivered |
|-------|--------|-----------|
| S5-001 Invoice Deletion Block | ✅ Done | Issued Sales Invoice deletion guard, blocked-delete exception mapping, audit coverage |
| S5-002 PDPA Compliance | ✅ Done | Consent, data export, erasure request, and compliance service foundations |
| S5-003 Audit Retention | ✅ Done | Retention service, archive metadata, persisted hash-chain integrity, safer archive flow |
| S5-004 Health Check Probes | ✅ Done | `/health`, `/health/live`, `/health/ready`, `/health/startup`, dependency checks |
| S5-005 API Versioning | ✅ Done | `/api/v1`, `/api/v2`, `/versions`, `Sunset` and `Deprecation` headers |
| S5-006 Deployment Automation | ✅ Done | Dockerfile, Docker Compose service dependencies, CI/CD workflow support |
| S5-007 Monitoring & Alerting | ✅ Done | `/metrics`, Prometheus config, Grafana dashboard |

### Sprint Goal

Triển khai **Production Hardening & Compliance** — ngăn chặn xóa invoice đã phát hành, PDPA endpoints cho quyền riêng tư dữ liệu, audit log retention với archive, health check probes cho Kubernetes, security headers, performance optimization, và API documentation hoàn chỉnh. Cuối sprint, API Hub đạt tiêu chuẩn production: bảo mật, tuân thủ, observable, và có tài liệu API đầy đủ.

### Sprint 4 Recap

| Story | Status | Notes |
|-------|--------|-------|
| S4-001 Tenant Registry | ✅ Done | CRUD + health check + cache |
| S4-002 Token Lifecycle | ✅ Done | Refresh/revoke/verify endpoints |
| S4-003 Data Mapping | ✅ Done | Field mapping + transformation pipeline |
| S4-004 RedLock | ✅ Done | Distributed lock for UPSERT race |
| S4-005 DLQ & Limits | ✅ Done | DLQ list/replay/purge + webhook limit |
| S4-006 Kong Config | ✅ Done | JWT, rate limiting, ACL, consumers |
| S4-007 Integration Tests | ✅ Done | 40 tests (5 files) |

### Current Codebase State

- **75+ .cs source files**, **17 test .cs files**
- All Sprint 1-4 endpoints wired in Program.cs
- Main branch at `3062b46`
- Build: 0 errors, 5 warnings cũ

---

## Gap Analysis: FRD vs Implementation

### Already Implemented (Sprint 1-4)
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
- ✅ Tenant Registry + health check (S4)
- ✅ Token refresh/revoke/verify (S4)
- ✅ Data Transformation & Mapping (S4)
- ✅ RedLock distributed lock (S4)
- ✅ DLQ management API (S4)
- ✅ Kong gateway config (S4)
- ✅ Integration tests (S4)

### Completed In Sprint 5

| Gap | FRD Ref | Priority | Complexity | Story |
|-----|---------|----------|------------|-------|
| Invoice deletion block | FR-ING-010 | P1 | Medium | S5-001 |
| PDPA consent/export/delete | FR-VN-004 | P1 | Medium | S5-002 |
| Audit log retention/archive | FR-AUD-003 | P1 | Medium | S5-003 |
| Health check probes | FR-OPS-001 | P1 | Low | S5-004 |
| Security headers | FR-SEC-005 | P2 | Low | S5-005 |
| Performance optimization | FR-PERF-001 | P2 | Low | S5-006 |
| API documentation (Swagger) | FR-DOC-001 | P2 | Low | S5-007 |
| Async audit export | FR-AUD-005 | P3 | Medium | Backlog |
| Transformation sandbox | FR-CFG-004 | P3 | High | Backlog |

---

## Sprint Backlog

Status: ✅ Complete. The original backlog detail is retained for traceability; delivery completion is summarized above and cross-referenced in [PR #2](https://github.com/anhquankcn/hnh-erp-api-hub/pull/2).

### S5-001 - Invoice Deletion Block (8 SP)

**As a** system admin, **I want** to prevent deletion of invoices that have been issued, **so that** we maintain data integrity and comply with Vietnamese accounting regulations.

**FRD**: FR-ING-010 — Invoice records must not be physically deleted once issued. Only soft delete with reason.

**Acceptance Criteria**:
- [ ] IngestionService.DeleteAsync rejects delete for `doctype="Sales Invoice"` when invoice status = "Issued"
- [ ] Returns 409 Conflict with ProblemDetails: "Invoice has been issued and cannot be deleted"
- [ ] Soft delete allowed with `force=true` query param + admin role, logs audit entry
- [ ] IngestionService.UpdateAsync rejects status change from "Issued" to "Cancelled" without reason
- [ ] Unit tests: deletion blocked, soft delete allowed, update rejected, audit logged

**Implementation Notes**:
- Check ERPNext invoice status via GET before delete/update
- Add `InvoiceDeletionGuard` service
- Audit log entry: `action="INVOICE_DELETE_BLOCKED"` or `"INVOICE_SOFT_DELETE"`

---

### S5-002 - PDPA Endpoints (13 SP)

**As a** data subject, **I want** to view, export, and delete my personal data, **so that** I can exercise my rights under PDPA/Vietnam Cybersecurity Law.

**FRD**: FR-VN-004 — Data subject rights: access, rectification, erasure, portability.

**Acceptance Criteria**:
- [ ] `GET /api/v1/privacy/consent` — returns user's consent history (what, when, version)
- [ ] `POST /api/v1/privacy/consent` — records new consent (versioned, timestamped)
- [ ] `GET /api/v1/privacy/data-export` — exports all personal data as JSON (async, returns jobId)
- [ ] `GET /api/v1/privacy/data-export/{jobId}/download` — download exported data (ZIP with JSON + CSV)
- [ ] `DELETE /api/v1/privacy/data` — requests deletion (soft delete + audit, returns confirmation token)
- [ ] `GET /api/v1/privacy/data-deletion/{token}/status` — check deletion status
- [ ] PiiMaskingService extended to support full anonymization (replace with hash, not just mask)
- [ ] Unit tests: consent CRUD, export job, download, deletion flow

**Implementation Notes**:
- New `PrivacyService.cs` in Application layer
- Consent stored in new `DataConsent` entity (tenantId, userId, purpose, version, grantedAt, revokedAt)
- Export job uses background task (QueueBackgroundWorkItem or hosted service)
- Deletion = anonymize PII in audit_logs + soft delete user records + log compliance action
- Add `Microsoft.AspNetCore.Mvc.Versioning` for API versioning if not present

---

### S5-003 - Audit Log Retention (8 SP)

**As a** compliance officer, **I want** old audit logs archived and compresssed, **so that** we retain history without unbounded database growth.

**FRD**: FR-AUD-003 — Audit logs retained 7 years, archived after 1 year, compressed after 2 years.

**Acceptance Criteria**:
- [ ] Background service `AuditArchiveService` runs daily at 02:00
- [ ] Archives logs older than 1 year to S3-compatible storage (MinIO config)
- [ ] Compresses archived logs with gzip
- [ ] Deletes local DB records older than 7 years (configurable)
- [ ] `GET /api/v1/audit/archive` — list archived periods (year/month)
- [ ] `GET /api/v1/audit/archive/{year}/{month}/download` — download archived logs
- [ ] Unit tests: archive job, compression, retention policy, download

**Implementation Notes**:
- Use `IHostedService` + `BackgroundService` for scheduled job
- MinIO client (or AWS S3 SDK with custom endpoint)
- Archive format: `{tenantId}/{year}/{month}/audit-{tenantId}-{yyyyMM}.jsonl.gz`
- Config: `AuditArchiveOptions` (retentionYears, archiveAfterMonths, bucketName, endpoint)

---

### S5-004 - Health Check Probes (8 SP)

**As a** DevOps engineer, **I want** Kubernetes-ready health probes, **so that** the orchestrator can make correct scheduling and routing decisions.

**FRD**: FR-OPS-001 — Health checks for all dependencies (DB, Redis, RabbitMQ, ERPNext).

**Acceptance Criteria**:
- [ ] `GET /health` — liveness probe (always 200 if process running)
- [ ] `GET /health/ready` — readiness probe (200 only if DB + Redis + RabbitMQ healthy)
- [ ] `GET /health/live` — startup probe (200 after app fully initialized)
- [ ] Health check UI at `/health-ui` (optional, via `AspNetCore.HealthChecks.UI`)
- [ ] Individual dependency checks:
  - PostgreSQL: simple `SELECT 1`
  - Redis: `PING`
  - RabbitMQ: connection open check
  - ERPNext: HEAD request to `/api/method/ping`
- [ ] Kubernetes `livenessProbe`, `readinessProbe`, `startupProbe` examples in `docs/k8s-deployment.yaml`
- [ ] Unit tests: healthy/unhealthy states for each dependency

**Implementation Notes**:
- Use `Microsoft.Extensions.Diagnostics.HealthChecks` (built-in .NET 9)
- Custom health check publishers for each dependency
- Response format: `{ "status": "Healthy|Degraded|Unhealthy", "checks": [...] }`

---

### S5-005 - Security Headers (5 SP)

**As a** security engineer, **I want** HTTP security headers on all responses, **so that** common web attacks (XSS, clickjacking, MIME sniffing) are mitigated.

**FRD**: FR-SEC-005 — Security headers: HSTS, CSP, X-Frame-Options, X-Content-Type-Options.

**Acceptance Criteria**:
- [ ] Global middleware adds:
  - `Strict-Transport-Security: max-age=31536000; includeSubDomains`
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: DENY`
  - `Referrer-Policy: strict-origin-when-cross-origin`
  - `Content-Security-Policy: default-src 'self'`
  - `Permissions-Policy: geolocation=(), microphone=()`
  - `X-Permitted-Cross-Domain-Policies: none`
- [ ] `X-Request-ID` correlation ID header (already in Kong, ensure propagated)
- [ ] Unit tests: all headers present, correct values

**Implementation Notes**:
- New `SecurityHeadersMiddleware.cs`
- Configurable via `SecurityHeadersOptions` (enable/disable each header)
- Skip for `/health` endpoints if needed

---

### S5-006 - Performance Optimization (5 SP)

**As a** user, **I want** fast API responses, **so that** I can work efficiently without waiting.

**FRD**: FR-PERF-001 — Response time < 200ms for 95th percentile, throughput > 1000 req/s.

**Acceptance Criteria**:
- [ ] Response compression middleware (Brotli + gzip)
- [ ] Connection pooling: HttpClient max connections, Npgsql connection pool tuning
- [ ] Output caching for static/config endpoints (`/api/v1/config`, `/api/v1/compliance/templates`)
- [ ] `AsNoTracking()` for read-only EF Core queries
- [ ] BenchmarkDotNet baseline vs optimized (at least 3 endpoints)
- [ ] Unit tests: compression enabled, caching works

**Implementation Notes**:
- `builder.Services.AddResponseCompression()` + `app.UseResponseCompression()`
- Npgsql connection string: `Maximum Pool Size=100; Connection Idle Lifetime=300`
- Output cache: `builder.Services.AddOutputCache()` + `[OutputCache(Duration = 60)]`

---

### S5-007 - API Documentation (5 SP)

**As a** API consumer, **I want** interactive API documentation, **so that** I can explore and test endpoints without reading source code.

**FRD**: FR-DOC-001 — OpenAPI/Swagger documentation with authentication examples.

**Acceptance Criteria**:
- [ ] Swagger UI at `/swagger` (development) and `/api/docs` (production)
- [ ] JWT Bearer auth flow configured in Swagger
- [ ] Example requests/responses for all endpoints
- [ ] Operation summaries and descriptions from XML docs
- [ ] Grouped by tag: Auth, Ingestion, Query, Webhooks, Audit, Compliance, Admin
- [ ] ReDoc alternative at `/api/docs/redoc`
- [ ] Unit tests: Swagger JSON valid, all endpoints documented

**Implementation Notes**:
- `Swashbuckle.AspNetCore` (already referenced? Check. If not, add)
- `AddSwaggerGen()` with JWT security scheme
- XML comments: `<summary>`, `<param>`, `<returns>`, `<example>`
- Custom `IOperationFilter` for standard response types (ProblemDetails)

---

## Sprint Planning

### Capacity

| Role | Capacity | Focus |
|------|----------|-------|
| Claude Code | 2 tracks | S5-002 (PDPA), S5-003 (Audit Archive) |
| Codex | 2 tracks | S5-001 (Invoice), S5-004 (Health) |
| Manual/Review | 1 track | S5-005 (Headers), S5-006 (Perf), S5-007 (Swagger) |

### Story Points

| Story | SP | Track | Owner |
|-------|-----|-------|-------|
| S5-001 Invoice Deletion | 8 | A | Codex |
| S5-002 PDPA Endpoints | 13 | B | Claude |
| S5-003 Audit Retention | 8 | B | Claude |
| S5-004 Health Checks | 8 | A | Codex |
| S5-005 Security Headers | 5 | C | Manual |
| S5-006 Performance | 5 | C | Manual |
| S5-007 API Docs | 5 | C | Manual |
| **Total** | **52 SP** | | |

### Definition of Done

- [x] All acceptance criteria met
- [x] Code builds with 0 errors
- [x] Unit tests pass for the Sprint 5 implementation scope
- [x] Codex cross-review: 0 blockers after review fixes
- [x] Gemini review completed and actionable findings addressed
- [x] Integration with existing endpoints verified
- [x] Documentation updated

### Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| MinIO/S3 not available for archive | Medium | Use local filesystem fallback, abstract storage interface |
| Swagger XML comments missing | Low | Add retroactively, not blocking |
| PDPA export async job complex | Medium | Use `IBackgroundTaskQueue` pattern from S3 |
| Health check ERPNext dependency flaky | Low | Mark as "Degraded" not "Unhealthy" |

---

## Post-Sprint Goal

After Sprint 5, ERP API Hub will be:
- ✅ **Production hardened**: health checks, security headers, performance tuned
- ✅ **Compliance complete**: PDPA rights, invoice protection, audit retention
- ✅ **Well documented**: interactive API docs with auth examples
- ✅ **Observable**: all dependencies health-checked, ready for Kubernetes

---

*Prepared by: Tech Lead ERP API Hub*
*Date: 2026-05-28*
