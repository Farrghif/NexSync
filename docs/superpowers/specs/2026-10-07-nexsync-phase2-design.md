# NexSync Phase 2 — Synchronization Engine + Desktop Client

**Date:** 2026-10-07
**Status:** Approved for implementation planning
**Scope:** Server sync protocol + WPF tray desktop client
**Depends on:** Phase 1 (`v1.0-phase1`)

---

## 0. Invariants

These hold across every section. Nothing below may violate them.

- **Server `Sequence` is canonical ordering.** LWW = highest committed `Sequence`. `OccurredAt` is informational only, never used for conflict resolution. Device clocks are never trusted.
- **Server entity IDs are canonical identity.** `RelativePath` is mutable location only. Rename/move updates the existing local entity.
- **`ChangeLogs` is the source of synchronization truth**, not an audit log. Tombstones persist indefinitely in Phase 2.
- **`X-Operation-Id` gives mutation idempotency**, persisted server-side. Duplicate-name 409s are not an idempotency mechanism.
- **Local SQLite is durable client state.** In-memory-only sync state is forbidden.
- **`LastPulledSequence` advances only after full batch applied.**
- **Incremental pull is the normal path; manifest is repair/initial sync only.**

---

## 1. Server Sync Schema

### 1.1 `SyncCounters`

One row per user, the sequence source.

| Column | Type | Constraints |
|--------|------|-------------|
| UserId | UUID | PK, FK → Users |
| NextSequence | BIGINT | NOT NULL, DEFAULT 1 |

`NextSequence` = next **unused** sequence, never "last used".

### 1.2 `ChangeLogs`

| Column | Type | Constraints |
|--------|------|-------------|
| Id | UUID | PK (event identity) |
| UserId | UUID | FK → Users, NOT NULL |
| Sequence | BIGINT | NOT NULL |
| EntityType | VARCHAR(20) | NOT NULL, domain enum: `File`, `Folder` |
| EntityId | UUID | NOT NULL |
| Operation | VARCHAR(20) | NOT NULL, domain enum: `Created`, `Modified`, `Renamed`, `Moved`, `Deleted` |
| Name | VARCHAR(255) | NULL |
| FolderId | UUID | NULL |
| Hash | VARCHAR(64) | NULL |
| Size | BIGINT | NULL |
| ContentType | VARCHAR(100) | NULL |
| OriginDeviceId | UUID | NULL (NULL = legacy/server-originated, must be applied, never treated as self) |
| OccurredAt | TIMESTAMPTZ | NOT NULL |

Constraint: `UNIQUE (UserId, Sequence)` (also serves as the pull index — no redundant second index).

### 1.3 `ProcessedOperations` (server-side idempotency)

| Column | Type | Constraints |
|--------|------|-------------|
| OperationId | UUID | PK |
| UserId | UUID | NOT NULL |
| DeviceId | UUID | NOT NULL |
| ResultStatus | INT | NOT NULL (HTTP status of original result) |
| EntityId | UUID | NULL |
| Sequence | BIGINT | NULL (resulting ChangeLog sequence, if any) |
| CreatedAt | TIMESTAMPTZ | NOT NULL |

### 1.4 Sequence allocation (atomic, commit-ordered)

Mutation + allocation + ChangeLog insertion are **one DB transaction**:

```text
BEGIN
  mutate File/Folder row
  SELECT NextSequence FROM SyncCounters WHERE UserId=? FOR UPDATE
  seq = NextSequence; UPDATE SyncCounters SET NextSequence = seq+1
  INSERT ChangeLog(Sequence=seq, ...snapshot...)
  INSERT ProcessedOperations(OperationId, ..., Sequence=seq)
COMMIT
```

The row lock is held until commit, so allocation order = commit order. Rollback gaps are allowed and harmless. Two-phase commit (mutate-then-log) is forbidden: a committed mutation without an event would be invisible to other devices forever.

### 1.5 ChangeLog payload semantics (resulting state, not delta)

- **Created / Modified:** `Name, FolderId, Hash, Size, ContentType` = new state.
- **Renamed / Moved:** `Name, FolderId` = final state.
- **Deleted:** `Name, FolderId` retained as tombstone metadata; `Hash/Size/ContentType` NULL.

`EntityType`/`Operation` are domain enums validated in the application layer (stored as VARCHAR, never free-form).

### 1.6 Retention

Tombstones persist indefinitely in Phase 2. No cursor expiry (contract reserves `410 CURSOR_EXPIRED` → force reconcile, never triggered in Phase 2). Phase 1 data is not backfilled; first desktop sync uses the manifest.

---

## 2. Sync API Contract

### 2.1 Pull

`GET /api/sync/pull?since={seq}&limit={n}` (default 500, min 1, max 500, clamped server-side).

```json
{
  "changes": [
    {
      "sequence": 101,
      "entityType": "File",
      "entityId": "…",
      "operation": "Modified",
      "name": "home.dart",
      "folderId": "…",
      "hash": "…",
      "size": 1234,
      "contentType": "text/plain",
      "originDeviceId": "…",
      "occurredAt": "2026-10-07T…Z"
    }
  ],
  "nextCursor": 104,
  "hasMore": true
}
```

Rules:

- Query: `WHERE UserId=? AND Sequence>? ORDER BY Sequence ASC LIMIT n`.
- Empty result → `nextCursor = since`; else `nextCursor` = highest returned `Sequence`.
- Client persists the cursor **only after ALL received changes applied**. Partial apply must not advance the cursor; re-pull repeats safely.
- Loop while `hasMore=true` within a bounded run (see §5.4).

### 2.2 Push (existing Phase 1 endpoints, extended)

No parallel `/api/sync/push-*` endpoints. Desktop uses existing File/Folder mutation APIs with two added headers:

- `X-Device-Id` (required, validated per §3).
- `X-Operation-Id: UUID` (required for sync-engine mutations; enables idempotency).

Server flow per mutation:

```text
X-Operation-Id seen before? → YES: return original result, no new mutation
  → NO: mutate + ChangeLog + ProcessedOperations in ONE transaction
```

File upload keeps the Phase 1 streaming/temp-file/hash pipeline; storage finalize happens before the DB transaction, with orphan cleanup on DB failure.

### 2.3 Manifest (repair / first sync)

`GET /api/sync/manifest` returns full folders + files (+hashes) **plus `snapshotCursor`**: the highest committed ChangeLog sequence as of one consistent DB snapshot (MVCC read). Client applies manifest, sets cursor = `snapshotCursor`, then pulls `since=snapshotCursor` so concurrent changes are not lost.

---

## 3. Device Model

### 3.1 `Devices`

| Column | Type | Constraints |
|--------|------|-------------|
| Id | UUID | PK |
| UserId | UUID | FK → Users, NOT NULL |
| Name | VARCHAR(100) | NOT NULL, trimmed |
| Platform | VARCHAR(20) | NOT NULL, domain enum: `Windows`, `Android`, `Web` |
| CreatedAt | TIMESTAMPTZ | NOT NULL |
| LastSeenAt | TIMESTAMPTZ | NOT NULL |
| RevokedAt | TIMESTAMPTZ | NULL |

`RevokedAt == null` → ACTIVE; else REVOKED. Index on `(UserId)`. Names need not be unique.

### 3.2 Registration

`POST /api/devices { name, platform }` — JWT only, no `X-Device-Id`, with `X-Operation-Id` for retry idempotency. Returns `deviceId`. This is when a UUID becomes legitimate. Web clients register too (`"Chrome - Windows" / Web`) and persist the ID in the browser.

### 3.3 Request validation

Every mutation/sync request carries JWT + `X-Device-Id`:

```text
JWT invalid/missing → 401
JWT valid + device missing / wrong owner / revoked → 403
```

`X-Device-Id` is an **identifier, not a credential** — security comes from JWT + ownership validation. Revoked devices cannot sync, mutate, or update `LastSeenAt`; rows are kept for history (`GET /api/devices` lists them; `DELETE /api/devices/{id}` sets `RevokedAt`).

`LastSeenAt` updates on successful device activity; implementation may throttle (e.g. max once per 1–5 min) rather than writing on every request.

### 3.4 Self-origin skip

A client may skip a change whose `OriginDeviceId` equals its own **only when local state already reflects the successful server mutation** (i.e. it saw the 200 OK). After a timeout of unknown outcome, never skip on origin alone — verify state first. `OriginDeviceId == NULL` is legacy/server-originated: always apply.

---

## 4. Desktop Architecture (WPF Tray + Watcher + Sync Daemon)

### 4.1 App shape

- WPF (.NET 10) system-tray app (`NotifyIcon`). No file-browser UI in Phase 2.
- Screens: login, local folder picker, status (`Online / Offline / Syncing`), pending count, change log, `Sync Now`.
- Auth: JWT memory-only; refresh token in DPAPI `CurrentUser`-scope store; `DeviceId` in local config. Single-instance mutex (`Global\NexSync-{UserId}`).
- Layout: `C:\…\NexSync\` holds user data; `.nexsync\` holds SQLite, logs, temp, recovery. Watcher ignores `.nexsync\` and `*.nexsync-temp`.

### 4.2 Watcher pipeline

```text
FileSystemWatcher (files+dirs, IncludeSubdirectories=true)
  → per-path debounce 500ms–1s, coalesce bursts
  → file stability check (size/mtime stable, readable; backoff retry)
  → SHA-256 hash gate vs LocalFiles.Hash (same hash → discard)
  → enqueue PendingOperations with fresh OperationId (= X-Operation-Id)
Rename via Renamed events (old→new), not delete+create.
```

`FileSystemWatcher` is a **detector, not truth**: on `Error`/buffer overflow → mark `RECOVERY_REQUIRED`, reinitialize watcher, reconcile via `GET /api/sync/manifest`, rebuild local state, resume.

### 4.3 Loop prevention (three layers)

1. **Operation-scoped suppression**: `{ RelativePath, OperationId, ExpectedHash, ExpiresAt }`. Suppression releases when the remote apply finishes; expiry is safety fallback only. Match on path **and** expected hash — a different hash is never suppressed.
2. **Hash gate**: own writes hash-match recorded state → no queue entry.
3. **Skip-self**: origin == self and local state confirmed → skip.

### 4.4 Local SQLite

```text
LocalFolders(RelativePath PK, ServerFolderId NULL, ParentRelativePath NULL,
  LastAppliedSequence, UpdatedAt)
LocalFiles(RelativePath PK, ServerFileId NULL, ServerFolderId NULL,
  Hash, Size, LastAppliedSequence, UpdatedAt)
PendingOperations(OperationId UUID PK, OpType, RelPath, NewRelPath NULL,
  ServerId NULL, ParentServerId NULL, RetryCount, Status, CreatedAt)
ProcessedOperations(OperationId UUID PK, ResultSummary, CreatedAt)
SyncState(Key PK, Value)   // LastPulledSequence, DeviceId, local root
```

`OpType`: `CreateFile, ModifyFile, DeleteFile, MoveFile, CreateFolder, DeleteFolder, MoveFolder, RenameFolder` (rename spelled explicitly for debuggability).

Rules: canonical relative paths (`/` separator, no `.` segments); server IDs are identity, paths are location; queue ordered by `(CreatedAt, OperationId)` with parent-before-child dependency fulfillment.

### 4.5 Daemon loop

One `SyncCoordinator`: watcher/timer/Sync-Now only signal; concurrent runs coalesce, never overlap. Offline = queue only. Order per run: **push bounded pending batch → pull to high-water boundary**. `LastPulledSequence` persists only after the full batch applies.

---

## 5. Push/Pull Failure Handling

### 5.1 Push (per op, with `X-Device-Id` + `X-Operation-Id`)

- **2xx:** apply server result to local DB, mark op processed.
- **409 name conflict:** NOT an LWW event. Pull parent/current entity; preserve local data as conflict copy under `.nexsync/Recovery/` or mark op unresolved. Never silently drop local data.
- **True same-entity divergence** (both sides committed): highest server `Sequence` wins; loser overwritten on next pull. No fork in Phase 2.
- **401:** refresh JWT once (atomic DPAPI replace, §5.5), retry once.
- **403:** stop the run, surface revoked/no-access to the user.
- **404 parent:** reconcile that subtree via manifest.
- **429 / 5xx / timeout:** exponential backoff (2s → 30s max), op stays queued. Retry is safe via server-side `ProcessedOperations` replay.

### 5.2 Pull

`GET /api/sync/pull?since=cursor`, loop `hasMore` within the run's high-water boundary. Apply per change: download → temp file → verify hash → atomic replace (suppression active) → update local DB by server ID → after **all** succeed, persist `nextCursor`. Crash before persist → re-pull repeats. Crash mid-file → temp discarded.

### 5.3 Remote delete wins — safely

Tombstone applies, but unpushed local content underneath is first moved to `.nexsync/Recovery/DeletedByServer/`, obsolete ops cancelled, then deletion applied. Server wins; user data is not destroyed (local delete → recycle/trash when possible).

### 5.4 Bounded runs

Each run snapshots a bounded pending batch and pulls to a high-water sequence captured at run start. New local/remote activity goes to the next coalesced run — a run always terminates.

### 5.5 Crash-safe token rotation (desktop)

DPAPI refresh-token replacement must be atomic/recoverable (write temp + atomic replace) so a crash between server rotation and local persist cannot strand the client on a revoked token.

---

## 6. Out of Scope (later phases)

Offline editing UX beyond queueing, conflict-resolution UI, versioning history, chunked/resumable upload, deduplication engine, SignalR push (protocol already compatible: notify → pull `since` cursor), Android client, second-device sync hardening.

## 7. Success Criteria

1. Desktop registers as a device; revoked devices get 403.
2. Local file create/modify/rename/delete/moves sync to server via existing APIs; server Changes visible via pull.
3. Remote changes (incl. web-made) apply to disk with atomic writes, no sync loops.
4. Offline edits queue in SQLite and push on reconnect; timeout retry creates no duplicates (`OperationId` replay).
5. `409` name conflicts preserve local data, never silently drop.
6. Remote delete preserves unpushed local data in Recovery.
7. Watcher overflow triggers manifest reconcile, not silent divergence.
8. Crash at any point (mid-push, mid-pull, pre-cursor-persist, mid-token-rotation) recovers to correct state on restart.
9. Integration tests cover: cursor ordering, tombstones, idempotent retry, 403 revoked, 409 preservation, recovery path, bounded runs.
