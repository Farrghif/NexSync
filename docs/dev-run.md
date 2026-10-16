# NexSync — Dev Run Guide

Panduan menjalankan backend, web, desktop, dan verifikasi Phase 2 secara lokal.
Target framework: .NET 10, PostgreSQL 17, Node (Vite), Windows untuk desktop WPF.

## 1. Prasyarat

- .NET SDK 10 (`dotnet --version`)
- Node.js + npm (untuk `web/`)
- Docker Desktop aktif (wajib untuk backend tests via Testcontainers; `docker info`
  harus menampilkan `Server Version`)
- EF Core CLI bila perlu migrasi manual: `dotnet tool install --global dotnet-ef`
  (terpasang: 10.0.12)

## 2. Database lokal (dev)

`docker/docker-compose.yml` hanya menjalankan Postgres 17:

```powershell
docker compose -f docker/docker-compose.yml up -d
```

Kredensial default dev (lihat `backend/src/NexSync.API/appsettings.json`):

- Host `localhost:5432`, DB `nexsync`, user `nexsync`, password `nexsync_dev`
- Koneksi dipakai via `ConnectionStrings:DefaultConnection`
  (`AddInfrastructure` → `UseNpgsql`)

Migrasi (dari folder `backend/`):

```powershell
dotnet ef database update --project src/NexSync.Infrastructure --startup-project src/NexSync.API
```

## 3. Backend API

```powershell
dotnet run --project backend/src/NexSync.API
```

- Launch profile `http`: `http://localhost:5189` (lihat
  `backend/src/NexSync.API/Properties/launchSettings.json`).
- Konfigurasi dev lain (`appsettings.json`): JWT dev secret, `Storage:RootPath`
  (`./storage`, maks 100 MB), CORS origin `http://localhost:5173`.
- Jangan jalankan server sebagai child process dari executor pengujian;
  plan mewajibkan verifikasi via `WebApplicationFactory`/unit test.

## 4. Web client

> **Wajib dari root repo `NexSync/`.** Opsi `--prefix web` itu relatif terhadap
> folder kamu sekarang. Jika dijalankan dari folder lain (mis. `C:\Users\farre`),
> akan error `ENOENT ...\web\package.json`. Fix: `cd` ke `NexSync/` dulu
> atau pakai path absolut `--prefix C:\path\ke\NexSync\web`.

```powershell
# dari root repo NexSync/
npm run install:web           # atau: npm install --prefix web
npm run dev:web               # atau: npm run dev --prefix web  → http://localhost:5173
npm run build:web             # atau: npm run build --prefix web  (tsc + vite)
# alternatif tanpa --prefix:
# cd web; npm install; npm run dev
```

- Dev server mem-proxy `/api` → `http://localhost:5000` (lihat `web/vite.config.ts`).
  Untuk dev lokal, samakan port API (`NEXSYNC_API_URL`) atau jalankan API di 5000.
- Setelah login, web mendaftarkan device `Web` (`ensureWebDevice` di
  `web/src/api/auth.ts`) dan mengirim `X-Device-Id` + `X-Operation-Id`
  pada setiap mutasi.

## 5. Desktop client (Windows)

```powershell
dotnet run --project desktop/NexSync.Desktop
```

- Aplikasi tray WPF: login → pilih folder lokal → status. Satu instance
  (`Global\NexSync-Desktop`, lihat `SingleInstance.MutexName`).
- Data lokal: `<sync-root>\.nexsync\` (SQLite `local.db`, log, temp, recovery).
  Watcher mengabaikan `.nexsync\` dan `*.nexsync-temp`.
- Folder non-empty wajib pilihan eksplisit: empty (ditolak bila berisi) atau
  import (enqueue fresh `Create`, aditif, tabrakan nama → 409).
- Env yang didukung: `NEXSYNC_API_URL` (default `http://localhost:5000`).

## 6. Verification (gates)

```powershell
# dari root repo NexSync/
dotnet test desktop/NexSync.Desktop.Tests/NexSync.Desktop.Tests.csproj --nologo -v q
dotnet build backend/NexSync.slnx --nologo -v q
dotnet test backend/tests/NexSync.Tests/NexSync.Tests.csproj --nologo -v q
npm run build:web             # atau: npm run build --prefix web
```

Hasil baseline `v2.0-phase2` (2026-10-12): desktop **164/164**, backend
**124/124**, solution build **0 error**, web build **sukses**.

Audit dependency:

```powershell
dotnet list backend/NexSync.slnx package --vulnerable --include-transitive
dotnet list backend/NexSync.slnx package --include-transitive
```

## 7. Dependency & security notes (terbuka, tidak diblokir)

Hasil `dotnet list ... --vulnerable --include-transitive` (2026-10-12, SDK aktual).
Semua temuan di bawah adalah **transitif**; tidak ada `PackageReference`
langsung ke paket rentan di csproj mana pun.

| Paket rentan | Versi | Proyek terdampak | Rantai induk | Advisory | Status fix (terverifikasi) |
|---|---|---|---|---|---|
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.11 | `NexSync.Desktop`, `NexSync.Desktop.Tests` | `Microsoft.Data.Sqlite` 10.0.0 → `bundle_e_sqlite3` 2.1.11 | `GHSA-2m69-gcr7-jv3q` (CVE-2025-6965, SQLite < 3.50.2, CVSS 7.2) | **Belum ada versi patch.** Advisory: affected ≤ 2.1.11, patched = None. Bundle terbaru di NuGet adalah 2.1.13/3.0.x — belum terverifikasi menambal CVE ini. |
| `Microsoft.OpenApi` | 2.0.0 | `NexSync.API`, `NexSync.Tests` | `Microsoft.AspNetCore.OpenApi` 10.0.0 | `GHSA-v5pm-xwqc-g5wc` (CVE-2026-49451, circular-ref stack overflow, CVSS 7.5) | Patch tersedia: **2.7.5** (line 2.x). Butuh upgrade paket induk `Microsoft.AspNetCore.OpenApi` ke versi yang membawa 2.7.5+ + full gate. |
| `System.Security.Cryptography.Xml` | 9.0.0 | `NexSync.Infrastructure` | rantai `Microsoft.IdentityModel`/`System.IdentityModel.Tokens.Jwt` 8.x | 8 advisory high (`GHSA-23rf-6693-g89p`, `-37gx-xxp4-5rgx`, `-6588-8gv4-xfgh`, `-8q5v-6pqq-x66h`, `-cvvh-rhrc-wg4q`, `-g8r8-53c2-pm3f`, `-mmjf-rqrv-855v`, `-w3x6-4m5h-cxqf`) | Versi 9.0.1–9.0.20 dan 10.0.x tersedia; fix per advisory belum dipetakan satu per satu — perlu audit lanjutan. |
| `SSH.NET` | 2024.1.0 | `NexSync.Tests` (test-scope only) | rantai Testcontainers | `GHSA-q939-rpr3-3284` (path traversal), `GHSA-mggc-4xg6-vcxf` (RCE via SCP path) | Patch tersedia: **2026.0.0**. Perlu upgrade Testcontainers ke versi yang membawa fix. Tidak ikut binary deploy. |

Keputusan Task 21: **tidak ada upgrade.** Remediation bukan scope plan Task 21
("Docs + Phase 2 baseline/tag"), dan upgrade tanpa bukti kompatibilitas
berisiko merusak baseline beku — termasuk SQLite yang belum ada patch-nya.
Rekomendasi pekerjaan keamanan terpisah: mulai dari test-scope (`SSH.NET` via
Testcontainers), lalu `Microsoft.OpenApi` (induk `AspNetCore.OpenApi`),
lalu Xml, terakhir SQLite (pantau advisory; pertimbangkan mitigasi sementara
karena SQLite lokal tidak memproses input SQL jarak jauh yang tidak tepercaya).
Jangan menekan warning (`NU1903`) untuk menyembunyikan masalah.

Catatan tambahan: `NU1510` (`System.Security.Cryptography.ProtectedData`
tidak perlu direferensikan eksplisit di `NexSync.Desktop.csproj`) adalah
peringatan hygiene, bukan kerentanan.
