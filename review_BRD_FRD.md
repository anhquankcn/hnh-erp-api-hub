# Review: BRD & FRD — ERP API Hub

**Reviewer:** Lam (tech lead) + Claude Code  
**Date:** 2026-05-26  
**Documents:** BRD-ERP-API-HUB.md · FRD-ERP-API-HUB.md  
**Reference:** HNH-TDD-PLAT-001 (Platform Reference Architecture)

---

## BRD Review

### 🔴 BLOCKERS (0)

Không có blocker.

### 🟡 WARNINGS (5)

| # | Severity | Section | Mô tả | Fix |
|---|----------|---------|-------|-----|
| W1 | warn | §1.1, §1.3 | BRD mô tả ERP API Hub như "cổng kết nối giữa external systems và ERP" — thiếu Core Master Data paradigm. Nên nhấn mạnh ERP là SSoT. | Cập nhật §1.1 và §1.3 để nhấn mạnh: "ERPNext là Core Master Data — mọi hệ thống push data vào ERP, pull data từ ERP" |
| W2 | warn | §4.1 Phase 1 | "Mở rộng Kong configuration" — cần làm rõ Kong là public-facing (port 8000), riêng với YARP internal (port 8888) | Thêm ghi chú: "Kong (public :8000) cho external partners, YARP (internal :8888) cho 1StopShop" |
| W3 | warn | §5 UC-02 | "API Hub gọi CrmService để cập nhật" — nhưng theo Core Master Data paradigm, CRM push data → ERP, không phải API Hub gọi CrmService | Sửa flow: CRM push customer data → Kong → API Hub → ERPNext lưu master data |
| W4 | warn | §6.1 C1 | ".NET Core 8" — kiến trúc thực tế là .NET 9 | Sửa C1 thành ".NET 9" |
| W5 | warn | §6.1 C4 | "Kong DB-less mode — config phải declarative" — đúng nhưng cần thêm context: Kong cho public, YARP cho internal | Thêm: "Kong DB-less (public) + YARP (internal) — cần maintain 2 gateway configs" |

### 🟢 NITS (3)

| # | Section | Mô tả |
|---|---------|-------|
| N1 | Appendix A | Glossary thiếu: ULID, Core Master Data, YARP |
| N2 | §3.2 | External Stakeholders đa số TBD — cần xác minh sớm |
| N3 | §9.2 | Timeline khá aggressive (6 tháng total) — nên thêm buffer |

### ✅ BRD Verdict: **APPROVED with warnings**

BRD tổng thể tốt, rõ ràng, có business context đầy đủ. Cần cập nhật:
1. Core Master Data paradigm (W1)
2. Dual-gateway architecture (W2, W5)
3. UC-02 flow direction (W3)
4. .NET version (W4)

---

## FRD Review

### 🔴 BLOCKERS (3)

| # | Section | Mô tả | Fix |
|---|---------|-------|-----|
| B1 | §2 System Overview diagram | Diagram vẫn còn lỗi: hiển thị "Kong (Public:8000) ←→ YARP API Gateway" trên cùng 1 box, gây nhầm lẫn. Kong và YARP là 2 gateway riêng biệt. | Tách Kong và YARP thành 2 box riêng. Kong (public :8000) ở trên, YARP (internal :8888) ở dưới. |
| B2 | §4.2 FR-ING-005 message format | Message ID dùng `ULID` đúng rồi, nhưng vẫn còn "event_id" thay vì "eventId" theo chuẩn envelope §7.4 của PLAT-001. Cần nhất quán. | Đổi `event_id` → `eventId`, `event_type` → `eventType`, `source_service` → `source` để match envelope chuẩn. |
| B3 | §11.1 RabbitMQ exchange | Routing key format cần nhất quán. FRD dùng `erphub.ingestion.customer.created` (3-level) nhưng PLAT-001 dùng `{service}.{entity}.{action}` (3-level snake_case). Cần confirm: `erphub.ingestion.customer.created` có hợp lệ không? | Nếu theo PLAT-001 thì nên là `erphub.customer.created` (bỏ "ingestion"). Hoặc thêm routing key pattern explanation. |

### 🟡 WARNINGS (7)

| # | Section | Mô tả | Fix |
|---|---------|-------|-----|
| W1 | §1.1 Purpose | Đã thêm Core Master Data paradigm nhưng Scope (§1.2) vẫn dùng "Inbound data ingestion" / "Outbound data queries" — nên đổi thành Push/Pull để nhất quán | §1.2: "Inbound data ingestion" → "Push: Business systems push data into ERP" |
| W2 | §3 FR-AUTH-001 | Keycloak endpoint: `https://quanna.tail072b2f.ts.net:8443/realms/HNHTravel-SGN` — đây là Tailscale URL, không phải public URL. Production sẽ cần domain khác. | Thêm ghi chú: "Staging URL; production sẽ dùng domain riêng" |
| W3 | §3 FR-AUTH-004 | Tenant Resolution: lookup `tenant_registry` từ BranchId. Nhưng PLAT-001 nói "Client KHÔNG BAO GIỜ được tin để chỉ định branch_id" — FR-AUTH-004 cần nhấn mạnh rule này rõ hơn. | Thêm bold: "**CRITICAL: branch_id chỉ lấy từ JWT claim BranchId, KHÔNG bao giờ từ request parameter**" |
| W4 | §8 FR-RLM | Rate limiting tiers: TIER_1=10/s, TIER_2=50/s, TIER_3=200/s — PLAT-001 §14 Open Items #5 nói "Rate limiting per-partner ở Gateway: Cấu hình tối thiểu, tinh chỉnh khi API Hub đi vào hoạt động". Cần pilot test. | Thêm note: "Tier limits cần pilot test với real traffic. Khuyến nghị bắt đầu TIER_1=5/s, tăng dần." |
| W5 | §14 Security | "Chỉ expose Kong port 8000/8443 ra ngoài" — nhưng chưa đề cập mTLS cho partner connections. PLAT-001 §14 #3 nói mTLS chưa bật. | Thêm: "Phase 1: TLS 1.3 + API key auth. Phase 2: mTLS cho partner connections" |
| W6 | §16.4 Kong config | Kong config YAML đang thiếu plugin `prometheus` cho metrics. | Thêm plugin `prometheus` vào Kong config |
| W7 | §4 FR-ING-002 | Batch ingestion: max 100 items. Nhưng chưa có rate limit cho batch endpoint riêng. | Thêm: "Batch endpoint rate limit: TIER_1=1 batch/min, TIER_2=5 batch/min, TIER_3=20 batch/min" |

### 🟢 NITS (4)

| # | Section | Mô tả |
|---|---------|-------|
| N1 | §1.4 References | Thiếu link đến PLAT-001 document |
| N2 | §10 API Specs | OpenAPI spec chưa có cho batch endpoint |
| N3 | §13 Vietnam Compliance | PDPD compliance section khá general — cần chi tiết hơn về consent management |
| N4 | Appendix | Document version vẫn là 1.0-Draft — cần update sau khi fix blockers |

### ✅ FRD Verdict: **CONDITIONALLY APPROVED — cần fix 3 blockers trước khi approve**

---

## Summary

| Document | Blockers | Warnings | Nits | Verdict |
|----------|----------|----------|------|---------|
| BRD | 0 | 5 | 3 | ✅ APPROVED with warnings |
| FRD | 3 | 7 | 4 | ⚠️ CONDITIONALLY APPROVED |

### Priority Actions

1. **Fix FRD B1**: Tách Kong/YARP thành 2 box riêng trong diagram
2. **Fix FRD B2**: Nhất quán event envelope field names (camelCase)
3. **Fix FRD B3**: Xác nhận routing key pattern
4. **Fix BRD W1-W5**: Core Master Data paradigm + dual-gateway + .NET 9
5. **Fix FRD W1-W7**: Push/Pull terminology, Keycloak URL note, branch_id rule, mTLS, Kong plugins

Sau khi fix blockers, FRD có thể approve.