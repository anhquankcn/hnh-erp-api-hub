# Codex Re-review — FRD-ERP-API-HUB-v1.1 (Post-Fix)
**Reviewer:** Codex (Independent Reviewer #2)  
**Date:** 2026-05-26  
**Conclusion:** APPROVED (with minor notes)

## Summary
Tất cả 3 blockers từ review ban đầu đã được xử lý trong commit `ee125a7`. FRD v1.1 đã sẵn sàng cho implementation phase.

---

## Blockers — All Resolved ✅

| # | Issue | Fix Applied | Verified |
|---|-------|-------------|----------|
| 1 | **Webhook approach không thực tế với ERPNext v15** | Thêm §6.2 mô tả hybrid approach: Polling + Frappe Server Script (không sửa core) + CDC (future). Thêm FR-WHK-001b chi tiết Server Script setup. | ✅ |
| 2 | **Tenant Resolution Service thiếu** | Thêm FR-AUTH-004b với `tenant_registry` schema, resolution flow, health checks. | ✅ |
| 3 | **Thiếu Vietnam Compliance** | Thêm §13 Vietnam Compliance Requirements đầy đủ: MST validation, Invoice sequence (Thông tư 78), PDPA (Nghị định 13), tour guide license. | ✅ |

## Warnings — All Resolved ✅

| # | Issue | Fix Applied | Verified |
|---|-------|-------------|----------|
| 4 | **SLA Sync ingestion P99 < 2s không thực tế** | Chia thành "simple" (<2s) và "complex*" (<5s), thêm note về ERPNext background jobs. | ✅ |
| 5 | **Thiếu ERPNext-specific transformation** | Thêm FR-ING-003b: Child table mapping, Link field validation, Naming series, Custom fields. | ✅ |
| 6 | **UPSERT race condition** | Thêm FR-ING-004b với RedLock, idempotency key, retry options. | ✅ |
| 7 | **Background worker architecture chưa rõ** | Mô tả trong §6.2 — API Hub có thể chạy workers dạng sidecar hoặc tách deployment. | ✅ |
| 8 | **Kong DB-less mode sync** | Sửa FR-RLM-005: Declarative config via CI/CD pipeline, không dùng Admin API. | ✅ |
| 9 | **RFC 7807 `errors` array non-standard** | Vẫn còn nhưng đã ghi chú "RFC 7807 with custom extensions" — acceptable cho implementation. | ✅ |

## Nits — Partially Fixed

| # | Issue | Status | Notes |
|---|-------|--------|-------|
| 10 | Giải thích lý do chọn .NET Core 8 | Not Fixed | Cần thêm 1-2 dòng trong §2.2 hoặc Context |
| 11 | Booking status values | ✅ Fixed | Đã thêm note về Select field options |
| 12 | PostgreSQL syntax `gen_random_uuid()` | Not Fixed | Cần chuyển sang `UUID()` hoặc app-generated |
| 13 | Header `X-API-Version` confusion | ✅ Fixed | Đã xóa header, chỉ dùng URL versioning |

## Minor Notes Remaining

1. **§2.2 Component Interaction**: Nên thêm note giải thích lý do chọn .NET Core 8 cho API Hub (team expertise, performance, ecosystem middleware).

2. **Appendix A Database Schema**: `gen_random_uuid()` là PostgreSQL 13+ syntax. Nếu API Hub dùng PostgreSQL < 13, cần đổi thành `uuid_generate_v4()` (requires uuid-ossp extension) hoặc app-generated UUID.

3. **§10.3 Error Format**: `errors` array trong RFC 7807 response là extension non-standard. Nếu muốn strict RFC 7807, nên gói validation errors trong `extensions` object:
   ```json
   {
     "type": "...",
     "status": 400,
     "detail": "Validation failed",
     "extensions": {
       "errors": [{"field": "...", "message": "..."}]
     }
   }
   ```
   Tuy nhiên, đây là nit-level và không ảnh hưởng implementation.

## Positive Notes

- **Vietnam Compliance §13:** Excellent addition. Bao quát MST, hóa đơn điện tử, PDPA — rất cần thiết cho doanh nghiệp VN.
- **Tenant Resolution Service:** Thiết kế production-ready với health checks và Redis caching.
- **UPSERT RedLock:** Giải pháp distributed lock phù hợp cho race condition.
- **Kong DB-less sync:** Realistic approach với CI/CD pipeline.
- **Webhook hybrid approach:** Practical — polling + minimal Server Script là feasible.

---

## Final Verdict

**APPROVED** ✅

FRD v1.1 đã đáp ứng đủ requirements cho development team bắt tay implement. Các blockers nghiêm trọng đã được xử lý. Còn lại 2-3 nits nhỏ không ảnh hưởng đến kiến trúc tổng thể.

**Recommended Next Steps:**
1. Merge FRD v1.1 vào branch `feat/erp-api-hub`
2. Proceed với TDD (Technical Design Document)
3. Định nghĩa schema migration (PostgreSQL) cho `tenant_registry`, `external_systems`, `webhook_subscriptions`

**Reviewer Signature:** Codex  
**Date:** 2026-05-26
