# NexSync — Master Roadmap (Phase J onward)

Source: owner directive "NEXSYNC — PHASE J ONWARD" (2026-11-11).
Operational plan: `docs/implementation/PHASE_J_AUTONOMOUS_PLAN.md`.
Running record: `docs/implementation/AUTONOMOUS_EXECUTION_LOG.md`.
Protected baseline: tag `v2.0-phase2` = `a8fc6ba` — never rewritten.

| # | Phase / Sprint | Scope | Status |
|---|----------------|-------|--------|
| 1 | Phase J — repo & environment verification | git/JDK/SDK/Gradle inventory, execution log + this roadmap | IN PROGRESS |
| 2 | Backend boot repair (verified defect) | IDistributedCache + UseSession order + rate-limiter never-blocks; 124/124 | PENDING |
| 3 | Phase K / Sprint 2.1 — SAF onboarding | dynamic root URI, persisted grants, revoke detection | PENDING |
| 4 | Phase K / Sprint 2.1 — SAF handling + write redesign | fromSingleUri fix, Boolean checks, immutable versions + recoverable commit | PENDING |
| 5 | Phase K / Sprint 2.1 — tests + acceptance gate | SAF test matrix, SAF_STORAGE_VERIFICATION.md, gate evidence | PENDING |
| 6 | Phase L / Sprint 3B — Ktor server lifecycle | real embedded server, lifecycle tests (blocked by 5) | PENDING |
| 7 | Phase M / Sprint 3C — API routes + streaming | versioned API, streaming, OpenAPI, per-route tests | PENDING |
| 8 | Phase N / Sprint 3D — security middleware | auth, pairing checks, rate/size limits, Keystore, revocation | PENDING |
| 9 | Phase O / Sprint 3E — integration tests | server/storage/SAF/auth/transfer matrix, test-type taxonomy | PENDING |
| 10 | Phase P / Sprint 3F — server UI | start/stop, status, transfers, pairing UI, no fake online | PENDING |
| 11 | Phase Q / Sprint 4 — pairing + Backend-Mediated Remote Access | control plane, data plane, transfer tickets, QR pairing, ADR update | PENDING |
| 12 | Phase R / Sprint 5 — background service + discovery | foreground service, notifications, NSD/mDNS (optimization only) | PENDING |
| 13 | Phase S / Sprint 6 — desktop + web integration | node list/status/pair/revoke/browse/transfers in both clients | PENDING |
| 14 | Phase T / Sprint 7 — E2E + hardening | 19-step cross-component scenario, security/perf review | PENDING |
| 15 | Final completion report | FINAL_PROJECT_COMPLETION_REPORT.md + 3 independent verdicts | PENDING |

Rules carried from the directive:
- Sprint 3B (6) cannot start until Sprint 2.1 (3-5) passes its acceptance gate.
- No mock substituted for claimed E2E verification; no invented test results.
- Backend Relay never pushes large file content through control messages and
  never opens an unrestricted inbound port on the device.
- User-authored docs preserved: `ANALISIS_LENGKAP_NEXSYNC.md`,
  `SPRINT_2_1_FINAL_EVIDENCE.md`.
