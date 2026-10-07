# NexSync Phase 2 Implementation Plan

**Date:** 2026-10-07
**Spec:** `docs/superpowers/specs/2026-10-07-nexsync-phase2-design.md`
**Baseline:** `v1.0-phase1`
**Execution:** SUBAGENT-DRIVEN (one task → review → freeze → next)
**Status:** Draft for review — do NOT implement before plan approval.

> Architecture is LOCKED. Plan must not redesign. Only rule on real contradictions
> against the spec, record the ruling, and continue.

## Global Constraints

- .NET 10, ASP.NET Core 10, EF Core 10, PostgreSQL 17, WPF .NET 10, SQLite via `Microsoft.Data.Sqlite`.
- Invariants from spec §0 are binding on every task.
- `Sequence` = canonical ordering; `OccurredAt` never decides conflicts.
- Server IDs = identity; paths = mutable location.
- Mutation + `ChangeLog` + `ProcessedOperations` = one DB transaction.
- Cursor advances only after full batch applied.
- `X-Device-Id` is identifier, not credential. JWT + ownership check required.
- No task may start long-running servers as child processes of the executor.
  Verify via `WebApplicationFactory` / unit tests, not `dotnet run` background jobs.

## Gates

### GATE 1 — SERVER SYNC GATE (after Task 07)

- [ ] `SyncCounters` row-lock allocation is commit-ordered under concurrency.
- [ ] Every File/Folder mutation writes `ChangeLog` + `ProcessedOperations` atomically.
- [ ] `OperationId` replay returns exact original result; fingerprint mismatch → 400.
- [ ] Pull honors `since+until+limit`, returns `nextCursor/hasMore/highWaterCursor`.
- [ ] Manifest + `snapshotCursor` represent one consistent snapshot.
- [ ] Device validation: 401 vs 403 semantics correct; revoked blocked.
- [ ] Tombstones persist after entity delete.

### GATE 2 — DESKTOP STATE GATE (after Task 13)

- [ ] Single-instance mutex enforced.
- [ ] Tray app boots to login → folder picker → status screen.
- [ ] DPAPI token store round-trips; device registered once.
- [ ] SQLite schema matches spec §4.4; canonical `/` paths enforced.
- [ ] Watcher debounce + stability check + hash gate enqueue correct ops.
- [ ] Queue is dependency-ordered (parent before child).
- [ ] `.nexsync/` and `*.nexsync-temp` never enter the queue.

### GATE 3 — SYNC CORRECTNESS GATE (after Task 18)

Must pass as scripted scenarios, not just unit asserts:

- [ ] Offline edits → reconnect → push, no duplicates on retry.
- [ ] Timeout-after-commit → retry replays original, single `ChangeLog`.
- [ ] Duplicate `OperationId` same fingerprint → replay; different fingerprint → 400.
- [ ] Watcher overflow → manifest reconcile, zero silent divergence.
- [ ] Remote delete with unpushed local edits → Recovery copy, then tombstone applies.
- [ ] Concurrent same-file modification → higher `Sequence` wins on both sides.
- [ ] Crash before cursor persist → re-pull repeats cleanly.
- [ ] Crash mid-atomic-replace → temp discarded, no corrupt file.
- [ ] 409 name conflict → conflict copy preserved, op unresolved (never silent drop).

---

## Task 01 — Sync schema + migrations

**Goal:** Add `SyncCounters`, `ChangeLogs`, `ProcessedOperations`, `Devices` tables.

**Files:**
- Modify: `backend/src/NexSync.Domain/Entities/*` (new: `SyncCounter.cs`, `ChangeLog.cs`, `ProcessedOperation.cs`, `Device.cs`; enums `SyncEntityType`, `SyncOperation`, `DevicePlatform`)
- Modify: `backend/src/NexSync.Infrastructure/Data/AppDbContext.cs`
- Create: EF migration `AddPhase2SyncSchema`

**Interfaces:**
- Produces: `DbSet<SyncCounter/ChangeLog/ProcessedOperation/Device>`; EF configs consumed by Tasks 02–07.

**Database changes:**
- `SyncCounters(UserId PK/FK, NextSequence BIGINT DEFAULT 1)`
- `ChangeLogs(Id UUID PK, UserId, Sequence BIGINT, EntityType/Operation VARCHAR(20), EntityId, Name NULL, ParentFolderId NULL, Hash/Size/ContentType NULL, OriginDeviceId NULL, OccurredAt TIMESTAMPTZ; UNIQUE(UserId,Sequence))`
- `ProcessedOperations(OperationId UUID PK, UserId, DeviceId, RequestFingerprint VARCHAR(64), ResultStatus INT, ResultPayload JSONB NULL, EntityId NULL, Sequence NULL, CreatedAt TIMESTAMPTZ)`
- `Devices(Id UUID PK, UserId FK, Name VARCHAR(100), Platform VARCHAR(20), CreatedAt/LastSeenAt TIMESTAMPTZ, RevokedAt NULL; INDEX(UserId))`

**Tests:** Migration applies up/down on throwaway PG; unique-constraint violation on `(UserId,Sequence)` duplicate.

**Dependencies:** `v1.0-phase1` baseline.
**Acceptance:** `dotnet ef database update` clean; snapshot contains all four tables; no Phase 1 table altered.

## Task 02 — Devices + registration API

**Goal:** `POST/GET/DELETE /api/devices` with idempotent registration.

**Files:**
- Create: `backend/src/NexSync.Application/DTOs/DeviceDtos.cs`
- Create: `backend/src/NexSync.Application/Interfaces/IDeviceService.cs`
- Create: `backend/src/NexSync.Application/Services/DeviceService.cs`
- Create: `backend/src/NexSync.API/Controllers/DevicesController.cs`

**Interfaces:**
- Consumes: `Device` entity, `IRepository<Device>` (add if missing).
- Produces: `IDeviceService.RegisterAsync(userId,name,platform,operationId) → DeviceDto` used by Task 08/10.

**API contract:**
- `POST /api/devices {name,platform}` + `X-Operation-Id` → `201 {deviceId,…}`; same `OperationId`+fingerprint → replay original.
- `GET /api/devices` → list incl. revoked with status.
- `DELETE /api/devices/{id}` → sets `RevokedAt` (idempotent).
- Validation: name trimmed, ≤100; platform ∈ enum; 400 otherwise.

**Tests:** Register → list shows ACTIVE; double registration same `OperationId` → one row; delete → REVOKED listed; other user's device invisible.
**Dependencies:** Task 01.
**Acceptance:** All device endpoints green; no `X-Device-Id` required on POST.

## Task 03 — ChangeLog sequence allocator

**Goal:** Commit-ordered per-user sequence allocation service.

**Files:**
- Create: `backend/src/NexSync.Application/Interfaces/ISyncSequenceAllocator.cs`
- Create: `backend/src/NexSync.Infrastructure/Data/SyncSequenceAllocator.cs` (or Application service + raw SQL via `DbContext`)

**Interfaces:**
- Produces: `AllocateAsync(userId, ct) → long` (must be called inside caller's transaction; executes `SELECT … FOR UPDATE`, bumps `NextSequence`).

**Database changes:** None beyond Task 01 (auto-creates `SyncCounters` row per user on first use).

**Tests:** 20 parallel allocations same user → strictly increasing, gap-free on success; rollback leaves gap, next commit still ordered.
**Dependencies:** Task 01.
**Acceptance:** Concurrency test passes; allocator never called outside a transaction (document call pattern).

## Task 04 — ChangeLog integration into mutations

**Goal:** Every File/Folder create/update/delete/move/rename writes `ChangeLog` atomically.

**Files:**
- Modify: `FileService.cs`, `FolderService.cs` (wrap in transaction: mutate → allocate → insert `ChangeLog`)
- Create: `SyncChangeWriter` helper (builds snapshot payload per operation per spec §1.5)

**Interfaces:**
- Consumes: `ISyncSequenceAllocator`, `X-Device-Id` → `OriginDeviceId`, `X-Operation-Id` (record only; replay logic is Task 05).

**Database changes:** None (writes to Task 01 tables).

**API contract:** No new endpoints; existing mutation responses unchanged + `ChangeLog` side effect.

**Tests:** Each mutation type produces exactly one `ChangeLog` with correct `Sequence`, snapshot fields, `ParentFolderId` semantics; delete leaves tombstone after entity row gone; failure mid-transaction leaves neither row nor event.
**Dependencies:** Tasks 01, 03.
**Acceptance:** GATE-1 partial: atomicity proven per operation type.

## Task 05 — ProcessedOperations + idempotency replay

**Goal:** Server-side `X-Operation-Id` idempotency with fingerprint + exact replay.

**Files:**
- Create: `IdempotencyFilter` (middleware or action filter) or integrate into `FileService/FolderService/DeviceService` entry points
- Create: `RequestFingerprint` helper (SHA-256 over canonical method+path+body fields)

**Interfaces:**
- Consumes: `ProcessedOperations` table; every mutation endpoint.
- Produces: replay path used by all future mutations.

**API contract:**
- First sight: process normally, store `{fingerprint, status, payload, entityId, sequence}` in same transaction.
- Replay same fingerprint → stored status+payload verbatim, no new `ChangeLog`.
- Same `OperationId` different fingerprint → `400 OPERATION_ID_REUSE`.

**Tests:** Upload → capture `OperationId` → simulate timeout retry → single `ChangeLog`, identical body; mismatched fingerprint → 400; device registration replay (Task 02) consistent with this mechanism.
**Dependencies:** Tasks 02, 04.
**Acceptance:** Timeout-retry test creates exactly one event and returns byte-identical payload.

## Task 06 — Sync pull API

**Goal:** `GET /api/sync/pull?since&until&limit`.

**Files:**
- Create: `SyncDtos.cs` (`PullResponse`, `ChangeItemDto`), `ISyncService`, `SyncService`, `SyncController`

**Interfaces:**
- Consumes: `ChangeLogs`, device validation helper (shared with Task 02).
- Produces: pull contract consumed by desktop Task 15 and tests.

**API contract:**
- Params: `since ≥ 0`, `until` required, `until ≥ since`, `limit` default 500 clamp 1–500.
- `WHERE UserId=? AND Sequence>? AND Sequence<=? ORDER BY Sequence LIMIT n`.
- Response: `{changes[], nextCursor, hasMore, highWaterCursor}` per spec §2.1.
- Auth: JWT + `X-Device-Id`; revoked/missing/foreign → 403.

**Tests:** Seed 5 changes → paginate `limit=2` with `until` cap; changes committed after run start excluded; empty → `nextCursor=since`; device 403 cases.
**Dependencies:** Tasks 01–04.
**Acceptance:** `until`-bounded determinism test green (the "101/102/103 during pull" scenario stays out).

## Task 07 — Manifest / reconcile API

**Goal:** `GET /api/sync/manifest` with consistent `snapshotCursor`.

**Files:**
- Modify: `SyncService`, `SyncController`; DTOs `ManifestResponse {folders[], files[], snapshotCursor}`.

**Interfaces:** Consumes same query layer as Task 06.

**API contract:** Single REPEATABLE READ (or equivalent) transaction: read folders+files+hashes and max committed `Sequence` as one snapshot; returns both together.

**Tests:** Concurrent writer during manifest read → `snapshotCursor` + subsequent `pull?since=snapshotCursor` yields the concurrent change exactly once; manifest entries carry hashes.
**Dependencies:** Tasks 01, 06.
**Acceptance:** GATE 1 ready → run full Server Sync Gate checklist before Task 08.

## Task 08 — React Web as a Device

**Goal:** Web registers a `Web` device and sends `X-Device-Id` + `X-Operation-Id`.

**Files:**
- Modify: `web/src/*` (auth flow, API client, device-id persistence in localStorage).

**Interfaces:** Consumes Task 02 endpoints; produces web-originated `ChangeLogs` with real `OriginDeviceId`.

**API contract:** No server change; client change only.

**Tests:** Login → device row created once (re-login reuses stored ID); file upload from web appears in pull with web `OriginDeviceId` (assert via API test or manual + integration check).
**Dependencies:** Task 02 (+ GATE 1 recommended).
**Acceptance:** Web mutations attributable; no NULL-origin events from web path.

## Task 09 — WPF foundation + single-instance + tray

**Goal:** `desktop/NexSync.Desktop` boots: login → folder picker → status/tray.

**Files:**
- Create: `desktop/NexSync.Desktop/` (WPF .NET 10, `NotifyIcon`, views: Login, Setup, Status; `Sync Now` button; log view).

**Interfaces:** Produces app shell + navigation consumed by Tasks 10–18. No sync logic yet.

**Tests:** Single-instance mutex test (second instance exits/signals first); tray shows/hides; screens navigate with fake auth service.
**Dependencies:** None (parallelizable after GATE 1).
**Acceptance:** App runs, one instance only, no sync attempted yet.

## Task 10 — DPAPI auth + desktop device registration

**Goal:** Login, secure token store, one-time device registration.

**Files:**
- Create: `AuthService` (JWT memory-only), `SecureTokenStore` (DPAPI `CurrentUser`, temp+atomic-replace writes), `DeviceRegistrationService`.

**Interfaces:** Consumes Task 02 API; produces authenticated `HttpClient` + persisted `DeviceId` for Tasks 14–15.

**Tests:** Token store round-trip; crash-simulated temp file leaves old token intact; registration retry same `OperationId` → single server row; 401 refresh-once path unit-tested with stub handler.
**Dependencies:** Tasks 02, 09.
**Acceptance:** Restart preserves login; duplicate registration impossible.

## Task 11 — SQLite local state

**Goal:** `LocalFolders`, `LocalFiles`, `PendingOperations`, `ProcessedOperations`, `SyncState` per spec §4.4.

**Files:**
- Create: `desktop/NexSync.Desktop/Data/*` (`LocalDb`, repositories, path canonicalizer).

**Interfaces:** Produces local-state API consumed by Tasks 12–16.

**Tests:** Canonical path tests (`\` vs `/`, `.` segments, case); CRUD per table; parent/child mapping round-trip.
**Dependencies:** Task 09.
**Acceptance:** No in-memory-only sync state; paths canonical.

## Task 12 — Watcher + debounce + stability

**Goal:** `FileSystemWatcher` → stable, hashed, queued events; `Error` → recovery flag.

**Files:**
- Create: `FolderWatcherService` (debounce 500ms–1s/path, stability check size/mtime+readable with backoff, SHA-256 gate, `Renamed` handling, `.nexsync/` + `*.nexsync-temp` ignores).

**Interfaces:** Produces `PendingOperations` rows for Task 13; raises `RecoveryRequired` consumed by Task 17.

**Tests:** Burst writes coalesce to one op; partial-write file not hashed until stable; identical content → no op; internal files ignored; `Error` injection sets recovery flag.
**Dependencies:** Task 11.
**Acceptance:** 20k-file copy simulation loses nothing silently (either queued or recovery flagged).

## Task 13 — Pending queue + dependency ordering

**Goal:** Ordered, dependency-aware pending queue.

**Files:**
- Create: `PendingQueueService` (enqueue with fresh `OperationId`; fetch bounded batch ordered `(CreatedAt, OperationId)`; parent-before-child guarantee).

**Interfaces:** Consumes Task 11/12 output; produces batch API for Task 14.

**Tests:** Create folder → subfolder → file always dequeues in dependency order even when events arrive scrambled; `OpType` set covers all 8 values.
**Dependencies:** Tasks 11, 12.
**Acceptance:** GATE 2 ready → run Desktop State Gate before Task 14.

## Task 14 — SyncCoordinator + push pipeline

**Goal:** Single-run coordinator; push bounded batch with per-status handling.

**Files:**
- Create: `SyncCoordinator` (signal coalescing, one-run-at-a-time), `PushPipeline`.

**Interfaces:** Consumes Tasks 10/13; drives Task 15 pull after push.

**API contract:** Sends `X-Device-Id` + per-op `X-Operation-Id` on existing mutation endpoints.

**Tests (stub-server level):** Coalesced triggers → one run; 409 → conflict-copy path (Task 18 hook); 401 → refresh+single retry; 403 → run stops + surfaced; 429/5xx/timeout → backoff, op retained.
**Dependencies:** Tasks 10, 13 (+ GATE 2).
**Acceptance:** `Sync Now ×3` rapid → exactly one run, second signal coalesced.

## Task 15 — Pull/apply + atomic writes

**Goal:** `since+until` pull loop; apply by server ID with atomic replace.

**Files:**
- Create: `PullPipeline` (high-water capture, `hasMore` loop inside window, per-change apply, cursor persist last).

**Interfaces:** Consumes Task 06 API + Task 11 state + Task 16 suppression.

**Tests:** Multi-batch pull applies in order; cursor file written only after full batch; simulated crash pre-persist → re-pull clean; mid-file crash → temp discarded, original intact; rename updates existing row by server ID (no duplicate).
**Dependencies:** Tasks 06, 11, 14.
**Acceptance:** Pull never advances cursor on partial apply.

## Task 16 — Loop prevention + suppression

**Goal:** Operation-scoped suppression `{path, operationId, expectedHash, expiry-fallback}` + hash gate + skip-self.

**Files:**
- Create: `SuppressionTracker`; wire into Task 15 writes and Task 12 watcher.

**Interfaces:** Cross-cutting between pull-apply and watcher.

**Tests:** Remote apply of 2 GB-simulated slow write → watcher events during write suppressed; same-path different-hash NOT suppressed; own-origin change with confirmed local state skipped; unconfirmed-timeout origin NOT skipped.
**Dependencies:** Tasks 12, 15.
**Acceptance:** Zero self-triggered re-uploads in scripted remote-apply soak.

## Task 17 — Failure recovery + watcher-overflow reconcile

**Goal:** `RECOVERY_REQUIRED` → manifest reconcile → resume.

**Files:**
- Create: `RecoveryService` (reinit watcher, `GET manifest`, diff by server ID + hash, rebuild local rows, clear stale pending where safe).

**Interfaces:** Consumes Task 07 API + Task 11 state.

**Tests:** Injected overflow mid-burst → reconcile converges local==server; deleted-local-db scenario → full rebuild + cursor set to `snapshotCursor`.
**Dependencies:** Tasks 07, 11, 12.
**Acceptance:** No silent divergence in any overflow test.

## Task 18 — Conflict + remote-delete recovery

**Goal:** 409 preservation + `DeletedByServer` recovery area.

**Files:**
- Create: `ConflictHandler` (409 → pull entity → conflict copy under `.nexsync/Recovery/`, mark op unresolved), remote-delete flow (move unpushed content → `Recovery/DeletedByServer/` → cancel ops → apply tombstone).

**Interfaces:** Called from Tasks 14/15.

**Tests:** 409 scenario keeps both copies; remote folder delete with unpushed edit → recovery copy byte-identical, ops cancelled, tombstone applied; local delete → trash/recycle where available.
**Dependencies:** Tasks 14, 15.
**Acceptance:** GATE 3 ready → run full Sync Correctness Gate scenarios.

## Task 19 — Integration + desktop tests

**Goal:** Close every spec success-criterion with automated tests.

**Files:**
- Server: extend `backend/tests` (`SyncPullTests`, `IdempotencyTests`, `DeviceAuthTests`, `ManifestTests`).
- Desktop: `desktop/NexSync.Desktop.Tests` (queue, suppression, canonicalizer, coordinator, recovery).

**Tests:** Map 1:1 to spec §7 items 1–9 + new criteria 10–11; use `WebApplicationFactory` + Testcontainers (server), stub `HttpMessageHandler` (desktop pipelines).
**Dependencies:** Tasks 01–18.
**Acceptance:** Full suite green; gate checklists re-run green.

## Task 20 — End-to-end acceptance

**Goal:** Manual/automated two-client proof: desktop ⇄ web via server.

**Files:** None (verification only) + `docs/PHASE2-ACCEPTANCE.md` recording results.

**Scenarios:** Register → setup empty folder → create/edit/rename/move/delete locally → visible on web; web edits → pulled to disk; offline burst → reconnect converges; timeout retry → single event; revoke device → 403; overflow → reconcile; non-empty setup → explicit choice enforced.
**Dependencies:** Task 19.
**Acceptance:** All scenarios recorded PASS with build/testimony refs.

## Task 21 — Docs + Phase 2 baseline/tag

**Goal:** Freeze Phase 2.

**Files:**
- Create/update: `docs/PHASE2-BASELINE.md`, dev-run docs, update root README.
- Tag: `v2.0-phase2`.

**Acceptance:** Clean tree, suite green, tag pushed-equivalent (local tag), next-phase notes recorded.
