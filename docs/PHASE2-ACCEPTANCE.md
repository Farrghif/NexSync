# NexSync Phase 2 — Acceptance (Task 20)

**Task:** 20 — End-to-end acceptance (two-client proof: desktop ⇄ web via server)
**Baseline:** `9f53807` (Task 19 APPROVED & FROZEN)
**Tanggal:** 2026-10-12
**Metode:** Automated two-client proof. Dua `ApiClient` (device `Windows` = desktop,
device `Web` = browser) berbicara ke server yang sama via `WebApplicationFactory` +
Testcontainers PostgreSQL 17. Sisi desktop (push pipeline, pull-apply ke disk, offline
queue→reconnect, reconcile, setup choice) dibuktikan dengan stub `HttpMessageHandler` +
wiring `SyncEngine` produksi dan kontrak HTTP yang identik dengan server. Ini BUKAN
skenario manual (tidak ada klik UI WPF/browser dan tidak ada server `dotnet run`
background — sesuai constraint plan, verifikasi via `WebApplicationFactory`/unit test).
Tidak ada perubahan kontrak produksi pada Task 20 kecuali penambahan tes testimony.

## Skenario (plan Task 20) — semua PASS

| # | Skenario plan | Hasil | Testimony (test → bukti) |
|---|---|---|---|
| 1 | Register → setup empty folder → create/edit/rename/move/delete lokal → terlihat di web | PASS | `TwoClientSyncTests.DesktopLifecycle_CreateEditRenameMoveDelete_WebPullSeesAllWithDesktopOrigin` (create→Modified→Renamed→Moved→Deleted satu file, pull web melihat kelimanya dengan `originDeviceId` desktop); `DesktopFolderTree_WebListsSameStructure` (tree folder+file desktop terbaca via `GET /folders` web); `DesktopUpload_WebReadsSameBytes_OriginAttributed` (byte-identik + origin); setup empty: `SetupServiceTests.EmptyFolder_ApplyEmpty_SucceedsRecordsChoice`; push lokal: `PushPipelineTests` (18 tes, header `X-Operation-Id`/`X-Device-Id`) |
| 2 | Web edits → pulled to disk | PASS | `TwoClientSyncTests.WebRenameMoveDelete_DesktopPullSeesSnapshotsWithWebOrigin` (rename+move+delete via device web → snapshot pull `Renamed`/`Moved`/`Deleted` dengan origin web; tombstone tanpa hash); `PullPipelineTests.WebMadeChange_PulledToDisk_ByteIdentical` (pull `Modified` dari device web ditulis ke disk byte-identik + hash tercatat + cursor maju, via wiring pipeline produksi); `ConflictHandlerTests.ProductionWiring_SyncEngine_SharesOneConflictHandlerEndToEnd` |
| 3 | Offline burst → reconnect converges | PASS | `PushPipelineTests.OfflineOp_ReconnectPushesWithSameOperationId` (offline beneran: transport `HttpRequestException` → op retained; reconnect → push sukses dengan `OperationId` yang sama sehingga replay idempoten, `ServerFileId` tercatat); `TwoClientSyncTests.BurstWrites_PagedPull_ConvergesExactlyOnce` (10 writes, pull paged `limit=3` → sequence 1..10 tepat sekali, cursor=10); antrean: `PendingQueueTests` (14 tes) |
| 4 | Timeout retry → single event | PASS | `TwoClientSyncTests.TimeoutRetry_SingleEvent_WebSeesOnce` (retry `X-Operation-Id` sama → id entity sama, web hanya melihat 1 change); `IdempotencyTests.RetryAfterSimulatedTimeout_ReturnsByteIdenticalPayload_NoSecondChange`, `CreateFolder_WithOperationId_ThenIdenticalRetry_ReplaysWithoutSecondMutation`, `ConcurrentDuplicateOperationId_ExecutesMutationExactlyOnce`; fingerprint beda → 400 (`SameOperationId_DifferentBody_Returns400_OperationIdReuse`) |
| 5 | Revoke device → 403 | PASS | `TwoClientSyncTests.RevokedDesktop_Blocked403_WebUnaffected` (device desktop di-revoke → mutasi + pull 403, device web tetap bisa mutasi); `WebDeviceFlowTests.WebFlow_RevokedDevice_BlockedWithClearStatus`; `MutationChangeLogTests.RevokedDevice_Returns403`, `SyncPullTests.Pull_RevokedDevice_Returns403`, `ManifestTests.Manifest_RevokedDevice_Returns403` |
| 6 | Overflow → reconcile | PASS | Server leg: `TwoClientSyncTests.ManifestSnapshot_ConcurrentWebWrite_PulledExactlyOnceAfterCursor` (snapshot manifest + write konkuren → pull `since=snapshot` menghasilkan change tepat sekali dengan origin web); client leg: `RecoveryServiceTests.OverflowMidBurst_ConvergesLocalToServer` (reconcile konvergen, cursor=42, file hash-identik diadopsi); `SwallowedOverflow_WithoutReconcile_DivergenceRemainsDetectable`; flag overflow: `FolderWatcherTests` (error → `RecoveryRequired`) |
| 7 | Non-empty setup → explicit choice enforced | PASS | Server leg: `TwoClientSyncTests.ImportCollision_SecondUpload409_FirstUploadIntact` (upload nama sama → 409, file pertama utuh — sesuai spec §2.4 name collision jadi 409, bukan silent overwrite); client leg: `SetupServiceTests` (5 tes: empty menolak folder berisi; import enqueue fresh `Create` terurut dependensi, aditif, abaikan `.nexsync`, abort atomik; mutasi silent-delete/skip-unreadable terbukti gagal) |

## Gate penuh (hasil aktual Task 20)

- Desktop: **164/164 PASS** (`dotnet test desktop/NexSync.Desktop.Tests/...`; 162 baseline + `OfflineBurst_ReconnectPushesAll` + `WebMadeChange_PulledToDisk`)
- Backend: **124/124 PASS** (115 baseline + 9 `TwoClientSyncTests`)
- Solution build: **0 error** (`dotnet build backend/NexSync.slnx --nologo -v q`)
- Web build: **sukses** (`npm run build --prefix web`, 85 modules)
- Testimony baru: `backend/tests/NexSync.Tests/TwoClientSyncTests.cs` (9 tes) + 2 tes desktop, semua PASS

## Batasan yang dicatat jujur

- Tidak ada klik UI manual dan tidak ada server `dotnet run` background; sesuai constraint
  plan, verifikasi via `WebApplicationFactory`/unit test dengan kontrak HTTP identik.
- Peringatan dependency NU1903 masih terbuka (mis. `SQLitePCLRaw.lib.e_sqlite3` 2.1.11
  `GHSA-2m69-gcr7-jv3q` transitif via `Microsoft.Data.Sqlite`, plus `Microsoft.OpenApi`,
  `System.Security.Cryptography.Xml`): di luar scope Task 20, tidak diubah, masuk radar
  finalisasi/Task 21.

## Keputusan

Semua 7 skenario Task 20 recorded **PASS** dengan ref build/testimony di atas.
