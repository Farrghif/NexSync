# NexSync

> Sinkronisasi file pribadi — server ASP.NET Core + PostgreSQL, web React, desktop WPF (tray) Windows, dan mobile .NET MAUI Android.

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![React 19](https://img.shields.io/badge/React-19-61DAFB)](https://react.dev)
[![PostgreSQL 17](https://img.shields.io/badge/PostgreSQL-17-336791)](https://www.postgresql.org)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**Status:** Phase 2 FROZEN pada tag `v2.0-phase2` (baseline `v1.0-phase1`). Detail beku: [`docs/PHASE2-BASELINE.md`](docs/PHASE2-BASELINE.md). Bukti E2E: [`docs/PHASE2-ACCEPTANCE.md`](docs/PHASE2-ACCEPTANCE.md). Panduan dev: [`docs/dev-run.md`](docs/dev-run.md).

---

## Daftar Isi

- [Fitur](#fitur)
- [Arsitektur](#arsitektur)
- [Tech Stack](#tech-stack)
- [Struktur Project](#struktur-project)
- [Prasyarat](#prasyarat)
- [Mulai Cepat](#mulai-cepat)
- [Konfigurasi & Environment](#konfigurasi--environment)
- [Cara Kerja Sync](#cara-kerja-sync)
- [Verifikasi & Testing](#verifikasi--testing)
- [API Ringkas](#api-ringkas)
- [Dokumen](#dokumen)
- [Roadmap](#roadmap)
- [Kontribusi](#kontribusi)
- [Lisensi](#lisensi)

---

## Fitur

- **Sync multi-device** — sequence kanonis per-user, idempotent, pull `since/until/limit`, manifest `snapshotCursor`.
- **Web client** — React + Vite, daftar sebagai device `Web`, header `X-Device-Id` + `X-Operation-Id` di setiap mutasi.
- **Desktop client** — WPF tray Windows: file watcher (debounce + hash gate), antrean dependency-ordered, push/pull pipeline, anti-loop suppression, manifest reconcile, conflict & remote-delete recovery.
- **Mobile** — .NET MAUI Android: autentikasi sesi, pendaftaran device, sync file, download & open, upload, sort/search/filter "This Device".
- **Storage lokal** — file blobs di `storage/` (maks 100 MB default), metadata di PostgreSQL.

---

## Arsitektur

```
                 ┌─────────────┐
  Desktop (WPF) ─┤             ├──► PostgreSQL 17
                 │  ASP.NET    │         │
  Web (React)   ─┤  Core API   ├─────────┤  EF Core + Npgsql
                 │             │         │  ChangeLog + ProcessedOperations
  Android (MAUI)─┤             │         │  (1 transaksi idempotent)
                 └──────┬──────┘         │
                        │ storage/ ◄─────┘
                        │ (file blobs)
```

| Lapisan | Folder | Deskripsi |
|---------|--------|-----------|
| API + Domain + Infra | `backend/` | EF Core, Npgsql, JWT, sync protocol |
| Web | `web/` | React 19 + Vite + React Router + Axios |
| Desktop | `desktop/` | WPF .NET 10, SQLite lokal, single-instance tray |
| Mobile | `android/` | .NET MAUI `net10.0-android` |
| Infra lokal | `docker/` | Compose Postgres 17 untuk dev |

---

## Tech Stack

| Komponen | Teknologi |
|----------|-----------|
| Backend | ASP.NET Core (.NET 10), EF Core, Npgsql, JWT Bearer |
| Database | PostgreSQL 17 |
| Web | React 19, Vite 8, TypeScript 6, Axios, React Router 7 |
| Desktop | WPF (.NET 10), SQLite (`Microsoft.Data.Sqlite`), FileSystemWatcher |
| Mobile | .NET MAUI (`net10.0-android`) |
| Dev infra | Docker Compose, Testcontainers (tests) |

---

## Struktur Project

```
NexSync/
├── backend/
│   ├── src/
│   │   ├── NexSync.API/            # Entry point, controllers, JWT, storage
│   │   ├── NexSync.Application/    # Use-cases / handlers
│   │   ├── NexSync.Domain/         # Entities, value objects
│   │   └── NexSync.Infrastructure/ # EF Core, Npgsql, migrations
│   ├── tests/NexSync.Tests/        # Backend tests (Testcontainers)
│   └── NexSync.slnx
├── web/                            # React + Vite
│   ├── src/
│   ├── vite.config.ts
│   └── package.json
├── desktop/
│   ├── NexSync.Desktop/            # WPF tray app
│   └── NexSync.Desktop.Tests/      # 164 tests
├── android/                        # .NET MAUI Android
├── docker/
│   └── docker-compose.yml          # Postgres 17
├── docs/
│   ├── dev-run.md
│   ├── PHASE2-BASELINE.md
│   ├── PHASE2-ACCEPTANCE.md
│   └── superpowers/
├── storage/                        # (gitignored) file blobs lokal
└── README.md
```

---

## Prasyarat

| Kebutuhan | Versi | Cek |
|-----------|-------|-----|
| .NET SDK | 10.x | `dotnet --version` |
| Node.js + npm | 18+ | `node -v && npm -v` |
| Docker Desktop | terbaru | `docker info` (harus tampil `Server Version`) |
| OS Desktop | Windows 10/11 | WPF hanya Windows |
| EF Core CLI (opsional) | 10.x | `dotnet tool install --global dotnet-ef` |

> Docker wajib aktif — backend tests memakai Testcontainers.

---

## Mulai Cepat

### 1. Clone & siapkan database

```powershell
git clone https://github.com/<user>/NexSync.git
cd NexSync
docker compose -f docker/docker-compose.yml up -d
```

Kredensial dev default (lihat `backend/src/NexSync.API/appsettings.json`):

- Host `localhost:5432`, DB `nexsync`, user `nexsync`, password `nexsync_dev`

### 2. Migrasi database

```powershell
dotnet ef database update --project backend/src/NexSync.Infrastructure --startup-project backend/src/NexSync.API
```

### 3. Jalankan backend

```powershell
dotnet run --project backend/src/NexSync.API
# http://localhost:5189 (launch profile "http")
# atau http://localhost:5000 untuk proxy Vite
```

### 4. Jalankan web

> Semua perintah `npm` di bawah **wajib dari folder root repo** (`NexSync/`).
> Jika kamu di folder lain (mis. `C:\Users\farre`), akan error:
> `ENOENT ...\web\package.json` karena `--prefix web` itu relatif terhadap
> folder kamu sekarang. Fix: `cd` ke `NexSync` dulu, atau pakai `npm --prefix C:\path\ke\NexSync\web`.

```powershell
# dari root repo NexSync/
npm run install:web           # atau: npm install --prefix web
npm run dev:web               # atau: npm run dev --prefix web  → http://localhost:5173
# alternatif tanpa --prefix:
# cd web; npm install; npm run dev
```

### 5. Jalankan desktop (Windows)

```powershell
dotnet run --project desktop/NexSync.Desktop
```

Aplikasi muncul di system tray → login → pilih folder sync lokal.

### 6. Jalankan Android (opsional)

```powershell
dotnet build android/NexSync.Android.csproj -t:Run -f net10.0-android
# atau buka android/NexSync.Android.csproj di Visual Studio → Run
```

---

## Konfigurasi & Environment

### Backend (`backend/src/NexSync.API/appsettings.json`)

| Key | Default | Deskripsi |
|-----|---------|-----------|
| `ConnectionStrings:DefaultConnection` | `Host=localhost;Database=nexsync;...` | Koneksi Npgsql |
| `Storage:RootPath` | `./storage` | Folder blob file (maks 100 MB) |
| `Jwt:Secret` | (dev secret) | Secret signing JWT — **ganti di production** |
| `Cors:AllowedOrigins` | `http://localhost:5173` | Origin web yang diizinkan |

### Desktop & Web

| Variable | Default | Deskripsi |
|----------|---------|-----------|
| `NEXSYNC_API_URL` | `http://localhost:5000` | Base URL API (desktop & web proxy) |

Buat file `.env` di `web/` bila perlu override:

```ini
VITE_API_URL=http://localhost:5000
```

> Jangan commit `.env` — sudah di-gitignore. Lihat `.env.example` sebagai template.

---

## Cara Kerja Sync

1. **Sequence kanonis** — setiap perubahan file menghasilkan baris `ChangeLog` dengan sequence monotonik per-user.
2. **Idempotency** — `ProcessedOperations` + `ChangeLog` ditulis dalam **satu transaksi**; `X-Operation-Id` mencegah duplikasi saat retry.
3. **Pull** — client memanggil `GET /sync/pull?since=&until=&limit=` lalu `GET /sync/manifest?snapshotCursor=`.
4. **Push** — client mengirim mutasi (`Create`/`Update`/`Delete`/`Move`) dengan `X-Device-Id`.
5. **Desktop pipeline** — watcher → debounce + hash gate → antrean dependency-ordered → push/pull → suppression anti-loop → manifest reconcile → conflict & remote-delete recovery.
6. **Setup folder** — folder non-empty wajib pilihan eksplisit: `empty` (ditolak bila berisi) atau `import` (enqueue `Create` aditif, tabrakan nama → 409).

---

## Verifikasi & Testing

```powershell
# dari root repo NexSync/
# Desktop (164 tests)
dotnet test desktop/NexSync.Desktop.Tests/NexSync.Desktop.Tests.csproj --nologo -v q

# Backend build + tests (124 tests, butuh Docker aktif)
dotnet build backend/NexSync.slnx --nologo -v q
dotnet test backend/tests/NexSync.Tests/NexSync.Tests.csproj --nologo -v q

# Web build & lint (wajib dari root repo — lihat Mulai Cepat §4)
npm run build:web             # atau: npm run build --prefix web
npm run lint:web              # atau: npm run lint --prefix web
```

Hasil baseline `v2.0-phase2` (2026-10-12): desktop **164/164**, backend **124/124**, solution build **0 error**, web build **sukses**.

Audit dependency:

```powershell
dotnet list backend/NexSync.slnx package --vulnerable --include-transitive
dotnet list backend/NexSync.slnx package --include-transitive
```

Lihat [`docs/dev-run.md`](docs/dev-run.md) untuk daftar advisory transitif yang masih terbuka dan rencana remediation.

---

## API Ringkas

| Method | Endpoint | Auth | Deskripsi |
|--------|----------|------|-----------|
| `POST` | `/api/auth/register` | — | Registrasi user |
| `POST` | `/api/auth/login` | — | Login → JWT |
| `POST` | `/api/devices` | JWT | Daftar device |
| `GET` | `/api/devices` | JWT | List device user |
| `POST` | `/api/files` | JWT + `X-Device-Id` | Upload / mutasi file |
| `GET` | `/api/files` | JWT | List file |
| `GET` | `/api/sync/pull` | JWT | Pull changelog (`since`, `until`, `limit`) |
| `GET` | `/api/sync/manifest` | JWT | Snapshot manifest (`snapshotCursor`) |

> Base URL dev: `http://localhost:5189` (atau `http://localhost:5000` bila disamakan dengan proxy Vite).

---

## Dokumen

| Dokumen | Deskripsi |
|---------|-----------|
| [`docs/dev-run.md`](docs/dev-run.md) | Panduan dev lengkap + env + gates + catatan security |
| [`docs/PHASE2-BASELINE.md`](docs/PHASE2-BASELINE.md) | Baseline beku Phase 2 + risiko terbuka |
| [`docs/PHASE2-ACCEPTANCE.md`](docs/PHASE2-ACCEPTANCE.md) | Bukti E2E Task 20 (jangan diubah) |
| [`docs/superpowers/specs/`](docs/superpowers/specs/) | Design spec (dikunci) |
| [`docs/superpowers/plans/`](docs/superpowers/plans/) | Implementation plan (dikunci) |

---

## Roadmap

- [x] **Phase 1** — Auth, file CRUD, device registry (`v1.0-phase1`)
- [x] **Phase 2** — Sync engine + desktop client + Android (`v2.0-phase2` — FROZEN)
- [ ] **Next** — Remediation dependency transitif, hardening storage, observability, CI/CD

Lihat `docs/PHASE2-BASELINE.md` bagian "next-phase notes" untuk detail.

---

## Kontribusi

1. Fork & buat branch dari `main`.
2. Ikuti gates verifikasi di atas — semua harus hijau sebelum PR.
3. Jangan menekan warning `NU1903` untuk menyembunyikan advisory.
4. Commit message: `<scope>: <pesan singkat>` (mis. `desktop: fix watcher debounce`).

---

## Lisensi

MIT — lihat [LICENSE](LICENSE) bila tersedia.

---

<p align="center"><sub>Dibuat dengan fokus pada sinkronisasi yang benar, bukan yang cepat. PR & issue welcome.</sub></p>
