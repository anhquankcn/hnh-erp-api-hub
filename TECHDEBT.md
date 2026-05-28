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

### S6-002: PDPA REST Endpoints ✅ FIXED
- [x] Added DB persistence via `PdpaService`
- [x] Composite key for multiple purposes
- [x] Real timestamps from DB

### S6-001: Polling Fallback Worker ✅ FIXED
- [x] Internal pagination + guard cursor
- [x] Prevent data loss when >100 records share timestamp

## Next Review Items
- S6-004: Tenant Health Check Background Job
- S6-005: Audit Search & Export API
- S6-006: Link-Field Validation
