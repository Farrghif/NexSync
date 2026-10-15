# NexSync for Android (.NET MAUI)

Versi native Android untuk NexSync dibangun di atas **.NET MAUI (net10.0-android)** dengan sinkronisasi dua arah, autentikasi sesi terproteksi, pendaftaran perangkat native, dan pengelola berkas lengkap.

---

## 1. Arsitektur & Fitur Utama

- **Device Native:** Terdaftar sebagai platform `Android` (nama model perangkat otomatis via `DeviceInfo.Current`, misalnya `Samsung SM-G998B`).
- **Autentikasi Terproteksi:** Token akses disimpan di `SecureStorage` Android KeyStore; cookie refresh ditangani secara otomatis melalui `CookieContainer`.
- **Pengelola Berkas:**
  - Navigasi folder (breadcrumb bertingkat).
  - Tampilan item dengan ikon native, ukuran human-readable, & ekstensi.
  - Unduh langsung ke direktori cache lokal + buka via `Launcher.OpenAsync()`.
  - Unggah banyak file sekaligus menggunakan `FilePicker.Default.PickMultipleAsync()`.
  - Buat folder baru, ganti nama (rename), dan hapus berkas/folder.
- **Filter Berdasarkan Perangkat (Device Filter):**
  - Tombol toggle status baris perangkat: **"This Device"** (hanya menampilkan item yang diunggah/dimutasi oleh perangkat Android ini) vs **"Show All"**.
  - Menggunakan metadata `originDeviceId` dari backend ChangeLog.
- **Penyortiran Fleksibel:**
  - Urut nama (A–Z, Z–A).
  - Urut waktu modifikasi (Terbaru, Terlama).
  - Urut ukuran (Terbesar, Terkecil).
  - Folder selalu diutamakan di baris atas.
- **Pencarian Real-Time:** Filter berkas & folder instan di setiap folder aktif.
- **Dukungan Tema:** Terintegrasi penuh dengan mode Terang (Light) dan Gelap (Dark) sistem Android.

---

## 2. Struktur Proyek

```
android/
├── NexSync.Android.csproj          # Target net10.0-android, MAUI SourceGen enabled
├── MauiProgram.cs                  # DI Container (ApiClient, AuthService, FileService, Pages)
├── App.xaml / App.xaml.cs          # Shell bootstrap & deep-link routing
├── AppShell.xaml                   # Navigasi utama tab & routing
├── Models/
│   └── Dtos.cs                     # Kontrak data DTO mirror backend
├── Services/
│   ├── ApiConfig.cs                # URL backend (default http://10.0.2.2:5189/api di emulator)
│   ├── SecureStore.cs              # Session storage via SecureStorage + Preferences
│   ├── ApiClient.cs                # HTTP pipeline berotentikasi, X-Device-Id, & upload multipart
│   ├── AuthService.cs              # Alur login, register, logout, & enroll device Android
│   └── FileService.cs              # CRUD folder & file, getContents, & download handler
├── ViewModels/
│   └── UiItem.cs                   # Model presentasi berkas/folder untuk CollectionView
├── Pages/
│   ├── LoginPage.xaml (.cs)        # Formulir masuk native + konfigurasi URL server
│   ├── RegisterPage.xaml (.cs)     # Pendaftaran pengguna baru
│   └── BrowserPage.xaml (.cs)      # Layar utama penjelajah file native
├── Platforms/Android/
│   ├── AndroidManifest.xml         # Izin INTERNET & ACCESS_NETWORK_STATE
│   └── MainApplication.cs         # Android Application entry point
└── Resources/
    ├── AppIcon/                    # Ikon aplikasi
    ├── Splash/                     # Splash screen native
    └── Styles/                     # Gaya warna & tema MAUI
```

---

## 3. Cara Menjalankan

### A. Prasyarat
1. **.NET 10 SDK** dengan workload `android` terpasang (`dotnet workload list`).
2. **Android SDK / Android Studio** (API Level 21+ ke atas, disarankan API 34+).
3. **Android Emulator** berjalan atau **Perangkat Fisik** tersambung via USB debugging.

### B. Konfigurasi Alamat Backend
Secara bawaan, Android emulator mengakses `localhost` komputer host melalui IP `10.0.2.2`:
- Emulator Android: `http://10.0.2.2:5189/api` (atau port 5000 jika menggunakan Kestrel default).
- Perangkat fisik: Gunakan alamat IP LAN komputer Anda, misalnya `http://192.168.1.50:5189/api`.
- Anda dapat mengubah alamat server langsung dari layar login pada kolom **Server URL**.

### C. Build & Jalankan via CLI

Jalankan perintah berikut dari direktori root repository:

```powershell
# Jalankan ke emulator Android yang sedang aktif
dotnet build android/NexSync.Android.csproj -t:Run -f net10.0-android
```

Atau buka solution di Visual Studio 2022/2026, pilih target device Android emulator/fisik, dan tekan **F5**.

---

## 4. Verifikasi Status Backend & Sinkronisasi
Backend telah diperluas untuk menyertakan `OriginDeviceId` pada respon `GET /api/folders`, memungkinkan aplikasi Android maupun Web memfilter berkas berdasarkan perangkat asalnya secara akurat.
