# NexSync Phase 2 — Baseline

**Tag:** `v2.0-phase2`
**Tanggal:** 2026-10-12
**Baseline sebelumnya:** `v1.0-phase1`
**Commit:** `fe1c7b6` (+ commit dokumentasi Task 21, lihat `git log`)
**Status:** Phase 2 FROZEN. Seluruh Task 01–21 APPROVED.

## Cakupan Phase 2

Server sync protocol (sequence kanonis, ChangeLog, idempotency, pull, manifest,
devices) + Web sebagai device + WPF tray desktop client (watcher, queue,
push/pull, suppression, recovery, conflict handling, setup choice) + integrasi,
E2E acceptance, dan dokumentasi finalisasi.

Riwayat lengkap: `git log --oneline v1.0-phase1..v2.0-phase2` (54 commit).

## Peta task → bukti

| Task | Hasil | Bukti |
|---|---|---|
| 01 Sync schema + migrations | DONE | `AddPhase2SyncSchema`, `SyncCounters/ChangeLogs/ProcessedOperations/Devices` |
| 02 Devices + registration API | DONE | `DeviceTests` (7), idempotent registration |
| 03 Sequence allocator | DONE | `SyncSequenceAllocatorTests` (5), commit-ordered |
| 04 ChangeLog integration | DONE | `MutationChangeLogTests` (11), atomic per mutation |
| 05 Idempotency replay | DONE | `IdempotencyTests` (9), exact replay, 400 reuse |
| 06 Pull API | DONE | `SyncPullTests` (17), since/until/limit |
| 07 Manifest API | DONE | `ManifestTests` (9), snapshotCursor konsisten |
| 08 Web as device | DONE | `WebDeviceFlowTests` (3), `ensureWebDevice`, origin attribution |
| 09 WPF foundation + tray | DONE | `DesktopFoundationTests`, single-instance mutex |
| 10 DPAPI auth + device reg | DONE | `DesktopAuthTests` (15), token round-trip, crash-safe |
| 11 SQLite local state | DONE | `LocalDbTests` (12), canonical `/` paths |
| 12 Watcher + debounce | DONE | `FolderWatcherTests` (17), hash gate, recovery flag |
| 13 Pending queue ordering | DONE | `PendingQueueTests` (14), parent-before-child |
| 14 Coordinator + push | DONE | `PushPipelineTests` (18), backoff, per-status handling |
| 15 Pull/apply atomic | DONE | `PullPipelineTests` (19), cursor-last, temp discard |
| 16 Suppression | DONE | `SuppressionTests` (18), zero self-reupload |
| 17 Recovery reconcile | DONE | `RecoveryServiceTests` (13), overflow → converge |
| 18 Conflict + remote-delete | DONE | `ConflictHandlerTests` (21), fail-closed I/O proofs |
| 19 Setup empty/import choice | DONE | `SetupServiceTests` (5), explicit choice enforced |
| 20 E2E acceptance | DONE | `TwoClientSyncTests` (9) + `docs/PHASE2-ACCEPTANCE.md`, 7/7 skenario PASS |
| 21 Docs + baseline/tag | DONE | Dokumen ini + `docs/dev-run.md` + README + tag `v2.0-phase2` |

## Gate final (hasil aktual, 2026-10-12)

- Desktop tests: **164/164 PASS**
- Backend tests: **124/124 PASS** (membutuhkan Docker aktif untuk Testcontainers)
- Solution build: **0 error**
- Web build: **sukses** (85 modules)
- Perintah: lihat `docs/dev-run.md` § Verification.

## Kontrak yang dibekukan

- `Sequence` kanonis untuk ordering; `OccurredAt` tidak pernah memutuskan konflik.
- Server IDs = identitas; path = lokasi mutable.
- Mutasi + `ChangeLog` + `ProcessedOperations` = satu transaksi DB.
- Cursor maju hanya setelah full batch teraplikasi.
- `X-Device-Id` identifier, bukan credential; JWT + ownership check wajib.
- SQLite lokal adalah durable state; in-memory-only dilarang.
- Fail-closed: I/O tak terbaca → pertahankan data, hentikan penerapan berbahaya.
- Setup folder non-empty wajib pilihan eksplisit (empty/import); tanpa silent delete/upload.

## Risiko terbuka (tidak diblokir, masuk next-phase)

Peringatan `NU1903` high-severity masih ada (terverifikasi `dotnet list ... --vulnerable`
pada 2026-10-12). Detail, analisis kompatibilitas, dan rekomendasi remediation
terpisah: lihat `docs/dev-run.md` § Dependency & security notes. Ringkasnya:

- `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 (transitif via `Microsoft.Data.Sqlite` 10.0.0,
  desktop) — `GHSA-2m69-gcr7-jv3q`, **belum ada versi patch** (affected ≤ 2.1.11).
- `Microsoft.OpenApi` 2.0.0 (transitif via `Microsoft.AspNetCore.OpenApi` 10.0.0,
  API) — `GHSA-v5pm-xwqc-g5wc`, patch 2.7.5 tersedia di line yang sama; butuh
  upgrade induk + verifikasi.
- `System.Security.Cryptography.Xml` 9.0.0 (transitif di Infrastructure) —
  8 advisory high; versi fix per advisory perlu verifikasi satu per satu.
- `SSH.NET` 2024.1.0 (transitif via rantai Testcontainers, **test-scope only**,
  tidak ikut deploy) — `GHSA-q939-rpr3-3284`, `GHSA-mggc-4xg6-vcxf`; patch 2026.0.0.

Tidak ada upgrade dependency di Task 21: remediation bukan scope plan, dan
upgrade tanpa bukti kompatibilitas berisiko merusak baseline yang dibekukan.

## Next-phase notes

- Kandidat: remediation dependency terukur (mulai dari test-scope `SSH.NET`/
  Testcontainers, lalu `Microsoft.OpenApi`, lalu Xml, terakhir SQLite yang
  belum ada patch), conflict-resolution UI, chunked upload, SignalR push,
  Android client, second-device hardening.
- Bukti penerimaan E2E: `docs/PHASE2-ACCEPTANCE.md` (jangan diubah — historis).
- Cara menjalankan: `docs/dev-run.md`.
