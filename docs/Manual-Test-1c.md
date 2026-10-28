# Panduan Uji Manual — ION PackMan Tahap 1c

Tujuan: membuktikan di mesin Windows sungguhan hal-hal yang **tidak bisa dibuktikan unit test**: elevasi UAC saat aplikasi dibuka,
start/stop service sungguhan, penulisan Registry 64-bit (`WOW6432Node`), file terkunci, aplikasi dimatikan paksa di tengah eksekusi,
dan tampilan pada monitor 1024×768. Sekaligus menjawab asumsi **A1–A3**.

> **Jalankan hanya di VM atau mesin dev.** Uji ini menghentikan/menyalakan sebuah service dan menulis ke HKLM.
> Fixture memakai sandbox `C:\PackManTest` dan key `HKLM\SOFTWARE\WOW6432Node\PackManTest`; tidak menyentuh `D:\ION`,
> `D:\APCONTAINER`, atau Registry aplikasi asli. Service pengganti WIND adalah **Spooler** (aman dimatikan di VM;
> ganti dengan `-ServiceName` bila perlu).
>
> Desain yang diuji: **satu aplikasi** yang berjalan sebagai admin (UAC sekali saat dibuka), tanpa Agent dan tanpa journal
> (ADR-001, ADR-004). Keamanan eksekusi bertumpu pada **Guards**, satu `backup.ini` per eksekusi, dan restore menyeluruh.

## 0. Prasyarat — sekaligus jawaban A1–A3

Catat hasilnya; ini menentukan apakah ADR-001 valid di mesin target.

| # | Cek | Cara | Hasil |
|---|---|---|---|
| A1 | OS 64-bit | `[Environment]::Is64BitOperatingSystem` → `True` | |
| A2 | UAC aktif | `(Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System).EnableLUA` → `1` | |
| A2 | Operator = admin lokal (UAC meminta "Yes/No", bukan password akun lain) | `whoami /groups` memuat `S-1-5-32-544` | |
| A3 | Windows 10 IoT **Enterprise** (bukan IoT Core) | `winver`; aplikasi WPF harus bisa terbuka | |
| — | Monitor **1024×768**, skala **100%** | Pengaturan layar | |

Jika **A2 gagal** (operator bukan admin): aplikasi tidak bisa dijalankan sama sekali; ADR-001 perlu ditinjau ulang.
Runtime .NET **tidak perlu** dipasang: aplikasi dipublikasikan self-contained.

## 1. Publish dan salin

Di mesin pengembangan:

```powershell
dotnet publish src\IonPackMan -c Release -r win-x64 --self-contained -o publish
```

Salin folder `publish\` ke VM, mis. `C:\PackMan\`. Salin juga `tools\PackManTestFixture.ps1`. (Ukuran ±160 MB karena runtime ikut dibawa.)

## 2. Siapkan sandbox (PowerShell **Administrator**)

```powershell
.\PackManTestFixture.ps1 -Action Setup
.\PackManTestFixture.ps1 -Action State > before.txt      # baseline untuk dibandingkan
```

Jalankan PackMan. **UAC muncul sekali saat dibuka**; setujui. Opsi `--release` dan `--machine` mengarahkannya ke sandbox
(tanpa opsi, ia memakai folder di samping `.exe` dan `D:\ION-PackMan\machine.ini`):

```powershell
C:\PackMan\IonPackMan.exe --release C:\PackManTest\release --machine C:\PackManTest\machine.ini
```

> `machine.ini` dibaca **saat aplikasi dibuka**. Setelah mengubahnya, tutup dan buka lagi aplikasinya.
> **Reset antar-skenario:** tutup PackMan → `-Action Cleanup` → `-Action Setup` → buka PackMan → impor build lagi (T2).

## 3. Skenario

Untuk tiap skenario bandingkan `-Action State` dengan `before.txt` (atau dengan hasil yang diharapkan).

### T1 — Dashboard dan deteksi
- Dashboard: Environment **Development**; Detected Profile **Wind Test** (pill "Detected"); service **Spooler** Running di tabel ION Services.
- Titik status topbar **hijau**; nama mesin dan "Development" tampil di topbar.
- **Lulus jika:** Task Manager (kolom *Elevated*) menunjukkan `IonPackMan.exe` **elevated**, dan tidak ada banner merah.

### T2 — Impor build
- Components → buka `wind` → **Import local build…** → pilih `C:\PackManTest\buildsrc\wind-2.1.0`.
- **Lulus jika:** toast "Imported 2 files…"; kolom *Local build* menampilkan `wind-2.1.0 · 2 files`; ada
  `C:\PackManTest\Cache\Builds\wind@2.1.0\` berisi `files\`, `build.ini`, `files.sha256`.
- Impor ulang: muncul dialog **Replace imported build**; **Cancel** tidak mengubah apa pun.

### T3 — Preview
- Developer Console → Profile **WINDTEST**, component `wind`, build `2.1.0`, centang **Deploy files** dan **Apply config** → **Preview plan**.
- **Lulus jika:** ringkasan menampilkan *Files: 1 replace, 1 new*; *Service: Spooler is Running: Stop → Start* (otomatis karena Running);
  *Config:* Registry `Support 0 → 1` dan `DisplayFlag (not set) → 1`, INI `[WebUI] Support 0 → 1` dan `[Debug] JNLLog 0 → 1`;
  *Guards:* semua ✔; `keep.txt` tidak tercantum di daftar file.
- **Ubah apa pun** (centang, build, component): preview hilang dan **Execute nonaktif** lagi.

### T4 — Eksekusi sukses (jalur utama)
- **Preview plan** → **Execute** → konfirmasi OK. Dialog progres menampilkan langkah dengan tanda ✔ dan tidak bisa ditutup.
- **Lulus jika** hasil hijau (Succeeded) dan `-Action State` menunjukkan:
  - `a.dll` baru, `sub\b.dll` ada, **`keep.txt` hash tidak berubah**;
  - INI: `Support=1` dan `JNLLog=1`; komentar `; INI aplikasi`, `Other=keep`, dan CRLF **utuh**;
  - Registry: `Support=1` dan `DisplayFlag=1` **pada `WOW6432Node\PackManTest`**, bukan `WOW6432Node\WOW6432Node\...`
    (cek: `reg query HKLM\SOFTWARE\WOW6432Node\WOW6432Node /s` → tidak ada);
  - service **Running**;
  - `Cache\Backups\<execId>\backup.ini` berisi `Status=Done`; `Cache\History\<execId>.ini` ada dengan `Outcome=Succeeded`;
  - tab **History** menampilkan baris itu; Dashboard *Deployment State* = **Succeeded**.

### T5 — Konfirmasi dibatalkan
- Reset → impor → Preview → Execute → **Cancel** di dialog konfirmasi.
- **Lulus jika:** tidak ada yang berubah (`State` identik baseline), tidak ada folder `Backups\<execId>` baru, tidak ada History baru.

### T6 — File terkunci
Di PowerShell kedua: `.\PackManTestFixture.ps1 -Action LockFile` (menahan `a.dll`; Enter melepasnya).
- Reset → impor → Preview → **tahan kunci** → Execute. Copy `a.dll` gagal (tidak ada percobaan ulang pada desain ini).
- **Lulus jika:** hasil **FailedRestored** dengan pesan yang menyebut `a.dll`; `State` identik baseline (a.dll lama, INI/Registry/service
  seperti semula); History `FailedRestored`; Developer Console tetap bisa dipakai (tidak terkunci).

### T7 — Restore tidak lengkap → konsol terkunci → Restore dari Dashboard
- Reset, impor, lalu `Set-Service Spooler -StartupType Disabled` (service tidak bisa dinyalakan lagi).
- Preview (Deploy + Apply config) → Execute.
- **Lulus jika:** hasil **FailedRestoreIncomplete**; pesan menyebut service tidak bisa dikembalikan; file, INI, dan Registry **sudah
  kembali** (periksa `State`); Dashboard menampilkan **banner merah** dengan tombol **Restore**; titik status topbar **kuning**;
  Developer Console **terkunci** ("Recovery needed"); Preview/Execute baru tidak bisa.
- `Set-Service Spooler -StartupType Automatic` → Dashboard **Restore** → konfirmasi.
- **Lulus jika:** banner hilang; titik topbar hijau; service **Running**; `State` identik baseline; History punya baris `RestoredAfterCrash`;
  `backup.ini` berstatus `Restored`; konsol bisa dipakai lagi.

### T8 — Aplikasi dimatikan paksa di tengah copy (crash/mati listrik)
- `.\PackManTestFixture.ps1 -Action LargeBuild` (401 file × 1 MB); hapus `Cache\Builds\wind@2.1.0` bila ada; impor build ini.
- Preview → Execute; saat dialog progres di **CopyingFiles**, matikan: `Stop-Process -Name IonPackMan -Force` (admin).
- **Lulus jika:** `target` berisi sebagian file (`State`); `Cache\Backups\<execId>\backup.ini` berstatus **`InProgress`**;
  service **Stopped** (dihentikan oleh deploy).
- Buka lagi PackMan (UAC). **Lulus jika:** banner merah di Dashboard menyebut `wind (<execId>): InProgress`; titik status kuning;
  Developer Console terkunci.
- **Restore** → **Lulus jika:** `State` identik baseline; `keep.txt` utuh; folder `sub\` yang baru dibuat **hilang** lagi;
  service **Running** kembali; History `RestoredAfterCrash`.
- Ulangi dengan mematikan aplikasi pada saat berbeda (sebelum copy, saat ApplyingConfig) bila bisa.

### T9 — Gating Environment
- Ubah `Environment=Test` di `C:\PackManTest\machine.ini`, **tutup dan buka lagi** PackMan.
- **Lulus jika:** menu **Developer Console** nonaktif (tooltip "Only available on Development machines"); topbar menampilkan
  "… · Test"; tombol "Use in Developer Console" di Components hanya menampilkan toast penjelasan dan tidak membuka konsol.
- Kembalikan ke `Development`.

### T10 — Build berubah setelah Preview
- Preview (valid) → ubah isi `Cache\Builds\wind@2.1.0\files\a.dll` → Execute.
- **Lulus jika:** hasil **Rejected** (build hash berbeda); `State` identik baseline; History `Rejected`; tidak ada folder backup baru.

### T11 — Jendela tidak bisa ditutup saat eksekusi
- Mulai eksekusi dengan build besar (seperti T8) dan coba menutup jendela utama (tombol X atau Alt+F4).
- **Lulus jika:** muncul peringatan "An execution is still running…" dan aplikasi tetap terbuka; setelah selesai jendela bisa ditutup.

### T12 — Cache di media removable (opsional)
- Ubah `LocalCacheDirectory` di `machine.ini` ke drive USB, buka ulang aplikasi.
- **Lulus jika:** Preview ditolak dengan pesan cara memperbaiki `LocalCacheDirectory`; impor build ditolak dengan pesan serupa.

### T13 — Backup rusak (opsional)
- Buat folder `Cache\Backups\20260101-000000-wind-0000\` berisi `backup.ini` dengan isi sembarang (`[Backup` saja).
- **Lulus jika:** Dashboard menampilkan masalahnya di banner (tombol Restore nonaktif karena tidak ada yang bisa dipulihkan otomatis);
  Developer Console terkunci sampai folder itu dibereskan manual.

### T14 — Tampilan di 1024×768 (monitor nyata)
- Skala 100%: buka tiap menu (Dashboard, Components, Profiles, Developer Console, History) dengan jendela maximize.
- **Lulus jika:** tidak ada scroll horizontal; di Developer Console tombol **Preview plan/Execute** selalu terlihat tanpa scroll;
  teks terbaca (≥12px) dan tidak terpotong tanpa tooltip.
- Skala 125%: **Lulus jika** sidebar ciut menjadi ikon saja dan semua tetap muat.

## 4. Yang dicatat untuk setiap skenario

Tanggal, build/commit, hasil (Lulus/Gagal), `-Action State` sesudahnya, isi `Cache\History\*.ini`, dan `Cache\Backups\<execId>\backup.ini`.
Jika ada yang gagal, lampirkan `backup.ini` dan `History` — keduanya memuat apa yang dicatat tepat sebelum kegagalan.

| Skenario | Tanggal | Hasil | Catatan |
|---|---|---|---|
| T1 … T14 | | | |

## 5. Bersihkan

Tutup PackMan, lalu:

```powershell
.\PackManTestFixture.ps1 -Action Cleanup
```

Menghapus `C:\PackManTest`, key Registry uji, dan mengembalikan service ke Automatic/Running.

## 6. Keputusan setelah uji

| Hasil | Tindakan |
|---|---|
| T1–T11 lulus, T14 lulus | Lanjut ke 1d (hardening) dan uji pada data component nyata (nama service, path, Registry asli, encoding `GPFCNFG.ini`/`WebUIUXCNFG.ini`). |
| A2 gagal | Tinjau ulang ADR-001 sebelum melanjutkan. |
| T4 menulis Registry ke tempat salah | Blocker: periksa `RegistryAccess` (view 64-bit). |
| T7/T8 `State` tidak identik baseline | Blocker: tambahkan tes `Deployer`/`BackupStore` untuk titik yang ditemukan sebelum apa pun. |
| T4: INI aplikasi rusak (komentar/CRLF hilang) | Blocker: periksa `AppIniEditor` dan encoding file INI sungguhan. |
| T14 ada layar yang meluap | Perbaiki tata letak layar itu; tambahkan ke `LayoutQaTests`. |
