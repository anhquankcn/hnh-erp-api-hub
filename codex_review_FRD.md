# Codex Review — FRD-ERP-API-HUB-v1.0
**Reviewer:** Codex (Independent Reviewer #2)  
**Date:** 2026-05-26  
**Conclusion:** NEEDS_REWORK (3 blockers, 6 warnings)

## Summary
FRD trình bày kiến trúc gateway hợp lý và đầy đủ các module chức năng chính. Tuy nhiên còn 3 blocker liên quan đến ERPNext event sourcing, tenant validation, và Vietnam compliance cần xử lý trước khi bắt tay implement. Review dưới đây dựa trên đối chiếu với BRD, ERP_API_HUB_CONTEXT.md, và ERPNext v15 REST API docs.

---

## Blockers (must fix)

| # | Severity | Section | Issue | Suggested Fix |
|---|----------|---------|-------|---------------|
| 1 | **blocker** | §6 Webhook Module / §11.3 | FRD mô tả webhook flow "ERPNext → API Hub → external" nhưng ERPNext v15 KHÔNG có native event streaming để push event trực tiếp. Frappe có Document Hooks (server-side Python) nhưng không có webhook dispatcher tích hợp sẵn như các SaaS. Để trigger webhook, cần custom Frappe Server Script hoặc custom app đẩy event vào RabbitMQ. Điều này vi phạm constraint "Không được sửa đổi ERP core logic" (BRD §6.1 C2). | Thay đổi approach: (a) API Hub poll ERPNext API định kỳ để phát hiện thay đổi, hoặc (b) dùng Frappe Server Script minimal (không sửa core, chỉ thêm custom script trong UI), hoặc (c) dùng CDC (Change Data Capture) từ MariaDB binlog. Cập nhật FRD §6.1, §6.2 để phản ánh thực tế ERPNext. |
| 2 | **blocker** | §3.4 / §10.4 | Multi-tenant tenant context chỉ mô tả "X-Frappe-Site-Name" header nhưng KHÔNG giải thích cách API Hub discover và validate tenant mapping. ERPNext sites là directories trên filesystem, không phải dynamic routing. Nếu API Hub chạy ở container/service riêng, nó cần biết mapping `tenant_id` → `site_name` → `internal_hostname:port` để route request. | Thêm requirement FR-AUTH-004b: "Tenant Resolution Service" — map `tenant_id` sang `site_name` và `erpnext_endpoint` (ví dụ: `frontend` → `frontend.local:8000`). Lưu mapping trong PostgreSQL config. Thêm health check per tenant. |
| 3 | **blocker** | §13 Security / §10.4 | Thiếu hoàn toàn yêu cầu compliance Việt Nam: VAT/GTGT, hóa đơn điện tử (e-invoice), mã số thuế, và quy định về xuất hóa đơn theo Thông tư 78/2021/TT-BTC. BRD §6.1 C7 nêu rõ "Tax, invoice rules phải đúng quy định VN" nhưng FRD không có requirement nào về điều này. | Thêm section "Vietnam Compliance Requirements" với các FR: (a) Validate tax_id format (10/13 số), (b) Invoice sequence phải tuân thủ quy định, (c) Hỗ trợ trường hợp xuất hóa đơn GTGT theo Thông tư 78, (d) PII handling tuân thủ Nghị định 13/2023/NĐ-CP (PDPA). |

## Warnings (should fix)

| # | Severity | Section | Issue | Suggested Fix |
|---|----------|---------|-------|---------------|
| 4 | **warn** | §14.1 SLA | Sync ingestion SLA P99 < 2s có thể không thực tế nếu ERPNext đang xử lý transaction phức tạp (ví dụ: Sales Order với nhiều line items trigger multiple background jobs). ERPNext v15 không phải real-time system. | Điều chỉnh: Sync ingestion P99 < 5s, hoặc khuyến khích async ingestion cho operations phức tạp. Thêm note về ERPNext background job queue có thể delay response. |
| 5 | **warn** | §4.2 FR-ING-003 / §9.2 FR-CFG-003 | Data transformation mô tả generic mapping types (Direct, Calculated, Lookup, Constant, Conditional) nhưng không đề cập cách xử lý ERPNext-specific quirks: (a) mandatory fields (`naming_series`, `doctype`), (b) child table handling (array of objects), (c) Link field validation (phải tồn tại trong reference table), (d) custom fields. | Thêm transformation requirements cho ERPNext-specific cases: child table mapping, link field validation, custom field detection, naming_series auto-generation. Ví dụ: Sales Order items là child table `items`, không phải flat object. |
| 6 | **warn** | §4.2 FR-ING-004 | UPSERT operation (Check existence → CREATE or UPDATE) có thể gây race condition nếu 2 requests xử lý song song. ERPNext REST API không có atomic UPSERT. | Thêm requirement: "UPSERT phải sử dụng distributed lock (Redis RedLock) hoặc idempotency key + retry để tránh race condition." |
| 7 | **warn** | §4.2 FR-ING-005 / §11.1 | Async ingestion flow mô tả "Queue (RabbitMQ) → ERPNext API" nhưng không giải thích ai là consumer. Nếu API Hub là consumer, thì nó phải maintain long-running workers — điều này ảnh hưởng đến architecture (stateful vs stateless). | Làm rõ: API Hub service chạy background workers (separate deployment hoặc sidecar) để consume RabbitMQ messages và gọi ERPNext. Hoặc tách thành "API Hub API" (stateless) và "API Hub Worker" (stateful). |
| 8 | **warn** | §8.2 FR-RLM-005 | Kong integration mô tả "Sync tier configuration from API Hub to Kong via Admin API" nhưng Kong đang chạy DB-less mode (BRD §6.1 C4). DB-less Kong KHÔNG có Admin API để runtime update config — config phải qua declarative file. | Sửa: Nếu Kong DB-less, config sync phải qua CI/CD pipeline (update kong.yml → reload Kong), không phải runtime API. Hoặc chuyển sang Kong DB-backed mode nếu cần dynamic config. |
| 9 | **warn** | §10.3 Error Format | RFC 7807 Problem Details format định nghĩa `errors` array nhưng RFC 7807 standard KHÔNG có field `errors` — field này là extension non-standard. Nếu dùng RFC 7807 nghiêm ngặt, chỉ có `type`, `title`, `status`, `detail`, `instance`. | Làm rõ: "RFC 7807 with custom extensions" hoặc đổi tên format thành "HNH Problem Details Format" để tránh confusion. Nếu muốn RFC 7807 strict, đưa validation errors vào `detail` hoặc dùng `extensions` object. |

## Nits (nice to have)

| # | Severity | Section | Issue | Suggested Fix |
|---|----------|---------|-------|---------------|
| 10 | **nit** | §2.2 Component Interaction | Đề cập .NET Core 8 cho API Hub nhưng BRD đã xác nhận ERPNext là Python/Frappe, không phải .NET. Nếu API Hub chọn .NET Core 8, cần giải thích lý do (team expertise, performance, ecosystem). | Thêm 1-2 dòng giải thích: "API Hub chọn .NET Core 8 vì team có expertise, performance tốt cho gateway, và có thể tận dụng middleware ecosystem (rate limiting, auth, caching)." |
| 11 | **nit** | §10.4 Doctype Examples | Booking doctype example có `status: "Confirmed"` nhưng không liệt kê các valid values. ERPNext thường dùng Select field với predefined options. | Thêm note: "Status values phải match ERPNext Select field options: Draft, Confirmed, Cancelled, Completed." |
| 12 | **nit** | §A Appendix | Database schema examples dùng PostgreSQL syntax (`gen_random_uuid()`) nhưng ERPNext dùng MariaDB. Mặc dù API Hub có thể dùng PostgreSQL, nhưng cần consistency trong ví dụ. | Đổi `gen_random_uuid()` thành `UUID()` hoặc `UUID_GENERATE_V4()` tùy DB, hoặc dùng application-generated UUID. |
| 13 | **nit** | §10.2 Headers | `X-API-Version` header có thể gây confusion với URL versioning (`/api/v1/`). Nên chọn 1 approach. | Nếu dùng URL versioning, bỏ `X-API-Version` header. Nếu dùng header versioning, đổi URL thành `/api/` generic. |

## Positive Notes

- **Kiến trúc module hóa tốt:** 7 module rõ ràng, mỗi module có API endpoints riêng, dễ implement và test độc lập.
- **RFC 7807 Problem Details:** Dù có vấn đề nhỏ về extensions, việc chọn standard format là good practice.
- **PII Masking:** Thiết kế chi tiết và thực tế (masking patterns cụ thể cho email, phone, ID card).
- **Idempotency Keys:** Thiết kế đúng — Redis TTL 24h, cached response, không bắt buộc.
- **Rate Limiting Tiers:** Thiết kế 3 tiers với burst handling là production-ready.
- **Data Flow Diagrams:** ASCII diagrams rõ ràng, dễ hiểu, phù hợp cho technical discussion.

---

## Recommendations

### Priority 1 (Blockers)
1. **Quyết định approach webhook:** Polling, Server Script minimal, hay CDC? Đây là quyết định kiến trúc quan trọng ảnh hưởng đến cả hệ thống.
2. **Thiết kế Tenant Resolution Service:** Làm rõ cách API Hub discover ERPNext sites.
3. **Bổ sung Vietnam compliance requirements:** Không thể bỏ qua với doanh nghiệp VN.

### Priority 2 (Warnings)
4. Điều chỉnh SLA cho sync ingestion (hoặc khuyến khích async).
5. Bổ sung ERPNext-specific transformation requirements (child tables, Link fields).
6. Xử lý race condition cho UPSERT.
7. Làm rõ background worker architecture.
8. Chỉnh sửa Kong integration approach cho DB-less mode.
9. Chuẩn hóa error response format.

### Priority 3 (Nits)
10-13. Sửa các vấn đề nhỏ về syntax, consistency, và documentation clarity.

---

**Overall Assessment:** FRD có foundation tốt với kiến trúc gateway hợp lý. Sau khi xử lý 3 blockers (đặc biệt là webhook approach), tài liệu sẵn sàng cho implementation phase.

**Reviewer Signature:** Codex  
**Date:** 2026-05-26
