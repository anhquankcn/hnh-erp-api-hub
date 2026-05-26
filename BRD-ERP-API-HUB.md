# Business Requirements Document (BRD)
# ERP API Hub — HNH Travel

**Document Version:** 1.1-Draft  
**Date:** 2026-05-26  
**Author:** HNH Technical Team  
**Status:** Draft — Pending Review  

---

## 1. Executive Summary

### 1.1 Overview
ERP API Hub là cổng kết nối trung tâm (integration gateway) cho phép các hệ thống nghiệp vụ giao tiếp hai chiều với **ERPNext — Core Master Data** của HNH Travel.

**Core Paradigm:** ERPNext đóng vai trò **Single Source of Truth**:
- **Push (Inbound):** Các hệ thống nghiệp vụ đẩy dữ liệu phát sinh vào ERP qua API Hub → ERP lưu trữ master data
- **Pull (Outbound):** Các hệ thống nghiệp vụ truy vấn ERP qua API Hub → lấy data mà hệ thống khác đã đẩy vào

API Hub đóng vai trò là lớp abstraction giữa ERP core và các hệ thống nghiệp vụ, đảm bảo tính bảo mật, nhất quán và khả năng mở rộng.

### 1.2 Business Problem
Hiện tại, ERP HNH Travel vận hành như một hệ sinh thái khép kín (closed ecosystem). Các nghiệp vụ sau đây đang gặp khó khăn:
- Khách hàng đặt tour qua website/e-commerce nhưng dữ liệu phải nhập tay vào ERP
- CRM bên ngoài (nếu có) không đồng bộ được với customer database trong ERP
- Đối tác cung cấp dịch vụ (khách sạn, hàng không, visa) cập nhật giá/availability qua email/file Excel
- Nhân sự tính lương trên phần mềm riêng, dữ liệu phải import thủ công
- Không có cơ chế đẩy báo cáo tự động ra hệ thống BI/báo cáo bên ngoài

### 1.3 Proposed Solution
Xây dựng **ERP API Hub** — một service layer chuyên biệt quản lý toàn bộ giao tiếp với external systems, bao gồm:
- **Chiều vào (Ingestion):** Tiếp nhận dữ liệu từ hệ thống bên ngoài, validate, transform, và đẩy vào ERP
- **Chiều ra (Query):** Cung cấp API để hệ thống bên ngoài truy vấn dữ liệu từ ERP
- **Event-driven integration:** Phát và tiêu thụ sự kiện qua message queue
- **Standardized security:** Xác thực/tác quyền qua Keycloak shared realm
- **Governance:** Rate limiting, audit logging, retry, circuit breaker

---

## 2. Business Objectives & SMART Goals

### 2.1 Objectives

| # | Objective | Priority |
|---|-----------|----------|
| 1 | Loại bỏ hoàn toàn việc nhập liệu thủ công từ các nguồn bên ngoài | High |
| 2 | Đảm bảo dữ liệu nhất quán real-time giữa ERP và external systems | High |
| 3 | Giảm thời gian xử lý đơn hàng/booking từ giờ xuống phút | High |
| 4 | Mở rộng khả năng kết nối với đối tác mà không cần sửa đổi ERP core | Medium |
| 5 | Cung cấp báo cáo/analytics cho hệ thống bên ngoài (BI, mobile apps) | Medium |

### 2.2 SMART Goals

| Goal | Metric | Target | Timeline |
|------|--------|--------|----------|
| G1 | Thời gian đồng bộ dữ liệu từ external systems | < 30 giây (95th percentile) | Phase 1 |
| G2 | Uptime API Hub | ≥ 99.9% | Phase 1 |
| G3 | Số lượng external systems tích hợp | ≥ 5 systems | Phase 2 |
| G4 | Thời gian onboarding system mới | < 2 tuần | Phase 2 |
| G5 | Rate limiting handling | 0% downtime do traffic spike | Phase 1 |
| G6 | Audit trail completeness | 100% API calls logged | Phase 1 |

---

## 3. Stakeholders

### 3.1 Internal Stakeholders

| Role | Name / Department | Responsibility | Interest |
|------|-------------------|----------------|----------|
| Project Sponsor | BOD / COO | Phê duyệt ngân sách, scope, timeline | Business ROI |
| Product Owner | IT Manager | Định nghĩa requirements, ưu tiên backlog | Feature completeness |
| Technical Lead | Development Team | Architecture, code review, technical decisions | System quality |
| QA Lead | QA Team | Test planning, acceptance criteria | Quality & reliability |
| DevOps | Infrastructure Team | Deploy, monitoring, infrastructure | Operational stability |
| Security Officer | Compliance | Security review, audit, data protection | Security & compliance |
| End Users | Back-stage (Phòng Vé), Sales, Accounting | Sử dụng ERP sau khi tích hợp | Usability & efficiency |

### 3.2 External Stakeholders

| Role | Organization | Integration Type | Contact |
|------|-------------|------------------|---------|
| System Integrator | TBD | Implementation partner | TBD |
| CRM Provider | TBD (Salesforce/HubSpot/Zoho) | Customer data sync | TBD |
| HR/Payroll Provider | TBD (HRM software) | Employee/payroll sync | TBD |
| E-commerce Platform | TBD (Website/Webapp) | Order/booking ingestion | TBD |
| Booking Engine | TBD (OTA/Reseller) | Availability, pricing | TBD |
| Supplier APIs | Hotels, Airlines, Visa agents | Product/tour data | TBD |
| BI/Analytics | TBD (PowerBI/Tableau/Looker) | Reporting queries | TBD |

---

## 4. Scope

### 4.1 In Scope

#### Phase 1 — Foundation (Month 1-2)
- **Core API Gateway Integration**
  - Kong 3.x DB-less (public, port 8000): JWT auth, rate limiting, API key, request transformation cho external partners
  - YARP .NET 9 (internal, port 8888): JWT auth, branch_id injection, routing cho 1StopShop services (unchanged)
  - Keycloak shared realm `HNHTravel-SGN`
  - Rate limiting per consumer/API key
  - Correlation ID propagation
  
- **Ingestion Framework**
  - REST API endpoints cho chiều vào (Write)
  - JSON schema validation
  - Transform/mapping engine (external format → ERP format)
  - Async processing queue (RabbitMQ)
  - Retry mechanism với exponential backoff
  - Dead Letter Queue (DLQ) cho failed messages
  
- **Query Framework**
  - REST API endpoints cho chiều ra (Read)
  - Pagination, filtering, sorting
  - Field selection (partial response)
  - Response caching (Redis)
  
- **Audit & Monitoring**
  - Audit log: mọi API call (who, what, when, result)
  - API usage metrics
  - Error tracking & alerting
  
#### Phase 2 — System Integrations (Month 3-4)
- **External CRM Integration**
  - Customer/Contact sync (two-way)
  - Lead conversion tracking
  
- **E-commerce/Website Integration**
  - Order ingestion
  - Payment confirmation webhook
  - Booking status update
  
- **Partner API Integration**
  - Tour data ingestion (availability, pricing)
  - Product catalog sync
  
#### Phase 3 — Advanced Features (Month 5-6)
- **HR/Payroll Integration**
  - Employee master data sync
  - Payroll result ingestion
  - Leave balance sync
  
- **Advanced Reporting**
  - Analytics API cho BI tools
  - Scheduled report export
  - Real-time dashboard data feeds
  
- **Event-driven Architecture**
  - Webhook subscriptions cho external systems
  - Event streaming (change data capture)

### 4.2 Out of Scope

| Item | Reason | Future Consideration |
|------|--------|-------------------|
| **FIN Module** | Đã được viết riêng, hoạt động độc lập | Không liên quan trực tiếp đến ERP API Hub |
| **ERP Core Modification** | API Hub phải tương thích với existing services | Nếu ERP core cần thay đổi, sẽ là project riêng |
| **Real-time Streaming (Phase 1)** | Quá phức tạp cho Phase 1 | Phase 3 với Kafka hoặc Redis Streams |
| **GraphQL API** | REST đủ cho Phase 1-2 | Phase 3 nếu có nhu cầu complex queries |
| **On-premise Deployment** | Tập trung cloud/stage trước | Nếu có yêu cầu hybrid cloud |
| **EDI/X12/B2B Protocols** | Ngành du lịch chủ yếu dùng REST/JSON | Nếu có đối tác yêu cầu EDI |
| **Mobile SDK** | API Hub cung cấp HTTP APIs thô | Mobile team tự build SDK nếu cần |

---

## 5. Business Use Cases

### UC-01: External CRM Customer Sync
**Actor:** External CRM system (Salesforce/HubSpot/Zoho)  
**Trigger:** Customer created/updated in CRM  
**Flow:**
1. CRM gửi customer data qua API Hub ingestion endpoint
2. API Hub validate schema
3. API Hub transform data → ERP Customer format
4. API Hub gọi CrmService để cập nhật
5. API Hub trả về confirmation ID
6. API Hub ghi audit log

**Business Value:** Loại bỏ nhập customer 2 lần, đảm bảo data consistency

### UC-02: E-commerce Order Ingestion
**Actor:** E-commerce website / mobile app  
**Trigger:** Customer hoàn tất đặt tour online  
**Flow:**
1. Website gửi order data qua API Hub (Push vào ERP — Core Master Data)
2. API Hub validate (tour availability, pricing, customer info)
3. API Hub push order vào ERPNext (Sales Order / Quotation)
4. API Hub publish event `erphub.ingestion.order.created` vào 1stopshop_event_bus
5. TicketingService consume event → tạo booking internally
6. API Hub gửi notification qua NotificationService
7. Website nhận booking confirmation

**Business Value:** Tự động hóa quy trình đặt tour online → giảm thời gian xử lý từ giờ xuống phút

### UC-03: Partner Tour Data Sync
**Actor:** External tour supplier / DMC  
**Trigger:** Supplier cập nhật tour mới hoặc thay đổi giá  
**Flow:**
1. Supplier đẩy tour data qua API Hub (bulk upload hoặc single update)
2. API Hub validate pricing rules, date ranges
3. API Hub update tour catalog trong TicketingService
4. API Hub invalidate cache
5. API Hub notify subscribed systems (website, internal users)

**Business Value:** Tour data luôn up-to-date; giảm sai sót giá/ngày

### UC-04: HR/Payroll Data Ingestion
**Actor:** External HR/Payroll system  
**Trigger:** Payroll cycle completed  
**Flow:**
1. HR system gửi payroll results (net salary, deductions, tax)
2. API Hub validate và format data
3. API Hub đẩy vào CoreService (employee records)
4. API Hub log với mức độ nhạy cảm cao (PII masking)

**Business Value:** Payroll data tự động đồng bộ; giảm sai sót tính lương

### UC-05: Inventory/Supplier Sync
**Actor:** Hotel, airline, visa service providers  
**Trigger:** Room availability changes, flight schedule updates  
**Flow:**
1. Supplier API push availability data
2. API Hub validate và reconcile với existing inventory
3. API Hub update QuotationService (affects pricing)
4. API Hub trigger cache refresh

**Business Value:** Real-time availability; tránh overbooking

### UC-06: Financial Reporting Export
**Actor:** External BI system / Accounting software  
**Trigger:** Scheduled report hoặc ad-hoc query  
**Flow:**
1. BI system gọi query API với parameters (date range, branch, report type)
2. API Hub authenticate và authorize (branch-level permissions)
3. API Hub query ERP services (aggregated data)
4. API Hub format response (JSON/CSV)
5. API Hub cache result nếu là báo cáo thường xuyên

**Business Value:** Báo cáo tự động; hỗ trợ ra quyết định real-time

### UC-07: Real-time Data Queries
**Actor:** Mobile app / Dashboard / External portal  
**Trigger:** User request  
**Flow:**
1. Client gọi API với query parameters
2. API Hub check cache (Redis)
3. Nếu cache miss → query ERP services
4. API Hub trả về JSON response

**Business Value:** Nhanh, nhẹ; hỗ trợ mobile experiences

### UC-08: Webhook Subscriptions
**Actor:** External system muốn nhận event notifications  
**Trigger:** ERP data changes  
**Flow:**
1. External system đăng ký webhook qua API Hub
2. API Hub validate webhook URL
3. Khi có event (e.g., booking confirmed), API Hub gửi POST đến webhook
4. API Hub track delivery status; retry nếu failed

**Business Value:** Event-driven; giảm polling overhead

---

## 6. Constraints & Assumptions

### 6.1 Constraints

| # | Constraint | Impact |
|---|-----------|--------|
| C1 | Phải tương thích với existing ERP services (.NET 9, PostgreSQL) | Tech stack không tự do lựa chọn |
| C2 | Không được sửa đổi ERP core logic | API Hub phải work around existing APIs |
| C3 | Keycloak shared realm — không được tạo realm riêng | Auth config phải phối hợp với IT admin |
| C4 | Kong DB-less (public :8000) + YARP (internal :8888) — cần maintain 2 gateway configs | Thay đổi routing cần redeploy Kong; YARP config riêng |
| C5 | Module FIN hoạt động độc lập — không giao tiếp trực tiếp | Financial data flows qua FIN, không qua API Hub |
| C6 | Multi-branch support — data isolation required | Complexity tăng với tenant context |
| C7 | Vietnamese travel industry compliance | Tax, invoice rules phải đúng quy định VN |

### 6.2 Assumptions

| # | Assumption | Risk if Invalid |
|---|-----------|----------------|
| A1 | External systems có khả năng gọi REST APIs | Cần hỗ trợ file-based import fallback |
| A2 | ERP services có đủ API surface để tích hợp | Có thể cần bổ sung endpoints trong ERP |
| A3 | Network latency giữa API Hub và ERP < 50ms trong cùng region | Cần optimize hoặc deploy gần hơn |
| A4 | RabbitMQ có đủ capacity cho async workload | Cần scale hoặc thêm queue partitioning |
| A5 | External systems có thể xử lý webhook | Cần hỗ trợ polling mode alternative |

---

## 7. Risks & Mitigation

| # | Risk | Probability | Impact | Mitigation |
|---|------|------------|--------|-----------|
| R1 | External system thay đổi API/schema đột ngột | Medium | High | Versioning strategy; adapter pattern; monitoring |
| R2 | Data inconsistency giữa ERP và external systems | Medium | High | Transactional outbox; eventual consistency; reconciliation job |
| R3 | Security breach qua external connection | Low | Critical | OAuth 2.0/JWT; mTLS; IP whitelist; audit log |
| R4 | Performance degradation do traffic spike | Medium | High | Rate limiting; circuit breaker; auto-scaling |
| R5 | Data loss trong async processing | Low | High | Persistent queues; DLQ; retry; dead letter monitoring |
| R6 | External system downtime ảnh hưởng ERP | Medium | Medium | Async processing; circuit breaker; queue buffering |
| R7 | PII leak trong audit log | Low | Critical | Log masking; field-level encryption; access control |
| R8 | Complexity vượt quá dự kiến | Medium | Medium | Phased approach; MVP first; feature toggles |
| R9 | Integration test khó khăn với external systems | High | Medium | Contract testing (Pact); mocks/stubs; sandbox environment |
| R10 | Multi-tenant data leak | Low | Critical | Row-level security; tenant context validation; tests |

---

## 8. Success Criteria

### 8.1 Acceptance Criteria

| ID | Criteria | Measurement |
|----|----------|-------------|
| AC1 | API Hub xử lý > 1000 requests/minute | Load testing |
| AC2 | P95 latency < 500ms cho cached queries | Performance testing |
| AC3 | P95 latency < 2s cho ingestion (async) | Performance testing |
| AC4 | 100% API calls có audit log | Audit log validation |
| AC5 | Zero data loss trong 30 ngày continuous operation | Monitoring |
| AC6 | 5 external systems onboarded thành công | Integration testing |
| AC7 | Uptime ≥ 99.9% | Monitoring (SLA) |
| AC8 | Security scan pass (OWASP Top 10) | Penetration testing |
| AC9 | Recovery time < 15 phút sau failure | Disaster recovery drill |
| AC10 | Developer onboarding time < 2 tuần cho system mới | Time tracking |

### 8.2 KPIs

| KPI | Baseline | Target | Measurement |
|-----|----------|--------|-------------|
| API Availability | N/A | 99.9% | Uptime monitoring |
| Avg Response Time | N/A | < 200ms | APM metrics |
| Error Rate | N/A | < 0.1% | Error tracking |
| Data Sync Latency | Manual (hours) | < 30s | Event timestamps |
| Integration Time | N/A (no API Hub) | < 2 weeks | Project tracking |
| Customer Data Quality | Manual errors | 99.5% accurate | Data validation reports |

---

## 9. Timeline & Phases

### 9.1 High-level Timeline

```
Month 1-2: Phase 1 — Foundation
Month 3-4: Phase 2 — Core Integrations  
Month 5-6: Phase 3 — Advanced Features
Ongoing:   Maintenance & Expansion
```

### 9.2 Phase Details

#### Phase 1 — Foundation (8 weeks)

| Week | Deliverable | Owner |
|------|-------------|-------|
| 1 | Architecture review, Kong config update, Keycloak setup | Tech Lead |
| 2 | API Hub service scaffolding, database schema | Backend Dev |
| 3 | Ingestion framework, validation engine | Backend Dev |
| 4 | Query framework, caching layer | Backend Dev |
| 5 | Retry/DLQ mechanism, audit logging | Backend Dev |
| 6 | Rate limiting, circuit breaker | Backend Dev |
| 7 | Monitoring, alerting, dashboards | DevOps |
| 8 | Testing, documentation, Phase 1 review | QA + Tech Lead |

#### Phase 2 — Core Integrations (8 weeks)

| Week | Deliverable | Owner |
|------|-------------|-------|
| 9 | CRM integration design + implementation | Backend Dev |
| 10 | E-commerce order ingestion | Backend Dev |
| 11 | Partner API connector framework | Backend Dev |
| 12 | Inventory/Supplier sync | Backend Dev |
| 13 | Integration testing với real systems | QA |
| 14 | Performance tuning | Backend Dev |
| 15 | Security hardening | Security Officer |
| 16 | Staging deployment, UAT | DevOps + QA |

#### Phase 3 — Advanced Features (8 weeks)

| Week | Deliverable | Owner |
|------|-------------|-------|
| 17 | HR/Payroll integration | Backend Dev |
| 18 | BI reporting API | Backend Dev |
| 19 | Webhook subscription system | Backend Dev |
| 20 | Event streaming (CDC) | Backend Dev |
| 21 | Advanced analytics endpoints | Backend Dev |
| 22 | Load testing, optimization | QA + Backend |
| 23 | Documentation, training | Tech Lead |
| 24 | Production deployment, go-live | DevOps + All |

### 9.3 Milestones

| Milestone | Date | Criteria |
|-----------|------|----------|
| M1 — Foundation Complete | End Month 2 | API Hub core operational, tests pass |
| M2 — First Integration Live | End Month 3 | 1 external system connected |
| M3 — Core Integrations Done | End Month 4 | ≥ 3 systems connected |
| M4 — Advanced Features Ready | End Month 6 | All Phase 3 features deployed |
| M5 — Production Go-live | Month 6+ | UAT passed, rollback plan ready |

---

## 10. Dependencies

### 10.1 Internal Dependencies

| # | Dependency | Impact | Owner | Status |
|---|-----------|--------|-------|--------|
| D1 | Kong configuration update | API routing, JWT | DevOps | Required |
| D2 | Keycloak realm configuration | Authentication | Security Officer | Required |
| D3 | ERP service API stability | Integration points | ERP Team | Assumed |
| D4 | PostgreSQL schema (if changes needed) | Data persistence | ERP Team | TBD |
| D5 | RabbitMQ capacity | Async processing | DevOps | Required |
| D6 | Redis cluster | Caching, rate limiting | DevOps | Required |
| D7 | Monitoring stack (Grafana, Prometheus) | Observability | DevOps | Required |

### 10.2 External Dependencies

| # | Dependency | Impact | Owner | Status |
|---|-----------|--------|-------|--------|
| D8 | External system API documentation | Integration design | External | TBD |
| D9 | External system sandbox access | Testing | External | TBD |
| D10 | SSL certificates (production) | Security | DevOps | TBD |
| D11 | Domain/DNS configuration | Public access | DevOps | TBD |
| D12 | Legal/compliance review (data sharing) | Compliance | Legal | TBD |

### 10.3 Dependency Graph

```
Keycloak Config ──┐
                  ├──→ API Hub Service ──→ External System Integrations
Kong Config ──────┤         │
                  │         ├──→ ERP Services
PostgreSQL ───────┤         │
                  │         ├──→ RabbitMQ
RabbitMQ ─────────┤         │
                  │         └──→ Redis
Redis ────────────┘
```

---

## Appendix A: Glossary

| Term | Definition |
|------|-----------|
| API Hub | Central integration layer managing all external system communications |
| BFF | Backend-for-Frontend pattern |
| CDC | Change Data Capture |
| DLQ | Dead Letter Queue — lưu messages failed sau max retry |
| DMC | Destination Management Company — đối tác cung cấp tour địa phương |
| ERP | Enterprise Resource Planning — hệ thống quản trị doanh nghiệp |
| JWT | JSON Web Token — authentication token format |
| Kong | API Gateway open source |
| MFE | Micro Frontend — kiến trúc frontend module hóa |
| OTA | Online Travel Agency — đại lý du lịch trực tuyến |
| PII | Personally Identifiable Information — thông tin cá nhân |
| REST | Representational State Transfer — HTTP API architectural style |
| SLA | Service Level Agreement — cam kết dịch vụ |
| Webhook | HTTP callback — server gửi data đến client khi có event |

## Appendix B: Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0-Draft | 2026-05-26 | HNH Tech Team | Initial draft |

## Appendix C: Approval

| Role | Name | Signature | Date |
|------|------|-----------|------|
| Product Owner | | | |
| Technical Lead | | | |
| Security Officer | | | |
| BOD Sponsor | | | |
