# Sprint 7 Tech Debt

## Sprint 7 — COMPLETE ✅ (2026-05-29)

All Sprint 7 stories implemented, reviewed, and merged to `main`.

### S7 Stories Completed

| Story | Feature | Points | Status |
|-------|---------|--------|--------|
| S7-001 | Webhook Dispatcher + Management API | 13 | ✅ Done |
| S7-002 | Frappe Event Ingestion | 8 | ✅ Done |
| S7-003 | Atomic Rate Limiting | 8 | ✅ Done |
| S7-004 | Token Security Fix | 5 | ✅ Done |
| S7-005 | Query Cache Invalidation | 8 | ✅ Done |
| **TOTAL** | | **42 SP** | |

### S7 Security Fixes Delivered

- [x] S7-001: SSRF protection (private IP block, HTTPS required, redirect validation)
- [x] S7-001: Delivery dedup via Redis `(eventId, subscriptionId)` TTL 24h
- [x] S7-001: HMAC signature with timestamp + delivery_id
- [x] S7-002: HMAC validation + timestamp anti-replay + IP restriction
- [x] S7-003: Redis Lua sliding window (ZADD + ZREMRANGEBYSCORE + ZCARD)
- [x] S7-003: EVALSHA with NOSCRIPT fallback
- [x] S7-003: Per-tier limits (TIER_1 10K/min, TIER_2 1K/min, TIER_3 100/min)
- [x] S7-003: Token bucket burst algorithm (Lua)
- [x] S7-003: Rate limit by Client IP for unauthenticated requests
- [x] S7-004: Anti-timing attack (unified "Invalid token" message)
- [x] S7-004: O(N) token scan → O(1) SHA-256 Redis hash lookup
- [x] S7-004: Token bucket burst algorithm
- [x] S7-005: Cache stampede prevention (KeyedSemaphore)
- [x] S7-005: Tag-based cache invalidation
- [x] S7-005: CacheInvalidationWorker consuming webhook events

### Gemini Review BLOCKERs (Fixed in `b5aa280`)

| BLOCKER | File | Issue | Fix |
|---------|------|-------|-----|
| 1 | `TokenService.cs:86` | `ValidateSignature` stub (always true) | RS256 JWKS + HS256 secret |
| 2 | `TokenService.cs:256` | O(N) linear scan + BCrypt each | SHA-256 Redis hash lookup |
| 3 | `RateLimitMiddleware.cs:46` | Unauthenticated bypass rate limit | Rate-limit by Client IP |
| 4 | `RedisRateLimiter.cs:100` | `CheckBurstAsync` stub (always true) | Token bucket Lua script |

## Remaining Known Issues (Post-Sprint 7)

### WARN: Redis `SCAN` in CacheInvalidationService
- **File**: `CacheInvalidationService.cs:115`
- **Issue**: `server.KeysAsync` maps to Redis `SCAN` — slow on large datasets
- **Fix**: Prefer tag-based O(1) invalidation; monitor Redis latency
- **Priority**: Low (tag-based already primary path)

### WARN: Task.Delay in WebhookDispatcherService retry loop
- **File**: `WebhookDispatcherService.cs:150`
- **Issue**: In-memory delay blocks worker from processing other events
- **Fix**: Offload retries to RabbitMQ delayed queue
- **Priority**: Medium (deferred to Sprint 8)

### WARN: WebhookDelivery payload storage per attempt
- **File**: `WebhookDispatcherService.cs:101`
- **Issue**: Payload stored in DB for every attempt → rapid growth
- **Fix**: Store payload once, reference by ID; or TTL-based storage
- **Priority**: Medium (deferred to Sprint 8)

### WARN: TokenService `aud`/`iss` validation
- **File**: `TokenService.cs:72`
- **Issue**: Missing `aud` (audience) and `iss` (issuer) claim checks
- **Fix**: Add checks per FR-AUTH-001
- **Priority**: Medium (deferred to Sprint 8)

## Deferred to Sprint 8

| Feature | Reason |
|---------|--------|
| Full-text search | Requires read model + sync/tenant partitioning |
| Cross-doctype joins | Requires read model architecture |
| Webhook retry via RabbitMQ delayed queue | Complex queue configuration |
| Webhook payload dedup storage | DB schema change needed |

---

# Sprint 6 Tech Debt (Historical)

[Remaining Sprint 6 content below...]

### S6-003: Token Lifecycle Management

**BLOCKER (Remaining): Plaintext Token in Redis Cache**
- **Issue**: `ApiTokenRecord.PlainToken` property still exists; tokens may be stored in Redis with plaintext exposed
- **Risk**: Memory dump or cache breach exposes active tokens
- **Fix Required**: Remove `PlainToken` from `ApiTokenRecord`; use tuple return `(record, plainToken)` from service; only expose plaintext in immediate HTTP response
- **Effort**: ~30 min
- **Planned For**: Sprint 7 or hotfix

**WARN: Timing Attack in ValidateTokenAsync**
- **Issue**: Different error messages for "not found" vs "revoked" vs "expired"
- **Risk**: Attacker can probe token existence
- **Fix**: Unified error message: "Invalid token"

**WARN: Rate Limit Race Condition**
- **Issue**: `IncrementAsync` + `ExpireAsync` not atomic
- **Risk**: Crash between calls = leaked rate limit key (no expiration)
- **Fix**: Use Redis Lua script or pipeline for atomic INCR+EXPIRE

### S6-002: PDPA REST Endpoints ⚠️ COMPILE ERRORS (11 errors in PdpaService.cs)
- **Issue**: `PdpaService.cs` defines local `ConsentRecord` class conflicting with `ERPApiHub.Domain.Entities.ConsentRecord`
- **Errors**: Type mismatch, missing properties (`Id`, `ExpiresAt`, `IsActive`, `Notes`, `Doctypes`, `CreatedAt`, `UpdatedAt`)
- **Fix Required**: Remove local class, use `ERPApiHub.Domain.Entities.ConsentRecord`; add missing properties to Domain entity or adjust mapping
- **Effort**: ~30 min
- **Planned For**: Sprint 6 hotfix or Sprint 7

### S6-001: Polling Fallback Worker ✅ FIXED
- [x] Internal pagination + guard cursor
- [x] Prevent data loss when >100 records share timestamp

## Pre-existing Compile Errors (unrelated to Sprint 6 features)

### Auth/TokenService.cs (S6-003)
- **Errors**: `ApiTokenRecord` does not contain `PlainToken` (5 errors at lines 159, 191, 219, 322, 352)
- **Fix**: Align `TokenService` with `ApiTokenRecord` entity definition (remove `PlainToken` references)

### Ingestion/IngestionService.cs & Query/QueryService.cs
- **Errors**: `IErpHubRepository` missing `CreateAuditLogAsync` (3 errors)
- **Errors**: `JsonElement` null assignment (2 errors in `InvoiceDeletionGuard.cs`, `IngestionService.cs`)
- **Note**: These are pre-existing API mismatches, not introduced by Sprint 6

## Fixed in Sprint 6
- [x] S6-001: Polling Fallback Worker ✅ FIXED
- [x] S6-002: PDPA REST Endpoints ✅ FIXED (removed duplicate ConsentRecord)
- [x] S6-003: Token Lifecycle Management ✅ FIXED (removed PlainToken references)
- [x] S6-004: Tenant Health Check Background Job ✅ FIXED
- [x] S6-005: Audit Search & Export API ✅ FIXED
- [x] S6-006: Link-Field Validation ✅ FIXED
- [x] Pre-existing: CreateAuditLogAsync + JsonElement null ✅ FIXED

## Planned for Sprint 7 (2026-06-12)

### S7-004: Token Timing Attack Fix
- **Status:** In Progress (Sprint 7)
- **Scope:** Unified "Invalid token" message + constant-time comparison
- **Story:** S7-004

### S7-003: Rate Limiting Hardening
- **Status:** In Progress (Sprint 7)
- **Scope:** Redis Lua script (EVALSHA + NOSCRIPT fallback), sliding window, per-tier limits
- **Story:** S7-003

### S7-001: Webhook Dispatcher + Management API
- **Status:** Planned (Sprint 7)
- **Scope:** Delivery, retry, DLQ, HMAC signature, SSRF protection, delivery dedup, CRUD API
- **Story:** S7-001

### S7-002: Frappe Server Script Integration
- **Status:** Planned (Sprint 7)
- **Scope:** `/internal/v1/events/ingest`, HMAC + IP restriction, mock event generator
- **Story:** S7-002

### S7-005: Query Cache Invalidation
- **Status:** Planned (Sprint 7)
- **Scope:** Cache invalidation on webhook events, stampede prevention
- **Story:** S7-005
- **Deferred to Sprint 8:** Full-text search, cross-doctype joins, read model
