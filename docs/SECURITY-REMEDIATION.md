# NexSync — Security Remediation (Post-Phase 2)

**Workstream:** dependency security remediation & hardening (bukan Task 22 plan Phase 2).
**Baseline:** `v2.0-phase2` (`a8fc6ba`) — immutable, tidak diubah. Kerja di branch
`security/dependency-remediation`.
**Tanggal audit:** 2026-10-13. SDK: .NET 10.0.401, runtime 10.0.12 terpasang.
**Dokumen historis tidak diubah:** `docs/PHASE2-ACCEPTANCE.md`,
`docs/PHASE2-BASELINE.md` (hasil Task 20/21 tetap apa adanya).

## 1. Hasil audit fresh (`dotnet list ... --vulnerable --include-transitive`)

Sama persis dengan baseline Task 21 — 4 paket transitif, semua High:

| # | Paket rentan | Resolved | Proyek | Rantai induk langsung | Advisory | Patch terverifikasi |
|---|---|---|---|---|---|---|
| 1 | `SQLitePCLRaw.lib.e_sqlite3` | 2.1.11 | Desktop, Desktop.Tests | `Microsoft.Data.Sqlite` 10.0.0 → `bundle_e_sqlite3` 2.1.11 | `GHSA-2m69-gcr7-jv3q` (CVE-2025-6965, SQLite < 3.50.2) | **Belum ada.** Affected ≤ 2.1.11, patched = None. Bundle 2.1.12/2.1.13 ada tapi belum terverifikasi menambal. |
| 2 | `Microsoft.OpenApi` | 2.0.0 | API, Tests | `Microsoft.AspNetCore.OpenApi` 10.0.0 | `GHSA-v5pm-xwqc-g5wc` (CVE-2026-49451, circular-ref) | **2.7.5+** (line 2.x). Nuspec `AspNetCore.OpenApi` **10.0.12** mensyaratkan `Microsoft.OpenApi [2.12.0, 3.0.0)` — upgrade induk patch. |
| 3 | `System.Security.Cryptography.Xml` | 9.0.0 | Infrastructure | `EF Design` 10.0.0 → `Build.Tasks.Core` 17.14.28 (PrivateAssets=all) | 8× High (`-23rf-…`, `-37gx-…`, `-6588-…`, `-8q5v-…`, `-cvvh-…`, `-g8r8-…`, `-mmjf-…`, `-w3x6-…`) | Tidak di-upgrade langsung. Rantai ini **build-time only** (tidak ikut `deps.json`/output deploy). |
| 4 | `SSH.NET` | 2024.1.0 | Tests (test-scope) | `Testcontainers.PostgreSql` 4.1.0 → `Testcontainers` 4.1.0 | `GHSA-q939-rpr3-3284`, `GHSA-mggc-4xg6-vcxf` | **2026.0.0**. Nuspec `Testcontainers` **4.16.0** (net10.0) mensyaratkan `SSH.NET 2026.0.0` — upgrade induk patch/minor. Test-only, tidak ikut deploy. |

Bukti nuspec resmi (api.nuget.org, dibaca saat audit):
`Testcontainers.PostgreSql 4.16.0 → Testcontainers 4.16.0 → SSH.NET 2026.0.0`;
`Microsoft.AspNetCore.OpenApi 10.0.12 → Microsoft.OpenApi [2.12.0, 3.0.0)`;
`Microsoft.Data.Sqlite 10.0.12 → bundle_e_sqlite3 2.1.12`.

## 2. Matriks prioritas & keputusan

| Grup | Exposure | Aksi | Alasan |
|---|---|---|---|
| A. Testcontainers 4.1.0 → **4.16.0** (SSH.NET → 2026.0.0) | Test-only | **UPGRADE** | Patch terverifikasi via nuspec induk; risiko rendah; tidak menyentuh runtime produksi. |
| B. AspNetCore.OpenApi 10.0.0 → **10.0.12** (OpenApi 2.0.0 → 2.12.0) | Produksi (dev-time OpenAPI doc; parser tidak memproses input tak tepercaya) | **UPGRADE** | Patch line yang sama, versi servicing Microsoft; verifikasi startup + OpenAPI endpoint + full gate. |
| C. EF Design 10.0.0 → 10.0.12 (Xml 9.0.0) | Build-time only | **UPGRADE INDUK servicing** bila aman, target: Xml keluar dari daftar vuln; bila tetap, status OPEN terdokumentasi | Rantai Xml hanya via Design (PrivateAssets=all); upgrade patch servicing, bukan override transitif. |
| D. Microsoft.Data.Sqlite 10.0.0 → 10.0.12 (bundle 2.1.11 → 2.1.12) | Produksi (SQLite lokal) | **UPGRADE + verifikasi advisory** | Hanya bila 2.1.12 terbukti menambal `GHSA-2m69-gcr7-jv3q`; bila tidak, status OPEN + mitigasi (SQLite lokal tidak memproses SQL jarak jauh tak tepercaya). DB lokal tidak dihapus/dimigrasi destruktif. |

Urutan: A → B → C → D, satu grup per langkah dengan gate terdampak.

## 3. Acceptance criteria

- Tidak ada regresi kontrak frozen (auth, sync, idempotency, queue, recovery).
- `dotnet list --vulnerable` pasca-perubahan menunjukkan advisory target hilang
  untuk grup yang di-upgrade; sisa yang terbuka tercatat eksplisit.
- Gate penuh hijau: desktop 164/164, backend 124/124 (Docker aktif), build 0 error,
  web build sukses.
- SQLite: LocalDb/migrasi/queue/recovery hijau + bukti DB lama tetap terbaca.
- Auth-related: auth/device/revoke tests hijau bila grup C/B menyentuh rantai JWT.
- Review independen PASS. Tag `v2.0-phase2` tidak diubah; tidak ada tag baru
  otomatis.

## 4. Hasil implementasi

Tanggal implementasi: 2026-10-13. Branch: `security/dependency-remediation`.

| Grup | Perubahan | Sebelum → Sesudah | Status |
|---|---|---|---|
| A. SSH.NET | `Testcontainers.PostgreSql` 4.1.0 → **4.16.0** (`NexSync.Tests.csproj`) | `SSH.NET` 2024.1.0 → **2026.0.0** | **FIXED AND VERIFIED.** Kedua advisory (`GHSA-q939-rpr3-3284`, `GHSA-mggc-4xg6-vcxf`) hilang dari `dotnet list --vulnerable`; backend 124/124 hijau (Testcontainers aktif). |
| B. OpenApi | `Microsoft.AspNetCore.OpenApi` 10.0.0 → **10.0.12** (`NexSync.API.csproj`) | `Microsoft.OpenApi` 2.0.0 → **2.12.0** | **FIXED AND VERIFIED.** Advisory `GHSA-v5pm-xwqc-g5wc` hilang; API build + smoke test manifest hijau. |
| C. Xml via EF Design | `Microsoft.EntityFrameworkCore.Design` 10.0.0 → **10.0.12** (API + Infrastructure) | `System.Security.Cryptography.Xml` 9.0.0 → hilang dari daftar vuln | **FIXED AND VERIFIED.** Kedelapan advisory Xml hilang; rantai terkonfirmasi build-time only (PrivateAssets=all, tidak ikut deploy). |
| D. SQLite | `Microsoft.Data.Sqlite` 10.0.0 → **10.0.12** (`NexSync.Desktop.csproj`) | bundle 2.1.11 → **2.1.12** | **TOOL-CLEAN, NOT CONFIRMED PATCH — residual risk OPEN.** Audit tool tidak lagi melaporkan `GHSA-2m69-gcr7-jv3q` (advisory: affected ≤ 2.1.11, patched = None), tetapi upstream belum menerbitkan versi patch yang menyatakan menambal CVE ini. Mitigasi: SQLite lokal tidak memproses SQL jarak jauh tak tepercaya; DB lokal tidak dihapus/dimigrasi destruktif. Pantau advisory untuk patch resmi. |
| Selaras | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.0 → **10.0.3** + pin `Microsoft.EntityFrameworkCore[.Relational]` **10.0.12** di Tests | CS1705 assembly mismatch | Diperlukan agar unifikasi EF 10.0.12 konsisten di solution; full gate hijau. |

Audit pasca-perubahan: **7/7 project "no vulnerable packages"**.
Gate: desktop **164/164**, backend **124/124**, solution build **0 error**,
web build **sukses**. Tidak ada warning `NU1903` tersisa.
