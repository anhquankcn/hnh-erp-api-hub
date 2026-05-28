# Daily Log — 2026-05-28

## SPRINT-6-PLAN.md (Option C - Hybrid)

**Sprint Goal:** Close remaining FRD gaps to make the API Hub operationally complete: webhook reliability, compliance endpoints, and token lifecycle management.

**Total Story Points:** 37 SP (6 stories)
**Duration:** 2 weeks
**Team Capacity:** 2 devs

---

## Gap Analysis

| # | FRD Reference | Gap Description | Sprint Story |
|---|---------------|-------------------|--------------|
| 1 | §16.8 (Fallback) | No polling mechanism for ERPNext events | S6-001 |
| 2 | §13 (PDPA) | PDPA services exist but not exposed as REST endpoints | S6-002 |
| 3 | §3.2 (Token Lifecycle) | Token refresh/revoke/verify not routed | S6-003 |
| 4 | §15 (Health) | Tenant health check background job not scheduled | S6-004 |
| 5 | §7 (Audit) | Audit search/export missing bulk operations | S6-005 |
| 6 | §4.2 (Link Validation) | No validation for ERPNext link-field references | S6-006 |

---

## Stories

### S6-001: Polling Fallback Worker (8 SP)
**As a** system operator, **I want** the API Hub to poll ERPNext for document changes when webhooks fail, **so that** no events are lost.

**Acceptance Criteria:**
- Background job polls `GET /api/resource/{Doctype}?filters=[["modified", ">", "{last_cursor}"]]` every 30s (critical doctypes) or 5min (others)
- Cursor persisted in Redis per doctype per tenant
- Graceful degradation: if webhook fails 3 times, switch to polling mode
- Configurable doctype list via `appsettings.json`

**Files:** `PollingWorker.cs`, `PollingOptions.cs`, `DoctypePollingRegistry.cs`

---

### S6-002: PDPA REST Endpoints (8 SP)
**As a** data subject, **I want** to submit consent and erasure requests via REST API, **so that** I can exercise my PDPA rights.

**Acceptance Criteria:**
- `POST /api/v2/consent` — submit consent
- `POST /api/v2/consent/{id}/withdraw` — withdraw consent
- `POST /api/v2/erasure-request` — submit erasure request
- `GET /api/v2/consent/status/{subjectId}` — check consent status
- `GET /api/v2/erasure-request/{id}/status` — check erasure status
- All endpoints use existing `ConsentService` and `ErasureRequestService`
- Rate limit: 10 req/min per subject

**Files:** `ConsentController.cs`, `ErasureRequestController.cs`, `PdpaApiExtensions.cs`

---

### S6-003: Token Lifecycle Management (5 SP)
**As an** external system, **I want** to refresh, revoke, and verify my JWT tokens, **so that** I can manage my session lifecycle.

**Acceptance Criteria:**
- `POST /api/v2/auth/refresh` — refresh token (returns new JWT)
- `POST /api/v2/auth/revoke` — revoke token (blacklist in Redis)
- `POST /api/v2/auth/verify` — verify token validity
- `GET /api/v2/auth/introspect` — return token claims and expiry
- Blacklist stored in Redis with TTL matching token expiry

**Files:** `AuthController.cs`, `TokenService.cs`, `TokenBlacklistService.cs`

---

### S6-004: Tenant Health Check Background Job (5 SP)
**As a** system operator, **I want** periodic health checks for each tenant's ERPNext instance, **so that** I can detect outages proactively.

**Acceptance Criteria:**
- Hangfire recurring job: every 5 minutes
- Check connectivity to each tenant's `erpnext_host`
- Store results in PostgreSQL (`TenantHealthCheck` table)
- Alert if health check fails 3 consecutive times
- Dashboard endpoint: `GET /api/v2/health/tenants`

**Files:** `TenantHealthCheckJob.cs`, `TenantHealthService.cs`, `TenantHealthController.cs`, migration

---

### S6-005: Audit Search & Export API (8 SP)
**As a** compliance officer, **I want** to search and export audit logs with filters, **so that** I can respond to regulatory requests.

**Acceptance Criteria:**
- `GET /api/v2/audit/search?entityType=&entityId=&action=&fromDate=&toDate=&page=&pageSize=`
- `POST /api/v2/audit/export` — export filtered results to CSV/JSON
- Support date range, entity type, action type filters
- Export async via Hangfire job, notify via webhook when ready
- PII masking applied to export output

**Files:** `AuditSearchController.cs`, `AuditSearchService.cs`, `AuditExportJob.cs`, `AuditExportDto.cs`

---

### S6-006: Link-Field Validation (3 SP)
**As an** API consumer, **I want** validation that link-field references in my ingestion payload exist in ERPNext, **so that** I don't create orphaned records.

**Acceptance Criteria:**
- Validate `customer`, `supplier`, `item`, `warehouse` references before ingestion
- Check existence via `GET /api/resource/{Doctype}/{name}`
- Cache results for 5 minutes to reduce ERPNext calls
- Return 400 with detailed error listing invalid references

**Files:** `LinkFieldValidator.cs`, `LinkFieldValidationMiddleware.cs`

---

## Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| ERPNext polling rate limits | Medium | High | Implement backoff, cache cursors |
| PDPA endpoints expose PII | Low | High | PII masking middleware, audit all requests |
| Token blacklist memory pressure | Medium | Medium | Redis TTL auto-expiry |
| Tenant health check noisy alerts | Medium | Low | Configurable threshold (3 failures) |

---

## Dependencies

- **S6-001** depends on: S5-004 (health checks), S4-002 (Hangfire)
- **S6-002** depends on: S5-002 (PDPA services)
- **S6-003** depends on: S2-001 (JWT auth)
- **S6-004** depends on: S5-004 (health checks), S4-002 (Hangfire)
- **S6-005** depends on: S5-003 (audit retention)
- **S6-006** depends on: S2-002 (ingestion), S4-001 (caching)

---

## Trade-offs

| Aspect | Option A (Tech) | Option B (Business) | **Option C (Hybrid)** |
|--------|-----------------|---------------------|-----------------------|
| **Speed** | Fastest (39 SP) | Medium (34 SP) | Medium (37 SP) |
| **Completeness** | Low | High | **High** |
| **FRD Coverage** | ~60% | ~70% | **~85%** |
| **Technical Debt** | Low | Medium | **Medium** |
| **User Value** | Low | High | **High** |
| **Recommended** | No | No | **Yes** |

Option C balances technical robustness (polling fallback, token lifecycle) with business compliance (PDPA endpoints, audit search) while keeping scope manageable for 2 weeks.

---

## Sprint Backlog Summary

| Story | SP | Focus |
|-------|-----|-------|
| S6-001 | 8 | Reliability |
| S6-002 | 8 | Compliance |
| S6-003 | 5 | Security |
| S6-004 | 5 | Operations |
| S6-005 | 8 | Compliance |
| S6-006 | 3 | Data Quality |
| **Total** | **37** | |
