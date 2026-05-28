# Changelog

## Sprint 5 - Production Hardening & Compliance

Released via branch `feat/s5-invoice-health` and cross-referenced in [PR #2](https://github.com/anhquankcn/hnh-erp-api-hub/pull/2).

### Added

- S5-001 Invoice Deletion Block for issued Sales Invoice records.
- S5-002 PDPA compliance foundations for consent, data export, and erasure workflows.
- S5-003 Audit Retention with archive processing and persisted hash-chain metadata.
- S5-004 Health Check Probes for liveness, readiness, startup, and aggregate health.
- S5-005 API Versioning with `/api/v1`, `/api/v2`, `/versions`, `Sunset`, and `Deprecation` headers.
- S5-006 Deployment Automation with Docker runtime support, compose dependency health checks, and CI/CD workflow support.
- S5-007 Monitoring & Alerting with Prometheus `/metrics` scraping and Grafana dashboard assets.

### Security And Reliability Fixes

- Fixed path traversal risk in archive/export handling.
- Changed invoice deletion protection to fail closed when invoice status cannot be verified.
- Made idempotency handling atomic to avoid duplicate ingestion under concurrent requests.
- Added two-phase archive handling to reduce partial archive/delete failure risk.
- Persisted audit hash-chain metadata for retained audit integrity.
- Hardened export error handling.
- Added v1 metadata handling so deprecated API responses consistently advertise lifecycle status.

### Review Process

- Gemini review identified production hardening gaps and security concerns.
- Codex fixes addressed the review findings in commit `1ad3c68`, covering path traversal, fail-closed guard behavior, atomic idempotency, two-phase archive handling, and v1 metadata.

### Sprint 5 Commits

- `f6ea8c5` docs(sprint-5): Production Hardening & Compliance planning (52 SP, 7 stories)
- `5e71172` feat(S5-001): Invoice Deletion Block - guard, exceptions, middleware, tests
- `75bfd14` feat(S5-004): Health Check Probes
- `ffed947` feat(S5-002,S5-003): PDPA Compliance + Audit Retention
- `a862937` S5-fix: Persist audit hash chain + export error handling
- `93d20e6` S5-005: API Versioning (v1/v2, Sunset headers, version discovery)
- `1919624` S5-006: Deployment Automation - Docker, CI/CD, health checks
- `0408208` S5-007: Monitoring & Alerting - Prometheus metrics, Grafana dashboard
- `1ad3c68` S5-fix: Codex review fixes - path traversal, fail-closed guard, atomic idempotency, two-phase archive, v1 metadata
