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
- **`X-Operation-Id` gives mutation idempotency**, persisted server-side with request fingerprint + original result payload. Duplicate-name 409s are not an idempotency mechanism.
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
| ParentFolderId | UUID | NULL |
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
| RequestFingerprint | VARCHAR(64) | NOT NULL (SHA-256 of canonical method+path+body relevant fields) |
| ResultStatus | INT | NOT NULL (HTTP status of original result) |
| ResultPayload | JSONB | NULL (original response body, for exact replay) |
| EntityId | UUID | NULL |
| Sequence | BIGINT | NULL (resulting ChangeLog sequence, if any) |
| CreatedAt | TIMESTAMPTZ | NOT NULL |

Replay rule: `OperationId` exists + `RequestFingerprint` matches → return stored `ResultStatus` + `ResultPayload` verbatim, no new mutation. Fingerprint mismatch → `400 OPERATION_ID_REUSE` (idempotency keys must not be recycled for different requests).

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

- **Created / Modified:** `Name, ParentFolderId, Hash, Size, ContentType` = new state.
- **Renamed / Moved:** `Name, ParentFolderId` = final state.
- **Deleted:** `Name, ParentFolderId` retained as tombstone metadata; `Hash/Size/ContentType` NULL.

For `EntityType=File`, `ParentFolderId` = containing folder. For `EntityType=Folder`, `ParentFolderId` = containing parent folder (NULL = root); the folder's own identity is always `EntityId`.

`EntityType`/`Operation` are domain enums validated in the application layer (stored as VARCHAR, never free-form).

### 1.6 Retention

Tombstones persist indefinitely in Phase 2. No cursor expiry (contract reserves `410 CURSOR_EXPIRED` → force reconcile, never triggered in Phase 2). Phase 1 data is not backfilled; first desktop sync uses the manifest.

---

## 2. Sync API Contract

### 2.1 Pull

`GET /api/sync/pull?since={seq}&until={highWater}&limit={n}` (default limit 500, min 1, max 500, clamped server-side; `until` required — run's high-water boundary captured at run start).

```json
{
  "changes": [
    {
      "sequence": 101,
      "entityType": "File",
      "entityId": "…",
      "operation": "Modified",
      "name": "home.dart",
      "parentFolderId": "…",
      "hash": "…",
      "size": 1234,
      "contentType": "text/plain",
      "originDeviceId": "…",
      "occurredAt": "2026-10-07T…Z"
    }
  ],
  "nextCursor": 104,
  "hasMore": true,
  "highWaterCursor": 120
}
```

Rules:

- Query: `WHERE UserId=? AND Sequence>? AND Sequence<=? ORDER BY Sequence ASC LIMIT n`.
- `highWaterCursor` echoes the run boundary (aids debugging/telemetry).
- Empty result → `nextCursor = since`; else `nextCursor` = highest returned `Sequence`.
- `hasMore` means more changes exist **up to `until`**, never beyond it. Changes committed after run start belong to the next coalesced run.
- Client persists the cursor **only after ALL received changes applied**. Partial apply must not advance the cursor; re-pull repeats safely.
- Loop while `hasMore=true` within the run's `[since, until]` window (see §5.4).

### 2.2 Push (existing Phase 1 endpoints, extended)

No parallel `/api/sync/push-*` endpoints. Desktop uses existing File/Folder mutation APIs with two added headers:

- `X-Device-Id` (required, validated per §3).
- `X-Operation-Id: UUID` (required for sync-engine mutations; enables idempotency).

Server flow per mutation:

```text
X-Operation-Id seen before?
  → YES + fingerprint matches: return stored ResultStatus + ResultPayload, no new mutation
  → YES + fingerprint differs: 400 OPERATION_ID_REUSE
  → NO: mutate + ChangeLog + ProcessedOperations (with ResultPayload) in ONE transaction
```

File upload keeps the Phase 1 streaming/temp-file/hash pipeline; storage finalize happens before the DB transaction, with orphan cleanup on DB failure.

### 2.3 Manifest (repair / first sync)

`GET /api/sync/manifest` returns full folders + files (+hashes) **plus `snapshotCursor`**: the highest committed ChangeLog sequence as of one consistent DB snapshot (MVCC read). Client applies manifest, sets cursor = `snapshotCursor`, then pulls `since=snapshotCursor` so concurrent changes are not lost.

### 2.4 Initial sync policy (non-empty local folder)

When the user picks a local sync root that already contains files, the desktop MUST NOT silently delete or silently upload. At setup the app requires an explicit choice:

- **(Recommended) Empty-folder setup:** user picks/creates an empty folder; first sync = manifest apply.
- **Import existing files:** user explicitly opts in; each pre-existing local file/folder is enqueued as a fresh `Create` operation (new `OperationId`), uploaded in dependency order. Local files have no server sequence, so LWW does not apply — import is additive. Name collisions with server state resolve as §5.1 409s (conflict copy, never silent overwrite).

No third behavior exists. The implementation plan must surface this choice in the setup UI; guessing is forbidden.

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

`POST /api/devices { name, platform }` — JWT only, no `X-Device-Id`, with `X-Operation-Id` for retry idempotency. Returns `deviceId`. This is when a UUID becomes legitimate.

**Web is a first-class device.** The Phase 1 React client must be updated in Phase 2: after login, register a `Web` device (`"Chrome – Windows"`-style name), persist the `deviceId` in the browser, and send `X-Device-Id` (+ `X-Operation-Id` for mutations) on every mutation/sync request. Without this, web-made changes carry `OriginDeviceId = NULL` and are indistinguishable from legacy events — breaking desktop skip-self logic and change attribution.

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

`GET /api/sync/pull?since=cursor&until=highWater`, loop `hasMore` strictly inside `[cursor, highWater]`. Apply per change: download → temp file → verify hash → atomic replace (suppression active) → update local DB by server ID → after **all** succeed, persist `nextCursor`. Crash before persist → re-pull repeats. Crash mid-file → temp discarded.

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
9. Integration tests cover: cursor ordering, `until`-bounded pulls, tombstones, idempotent retry with exact replay, fingerprint-mismatch rejection, 403 revoked, 409 preservation, recovery path, bounded runs.
10. Web client registers a device and sends `X-Device-Id` + `X-Operation-Id`; web-made changes carry real `OriginDeviceId`.
11. Non-empty local folder setup forces explicit empty/import choice; no silent delete or silent upload.
