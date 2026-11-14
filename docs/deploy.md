# NexSync — Deploy: Vercel (web) + ngrok (backend)

Panduan ini menyiapkan NexSync agar bisa dibuka dari internet:
**web di-host di Vercel**, **backend berjalan di komputer kamu** dan
diekspos ke internet lewat **ngrok**.

```
Browser  →  https://<nama>.vercel.app  (web statis)
                │  request /api/* diproxy oleh Vercel
                ▼
           https://<id>.ngrok-free.app  (terowongan ngrok)
                ▼
           backend kamu  →  http://localhost:5189  (.NET)
                ▼
           PostgreSQL di Docker  →  http://localhost:5432
```

**Kenapa lewat proxy Vercel?** Semua request jadi *same-origin* (satu
domain), sehingga cookie sesi (`SameSite=Strict`), CSRF, dan CORS pada
backend tetap berfungsi tanpa mengubah kode apa pun, dan file besar
(100 MB) tidak melewati fungsi serverless Vercel yang dibatasi 4,5 MB.

## 0. Prasyarat

- Akun [Vercel](https://vercel.com) (gratis) dan akun [ngrok](https://dashboard.ngrok.com) (gratis)
- Tool terpasang: `node`/`npm`, `dotnet` (.NET 10 SDK), Docker Desktop, [`vercel` CLI](https://vercel.com/docs/cli) (`npm i -g vercel`), [`ngrok` CLI](https://ngrok.com/download)
- Kode sudah pernah lolos build lokal (lihat `docs/dev-run.md` bagian 6)

## 1. Jalankan database (sekali, atau setelah restart PC)

```powershell
docker compose -f docker/docker-compose.yml up -d
```

Migrasi bila skema berubah (dari folder `backend/`):

```powershell
dotnet ef database update --project src/NexSync.Infrastructure --startup-project src/NexSync.API
```

## 2. Jalankan backend dengan secret produksi

`appsettings.json` memuat JWT secret versi dev yang ada di repo — siapa
pun yang membaca repo bisa memalsukan token. **Wajib diganti** saat
dipublikasikan. Sekali per sesi PowerShell (sebelum `dotnet run`):

```powershell
# generate secret acak 48 karakter
$env:Jwt__Secret = -join ((48..57)+(65..90)+(97..122) | Get-Random -Count 48 | ForEach-Object { [char]$_ })
dotnet run --project backend/src/NexSync.API
```

Backend harus tetap berjalan di terminal ini (port `5189`).
Catatan: env var `Jwt__Secret` memetakan ke konfigurasi `Jwt:Secret`;
secret harus ≥ 32 karakter, kalau tidak backend menolak start.

## 3. Buka terowongan ngrok

Agar URL ngrok tidak berubah tiap restart (gratis: 1 domain statis per
akun), daftar dulu di [ngrok Dashboard → Domains](https://dashboard.ngrok.com/domains)
(New Static Domain), lalu jalankan (terminal kedua):

```powershell
ngrok http 5189 --domain=<domain-statis-kamu>
```

Tanpa domain statis, cukup `ngrok http 5189` — tapi URL-nya berubah
tiap kali ngrok restart (lihat bagian 6). Salin URL HTTPS-nya, misalnya
`https://abc123.ngrok-free.app`.

## 4. Arahkan Vercel ke URL ngrok

Ganti placeholder di `web/vercel.json`:

```json
"destination": "https://abc123.ngrok-free.app/api/:path*"
```

Lalu deploy (terminal ketiga, dari folder `web/`):

```powershell
cd web
vercel login        # sekali saja
vercel --prod       # deploy ke produksi
```

Alternatif lewat dashboard: import repository → **Root Directory =
`web`** → tetap harus mengedit `web/vercel.json` seperti di atas.

## 5. Verifikasi

1. Buka URL Vercel yang tercetak (mis. `https://nexsync-web.vercel.app`).
2. Register/login → dashboard tampil.
3. Upload file kecil → muncul di daftar, bisa diunduh lagi.
4. Login ulang dari tab lain tidak kena rate limit (limit: 5 request
   login/60 detik per IP — berlaku global, tunggu sebentar bila kena 429).

## 6. Perawatan

| Situasi | Yang dilakukan |
|---|---|
| Backend/ngrok dimatikan | Jalankan ulang langkah 1–3. **Tidak perlu** deploy Vercel ulang. |
| URL ngrok berubah (tanpa domain statis) | Edit `web/vercel.json` → `vercel --prod` ulang. |
| Ganti JWT secret | Stop backend → set `$env:Jwt__Secret` baru → start. User harus login ulang. |
| Web diubah | `cd web` → `vercel --prod`. |
| Backend diubah | Stop → `dotnet run` ulang (otomatis compile). |

## Troubleshooting

- **Web tampil tapi API error (502/504)** → ngrok mati atau URL di
  `vercel.json` salah. Cek terminal ngrok masih jalan.
- **Login 429** → rate limit 5/60 detik per IP. Tunggu satu menit.
- **Upload >4,5 MB gagal** → kemungkinan batas proxy Vercel; laporkan
  ke maintainer, solusinya mode direct (lihat catatan risiko di bawah).
- **Cookie/sesi cepat hilang** → pastikan akses lewat domain Vercel
  (https), bukan membuka `localhost` langsung.

## Risiko & batasan yang perlu diketahui

- Backend hidup di komputer kamu: PC harus menyala selama layanan
  publik aktif. Untuk produksi rutin, backend pindah ke VPS/cloud.
- ngrok gratis: ada batas koneksi/menit dan bisa sleep; domain statis
  disarankan.
- JWT secret & URL ngrok jangan di-commit bila sudah berisi nilai asli
  (`web/vercel.json` berisi URL ngrok — nilai lokal, aman selama tidak
  ada rahasia lain di dalamnya).
- Mode direct (tanpa proxy Vercel): set `VITE_API_BASE` saat build +
  `Cors:Origins` + ubah cookie `SameSite` ke `None` — tidak direkomendasikan
  tanpa review keamanan.
