# Sprint 6 Tech Debt

## Known Issues

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

## Remaining for Sprint 7
1. **S6-003 WARN: Timing Attack** — Unified error message for token validation
2. **S6-003 WARN: Rate Limit Race Condition** — Atomic INCR+EXPIRE
