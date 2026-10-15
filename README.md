# NexSync — Phase 2 (Sync Engine + Desktop Client)

NexSync adalah sinkronisasi file pribadi: server ASP.NET Core + PostgreSQL,
web client React, dan desktop client WPF (tray) untuk Windows.

**Status:** Phase 2 FROZEN pada tag `v2.0-phase2` (baseline `v1.0-phase1`).
Detail beku: `docs/PHASE2-BASELINE.md`. Bukti penerimaan E2E:
`docs/PHASE2-ACCEPTANCE.md`. Cara menjalankan: `docs/dev-run.md`.

## Arsitektur singkat

- `backend/` — API + domain + infrastruktur (EF Core, Npgsql). Sync protocol:
  sequence kanonis per user, `ChangeLog` + idempotency `ProcessedOperations`
  dalam satu transaksi, pull `since/until/limit`, manifest `snapshotCursor`.
- `web/` — React + Vite. Mendaftar sebagai device `Web`, mengirim
  `X-Device-Id` + `X-Operation-Id` pada mutasi.
- `desktop/` — WPF tray (.NET 10): watcher (debounce + hash gate), antrean
  dependency-ordered, push/pull pipeline, suppression anti-loop, manifest
  reconcile, conflict + remote-delete recovery, explicit empty/import setup.
- `android/` — Aplikasi mobile native (.NET MAUI net10.0-android): autentikasi sesi,
  pendaftaran perangkat Android, sinkronisasi file, download & open, upload berkas,
  penyortiran, pencarian, dan filter khusus perangkat ini (This Device).
- `docker/` — compose Postgres 17 untuk dev lokal.

## Mulai cepat

```powershell
docker compose -f docker/docker-compose.yml up -d
dotnet run --project backend/src/NexSync.API
npm run dev --prefix web
dotnet run --project desktop/NexSync.Desktop
```

Lihat `docs/dev-run.md` untuk kredensial dev, migrasi, env, dan gates.

## Verifikasi

```powershell
dotnet test desktop/NexSync.Desktop.Tests/NexSync.Desktop.Tests.csproj --nologo -v q
dotnet build backend/NexSync.slnx --nologo -v q
dotnet test backend/tests/NexSync.Tests/NexSync.Tests.csproj --nologo -v q
npm run build --prefix web
```

Baseline `v2.0-phase2`: desktop **164/164**, backend **124/124**,
solution build **0 error**, web build **sukses**.

## Dokumen

- `docs/superpowers/specs/2026-10-07-nexsync-phase2-design.md` — design (dikunci)
- `docs/superpowers/plans/2026-10-07-nexsync-phase2-implementation.md` — plan (dikunci)
- `docs/PHASE2-ACCEPTANCE.md` — bukti E2E Task 20 (historis, jangan diubah)
- `docs/PHASE2-BASELINE.md` — baseline beku + risiko terbuka + next-phase notes
- `docs/dev-run.md` — panduan dev + catatan dependency/security
