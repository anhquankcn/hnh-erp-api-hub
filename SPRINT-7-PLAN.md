# Sprint 7 Plan: Webhook, Rate Limiting & Security Hardening

**Sprint Goal:** Complete Webhook Module, fix Rate Limiting race conditions, and resolve remaining security issues from Sprint 6.

**Duration:** 2 weeks
**Target Date:** 2026-06-12

**Review Status:** Reviewed by Codex + Gemini (2026-05-29). 8 blockers identified and resolved in this revision.

---

## Stories

### S7-001: Webhook Dispatcher Service + Management API
**Priority:** P0 - Critical
**Points:** 13
**Owner:** TBD

**Requirements (from FRD FR-WHK-001 to FR-WHK-006):**
- Consume events from RabbitMQ exchange `1stopshop_event_bus`
- Route to webhook subscriptions based on event_type filtering
- HTTP POST delivery with HMAC-SHA256 signature (X-Hub-Signature-256 header)
- Sign payload + timestamp + delivery_id (anti-replay)
- Retry policy: 3 retries, exponential backoff (5s, 15min, 30min) per FRD
- DLQ: `erphub.webhook.dlq` with TTL 30 days + max-length policy
- Alert when DLQ depth > 100 messages
- **NEW:** Webhook Management API — CRUD subscriptions via REST

**Security Requirements:**
- SSRF protection: Block private IPs, require HTTPS, validate redirects
- Per-host concurrency limits to prevent fan-out bottleneck
- Delivery dedup: Store `(eventId, subscriptionId)` processed keys (Redis, TTL 24h)

**Acceptance Criteria:**
- [ ] Webhook delivery succeeds for active subscriptions
- [ ] Signature validated by receiving systems (includes timestamp + delivery_id)
- [ ] Failed deliveries retry correctly (3 attempts per FRD)
- [ ] DLQ captures exhausted retries with bounded storage
- [ ] Grafana alert on DLQ depth
- [ ] CRUD API for webhook subscriptions
- [ ] SSRF protection active
- [ ] Delivery dedup prevents duplicate POSTs

**Files to modify:**
- `src/ERPApiHub/ERPApiHub.Application/Webhooks/WebhookDispatcherService.cs` (new)
- `src/ERPApiHub/ERPApiHub.Application/Webhooks/WebhookSubscriptionService.cs` (new)
- `src/ERPApiHub/ERPApiHub.Application/Webhooks/WebhookDeliveryAttempt.cs` (new)
- `src/ERPApiHub/ERPApiHub.Domain/Entities/WebhookSubscription.cs` (exists — verify/modify)
- `src/ERPApiHub/ERPApiHub.Domain/Entities/WebhookDeliveryAttempt.cs` (new)
- `src/ERPApiHub/ERPApiHub.API/Controllers/WebhookSubscriptionController.cs` (new)
- `src/ERPApiHub/ERPApiHub.Worker/Workers/WebhookDeliveryWorker.cs` (new)
- `src/ERPApiHub/ERPApiHub.Infrastructure/Data/ErpHubDbContext.cs`
- `src/ERPApiHub/ERPApiHub.Infrastructure/Data/ErpHubRepository.cs`
- `src/ERPApiHub/ERPApiHub.Application/Abstractions/IErpHubRepository.cs`

---

### S7-002: Frappe Server Script Integration (ERPNext → Webhook)
**Priority:** P0 - Critical
**Points:** 8
**Owner:** TBD
**Dependency:** Unblocks S7-001 (must complete first for integration testing)

**Requirements (from FRD FR-WHK-007):**
- Internal endpoint: `POST /internal/v1/events/ingest` (align với FRD naming)
- Accept events from ERPNext Frappe Server Scripts
- Validate HMAC signature (shared secret + timestamp, anti-replay)
- IP restriction: Chỉ accept from ERPNext internal IP range
- Publish to RabbitMQ `1stopshop_event_bus` with publisher confirms + idempotency
- Routing key: `erphub.webhook.{event_type}`
- Events: `customer_created`, `booking_created`, `invoice_paid`, etc. (FR-WHK-002 taxonomy)
- Fallback: Mock ERPNext Event Generator trong Worker nếu không có admin access

**Acceptance Criteria:**
- [ ] ERPNext Server Script can POST to `/internal/v1/events/ingest`
- [ ] Events published to RabbitMQ with correct routing key
- [ ] Invalid HMAC or stale timestamp returns 401
- [ ] Event payload validated (required: eventType, doctype, name, timestamp)
- [ ] Publisher confirms enabled, idempotent by eventId
- [ ] Mock event generator available for testing

**Files to modify:**
- `src/ERPApiHub/ERPApiHub.API/Controllers/Internal/ErpEventController.cs` (new)
- `src/ERPApiHub/ERPApiHub.Worker/Workers/MockErpEventGenerator.cs` (new, for testing)
- `src/ERPApiHub/ERPApiHub.API/Program.cs` (add route)

---

### S7-003: Atomic Rate Limiting (Redis Lua)
**Priority:** P1 - High
**Points:** 8
**Owner:** TBD

**Requirements (from TECHDEBT.md S6-003 WARN + FRD FR-RLM-001 to FR-RLM-005):**
- Replace `IncrementAsync` + `ExpireAsync` with atomic Lua script
- **Sliding window** via sorted-set (ZADD + ZREMRANGEBYSCORE) hoặc token bucket
- Per-consumer tier limits: TIER_1 (10K/min), TIER_2 (1K/min), TIER_3 (100/min)
- Per-endpoint percentages: Ingestion 50%, Query 100%, Webhook 10%, Admin 5%
- Kong integration: DB-less config generation cho edge rate limiting
- **Redis failure handling:** Fail-open với alert, không block public APIs
- Configurable via `RateLimitOptions`

**Acceptance Criteria:**
- [ ] Rate limit atomic (no race condition)
- [ ] Sliding window accurate (not fixed window)
- [ ] Different tiers applied correctly
- [ ] Redis key has TTL (no leaked keys)
- [ ] Lua script dùng EVALSHA với NOSCRIPT fallback
- [ ] Kong config generated/redeploy path documented
- [ ] Redis failure: fail-open + alert

**Files to modify:**
- `src/ERPApiHub/ERPApiHub.Application/RateLimiting/RedisRateLimiter.cs` (verify exists, modify)
- `src/ERPApiHub/ERPApiHub.Application/RateLimiting/RateLimitOptions.cs` (verify exists, modify)
- `src/ERPApiHub/ERPApiHub.Application/RateLimiting/RateLimitMiddleware.cs` (verify exists, modify)
- `src/ERPApiHub/ERPApiHub.API/Program.cs` (add middleware)

---

### S7-004: Token Validation Timing Attack Fix + Redis Cleanup
**Priority:** P1 - High
**Points:** 5
**Owner:** TBD

**Requirements (from TECHDEBT.md S6-003 WARN + BLOCKER):**
- Unified error message: "Invalid token" for all failure cases (not found, revoked, expired)
- Constant-time comparison: BCrypt verify already constant-time; cần dummy work trên not-found path
- **BLOCKER FIX:** Verify `PlainToken` removed from `ApiTokenRecord`; chỉ expose trong immediate response
- Redis cache: Không store plaintext token; dùng reference/hash lookup

**Acceptance Criteria:**
- [ ] All token validation failures return same status code + body ("Invalid token")
- [ ] No timing difference between different failure paths (unit test)
- [ ] PlainToken không còn trong `ApiTokenRecord` entity
- [ ] Redis cache không chứa plaintext token

**Files to modify:**
- `src/ERPApiHub/ERPApiHub.Application/Auth/TokenService.cs`
- `src/ERPApiHub/ERPApiHub.Domain/Entities/ApiTokenRecord.cs` (verify PlainToken removed)

---

### S7-005: Query Caching + Cache Invalidation
**Priority:** P2 - Medium
**Points:** 8 (reduced from 13)
**Owner:** TBD

**Rationale for scope reduction:** Full-text search + cross-doctype joins require read model với sync, tenant partitioning, invalidation jobs — quá lớn cho 2-week sprint. Defer to Sprint 8.

**Requirements (from FRD FR-QRY-002, FR-QRY-003):**
- Cache invalidation on webhook events (FR-QRY-002: "Cache invalidation: Webhook events or explicit purge")
- **NEW:** Cache stampede prevention (per PERFORMANCE_BASELINE.md)
- Query proxy: Chỉ support ERPNext-native filters/sorts/counts (FR-QRY-001 to FR-QRY-005)
- Không implement full-text search hay cross-doctype joins trong sprint này

**Acceptance Criteria:**
- [ ] Cache invalidated khi webhook event nhận được (matching doctype)
- [ ] Cache stampede: 100 concurrent requests → factory called once
- [ ] Proxy ERPNext filters/sorts/counts correctly
- [ ] p95 < 500ms for cache miss (per NFR-002)

**Files to modify:**
- `src/ERPApiHub/ERPApiHub.Application/Query/QueryService.cs`
- `src/ERPApiHub/ERPApiHub.Application/Cache/CacheInvalidationService.cs` (new)
- `src/ERPApiHub/ERPApiHub.Application/Cache/CacheStampedeGuard.cs` (new)

---

## Technical Tasks

### Database Migrations
- Add `WebhookSubscription` table (verify nếu chưa có)
- Add `WebhookDeliveryAttempt` table (new)
- Add `RateLimitTier` to `ExternalSystem` (verify nếu chưa có)
- ~~Add `SearchIndex` table~~ (deferred to Sprint 8)

### Infrastructure Updates
- Kong config: Add `/api/v1/webhooks/*` routes
- RabbitMQ: Create `erphub.webhook.delivery` queue + `erphub.webhook.dlq`
- Redis: Lua script cho rate limiting (EVALSHA với NOSCRIPT fallback)
- Redis: Delivery dedup keys `(eventId, subscriptionId)` TTL 24h

### Documentation
- Update API documentation (webhook endpoints + management API)
- Update deployment guide (RabbitMQ queue setup, Kong config)
- Update monitoring dashboard (webhook metrics, delivery latency)
- Update TECHDEBT.md (remove resolved items, add new if any)

---

## Dependencies

| Story | Depends On | Blocks |
|-------|-----------|--------|
| S7-002 | — | S7-001 |
| S7-001 | S7-002 | S7-005 |
| S7-003 | — | — |
| S7-004 | — | — |
| S7-005 | S7-001, S7-002 | — |

---

## Risk & Mitigation

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Frappe Server Script requires ERPNext admin access | High | Prepare Mock ERPNext Event Generator; coordinate with ERPNext team |
| Redis Lua script compatibility (cluster/flush) | Medium | Dùng EVALSHA + NOSCRIPT fallback; test on Redis 7 cluster nếu dùng |
| Webhook delivery reliability | High | Circuit breaker + per-host concurrency limits + bounded parallelism |
| Scope creep (full-text search deferred) | Low | Cut S7-005 xuống cache invalidation only; move search to Sprint 8 |
| S7-001 + S7-002 merge conflict | Medium | S7-002 là Week 1 priority; S7-001 integration test cần S7-002 xong |
| Delivery dedup Redis key leak | Low | TTL 24h trên processed keys; monitor Redis memory |
| Kong config drift | Medium | CI/CD pipeline cho kong.yml; config as code |

---

## Definition of Done

- [ ] All stories implemented and code-reviewed (Claude Code + Codex)
- [ ] Build passes (0 errors, 0 warnings)
- [ ] Unit tests > 80% coverage for new code
- [ ] Integration tests pass (RabbitMQ, Redis, PostgreSQL)
- [ ] Security review completed (timing attacks, SSRF, replay protection)
- [ ] Performance benchmarks met (webhook delivery < 2s p95 per NFR-022)
- [ ] Documentation updated (API docs, deployment guide, TECHDEBT.md)
- [ ] TECHDEBT.md updated (new issues tracked, resolved items removed)
- [ ] Audit logging added cho: subscription changes, delivery failures, token validation attempts (PII masked)
