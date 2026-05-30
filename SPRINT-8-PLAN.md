# Sprint 8 Plan — Audit Search, Webhook Retry & Token Security

**Date:** 2026-05-30  
**Planned Start:** 2026-05-30 (immediately after Sprint 7)  
**Planned End:** 2026-06-13 (2-week sprint)  
**Total Points:** 37 SP  
**Status:** Approved ✅

---

## Sprint 8 Proposal

### Story 1: Audit Log Advanced Search (S8-001) — 8 SP
**Priority:** P1 — High  
**FRD Ref:** FR-AUD-004  
**Owner:** TBD

**Description:**  
Enhance audit log search with advanced filtering capabilities. Current implementation (S6-005) supports basic CSV export and stats. Sprint 8 adds queryable search with composite filters, full-text search on message fields, and paginated results.

**Acceptance Criteria:**
- [ ] GET `/api/v1/audit/search` endpoint with query parameters:
  - `tenantId` (exact match)
  - `userId` (exact match)
  - `actionType` (enum: Create, Update, Delete, Query, Export, Login, etc.)
  - `status` (Success, Failure, Warning)
  - `dateFrom`, `dateTo` (ISO 8601 range)
  - `message` (full-text search, `LIKE '%keyword%'` or `tsvector`, NOT raw ILIKE)
  - `correlationId` (exact match)
- [ ] Composite filters: multiple conditions combined with AND logic
- [ ] Pagination: limit/offset with total count
- [ ] Sorting: by timestamp (default desc), actionType, status
- [ ] Response: paginated list with metadata (total, page, pageSize)
- [ ] Index: PostgreSQL `pg_trgm` GIN index on `message` + composite index on `(tenantId, timestamp)`
- [ ] Performance: p95 < 500ms for 1M records (GIN-indexed LIKE; realistic without tsvector)
- [ ] Graceful degradation: if no search term, return paginated list sorted by time

**Technical Notes:**
- Reuse `AuditSearchService` from S6-005
- Add `AuditSearchRequest` DTO + `AuditSearchResponse`
- Consider PostgreSQL `tsvector` for full-text if message field large
- Reuse existing `AuditLog` entity (no schema change needed)

---

### Story 2: Webhook Retry via RabbitMQ Delayed Queue (S8-002) — 8 SP
**Priority:** P1 — High  
**FRD Ref:** FR-WHK-003  
**Owner:** TBD

**Description:**  
Replace in-memory `Task.Delay` retry in `WebhookDispatcherService` with RabbitMQ delayed message queue. This decouples retry timing from worker process lifetime and enables distributed retry handling.

**Acceptance Criteria:**
- [ ] Configure RabbitMQ `x-delayed-message` exchange (or use TTL + dead-letter)
- [ ] `WebhookDispatcherService` publishes failed deliveries to delayed queue with:
  - Delay: 0s (immediate), 5min (attempt 1), 15min (attempt 2)
  - Delivery attempt counter (max 3 total)
- [ ] `WebhookRetryConsumer` processes delayed messages:
  - Re-dispatch to original subscription
  - Increment attempt counter
  - After 3 failures: mark subscription `failed`, log to `webhook_dlq` table
  - Alert admin via structured log (`alert: webhook_max_retry_exceeded`)
- [ ] Remove `Task.Delay` from `WebhookDispatcherService`
- [ ] Retry timing survives API restart (messages persist in queue)
- [ ] Monitor: queue depth, retry latency, failure rate

**Technical Notes:**
- Requires RabbitMQ plugin `rabbitmq_delayed_message_exchange` OR TTL DLX pattern
- If plugin unavailable, use per-attempt queue with TTL (5min-queue, 15min-queue)
- Update `RabbitMqMessageBus` to support delayed publish
- Consider idempotency: retry must not duplicate if original succeeded
- Define "alert admin": structured log event for log aggregation (Grafana/Splunk) to pick up

---

### Story 3: Token JWKS Validation (S8-003) — 5 SP
**Priority:** P1 — High  
**FRD Ref:** FR-AUTH-001  
**Owner:** TBD

**Description:**  
Replace `ValidateSignature` stub with real JWKS (JSON Web Key Set) validation for RS256 tokens. Fetch and cache Keycloak public keys, validate JWT signature against correct key by `kid` (key ID).

**Acceptance Criteria:**
- [ ] `TokenService.ValidateSignature` supports RS256 via JWKS
- [ ] Fetch JWKS from `{Authority}/.well-known/jwks.json`
- [ ] Cache JWKS keys in memory (TTL 1h) + Redis (TTL 24h)
- [ ] Match JWT `kid` header with JWKS key ID
- [ ] Validate `aud` claim matches configured audience
- [ ] Validate `iss` claim matches Keycloak realm URL
- [ ] Support key rotation (auto-refresh JWKS on `kid` mismatch)
- [ ] HS256: continue using configured secret (backward compatible)
- [ ] Graceful degradation: if JWKS endpoint unreachable (Keycloak down), validate using cached keys (Redis TTL 24h). If cache miss + unreachable → return 503 Service Unavailable with `retry_after: 300`

**Technical Notes:**
- Use `System.IdentityModel.Tokens.Jwt` or manual `RSA` + `JsonWebKey`
- Keycloak JWKS endpoint: `http://localhost:8080/realms/HNHTravel-SGN/protocol/openid-connect/certs`
- Cache invalidation on key rotation event
- JWKS unreachable behavior: return 503, log `jwks_unavailable`, do NOT fail-open (security)

---

### Story 4: Performance Monitoring Dashboard (S8-004) — 8 SP
**Priority:** P2 — Medium  
**FRD Ref:** FR-PERF-001~003 (Performance Requirements)  
**Owner:** TBD

**Description:**  
Implement metrics collection and Grafana dashboard for production monitoring. Export Prometheus-compatible metrics from API and workers.

**Acceptance Criteria:**
- [ ] Prometheus `/metrics` endpoint (already in S5-007, enhance):
  - Request rate/latency histograms per endpoint
  - Cache hit/miss ratio
  - Rate limit throttled count
  - Webhook delivery success/failure/retry counts
  - Token validation failure reasons
  - Queue depth (RabbitMQ)
  - Redis connection pool stats
- [ ] Grafana dashboard JSON: importable dashboard
  - API Overview: RPS, latency p50/p95/p99, error rate
  - Cache Performance: hit ratio, eviction rate, stampede events
  - Webhook Health: delivery rate, failure rate, retry queue depth
  - Rate Limiting: throttled requests, tier distribution
  - Token Security: validation failures, JWKS refresh events
- [ ] Alert rules (Grafana alert or Prometheus alertmanager):
  - Error rate > 5% for 5min
  - p95 latency > 500ms for 10min
  - Webhook failure rate > 20% for 15min
  - Redis down for > 1min
  - Queue depth > 1000 for 10min
- [ ] **Infrastructure assumption:** Prometheus + Grafana already deployed (HNH DevOps). API Hub only exports `/metrics`

**Technical Notes:**
- Use `prometheus-net` package (already referenced)
- Dashboard as code: store JSON in `monitoring/grafana/`
- Alert rules in `monitoring/prometheus/alerts.yml`

---

### Story 5: API Documentation & SDK Generation (S8-005) — 8 SP
**Priority:** P2 — Medium  
**FRD Ref:** N/A (Developer Experience)  
**Owner:** TBD

**Description:**  
Generate OpenAPI 3.0 spec from existing controllers and produce client SDKs for common languages.

**Acceptance Criteria:**
- [ ] OpenAPI spec at `/swagger/v1/swagger.json` (Swashbuckle already configured)
- [ ] Enhance spec with:
  - Authentication schemes (Bearer JWT, API Key)
  - Rate limit headers (`X-RateLimit-Limit`, `X-RateLimit-Remaining`)
  - Error response schemas
  - Example requests/responses
- [ ] SDK Generation:
  - TypeScript/Node.js client (auto-generated from OpenAPI spec)
  - Python client (auto-generated from OpenAPI spec)
  - Postman collection export (auto-generated from OpenAPI spec)
- [ ] Developer Portal: static HTML page with:
  - API overview
  - Authentication guide
  - Rate limiting explanation
  - Webhook integration guide
  - Changelog
- [ ] **Scope limit:** If time runs out, drop Developer Portal (keep SDKs + Postman)

**Technical Notes:**
- Use `Swashbuckle.AspNetCore` (already in project)
- NSwag or OpenAPI Generator for SDK
- Developer portal: simple static site or GitHub Pages

---

## FRD Coverage Assessment

| Module | Total FRs | Implemented | Remaining |
|--------|-----------|-------------|-----------|
| Authentication | 5 | 5 | 0 (JWKS enhancement = above FRD) |
| Ingestion | 10 | 10 | 0 |
| Query | 7 | 7 | 0 |
| Webhook | 5 | 4 | 1 (FR-WHK-003 delayed queue) |
| Audit | 5 | 4 | 1 (FR-AUD-004 advanced search) |
| Rate Limiting | 5 | 5 | 0 |
| Configuration | 4 | 4 | 0 |
| Vietnam Compliance | 6 | 6 | 0 |
| **Total** | **51** | **49** | **2** |

**FRD Compliance: 96%** (49/51 FRs)

---

## Risk Assessment

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| RabbitMQ delayed queue plugin not available | S8-002 blocked | Medium | Fallback to TTL DLX pattern; test in CI |
| JWKS endpoint unavailable (Keycloak down) | Token validation fails | Medium | Cache JWKS in Redis with long TTL; graceful degradation |
| Audit search slow on large datasets | Performance degradation | Medium | PostgreSQL indexing strategy; consider read replica |
| SDK generation complex | S8-005 scope creep | Low | Limit to TypeScript + Python only |
| Grafana setup requires infrastructure | S8-004 blocked | Low | Provide dashboard JSON only; ops team deploys |

---

## Sprint 8 Dependencies

```
S8-003 (Token JWKS) ──► S8-004 (Performance) ──► Metrics for token validation
S8-002 (Webhook Retry) ──► S8-004 (Performance) ──► Metrics for webhook health
S8-001 (Audit Search) ──► S8-005 (Docs) ──► Include search endpoint in OpenAPI
```

**No hard blockers** — stories can be developed in parallel.

---

## Definition of Done (Sprint 8)

- [ ] All 5 stories implemented and reviewed
- [ ] Build passes: 0 errors
- [ ] All FRD requirements covered (96% → 100%)
- [ ] Gemini/Codex review: no BLOCKERs
- [ ] Documentation updated (README, TECHDEBT)
- [ ] Branch merged to main

---

## Story Point Breakdown

| Story | Points | Complexity | Uncertainty | Risk |
|-------|--------|-----------|-------------|------|
| S8-001 Audit Search | 8 | Medium | Low | Low |
| S8-002 Webhook Retry | 8 | Medium | Medium | Medium |
| S8-003 Token JWKS | 5 | Low | Low | Low |
| S8-004 Performance Dashboard | 8 | Medium | Medium | Low |
| S8-005 API Docs + SDK | 8 | Medium | Low | Low |
| **TOTAL** | **37 SP** | | | |

---

## Approval

**Approved by:** Jin Kim  
**Date:** 2026-05-30  
**Notes:** Proceed with implementation immediately after Sprint 7 merge.
