# Panduan Uji Manual — ION PackMan Tahap 1c

Tujuan: membuktikan di mesin Windows sungguhan hal-hal yang **tidak bisa dibuktikan unit test**: elevasi UAC, kontrol service,
penulisan Registry 64-bit (`WOW6432Node`), file terkunci, dan pemulihan setelah proses dimatikan paksa.
Sekaligus menjawab asumsi **A1–A3** di System Design §9.

> **Jalankan hanya di VM atau mesin dev.** Uji ini menghentikan/menyalakan sebuah service dan menulis ke HKLM.
> Fixture memakai sandbox `C:\PackManTest` dan key `HKLM\SOFTWARE\WOW6432Node\PackManTest`; tidak menyentuh `D:\ION`,
> `D:\APCONTAINER`, atau Registry aplikasi asli. Service pengganti WIND adalah **Spooler** (aman dimatikan di VM;
> ganti dengan `-ServiceName` bila perlu).

## 0. Prasyarat — sekaligus jawaban A1–A3

Catat hasilnya di tabel; ini yang menentukan apakah ADR-001 (Agent sesuai permintaan via UAC) valid.

| # | Cek | Cara | Hasil |
|---|---|---|---|
| A1 | OS 64-bit | `[Environment]::Is64BitOperatingSystem` → `True` | |
| A2 | UAC aktif | `(Get-ItemProperty HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System).EnableLUA` → `1` | |
| A2 | Operator = admin lokal (prompt UAC "Yes/No", bukan minta password) | `whoami /groups` memuat `S-1-5-32-544` | |
| A3 | Windows 10 IoT **Enterprise** (bukan IoT Core) | `winver`; WPF harus bisa jalan | |
| — | .NET 8 Desktop Runtime terpasang | `dotnet --list-runtimes` memuat `Microsoft.WindowsDesktop.App 8.` | |

Jika **A2 gagal** (UAC dimatikan atau operator bukan admin): hentikan; ADR-001 harus diganti ke Windows Service terpasang.

## 1. Build dan salin

Di mesin pengembangan:

```bash
dotnet build -c Release
```

Salin seluruh folder `src\IonPackMan.App\bin\Release\net8.0-windows\` ke VM, mis. `C:\PackMan\`. Folder itu sudah berisi
`IonPackMan.Agent.exe` (disalin otomatis saat build). Salin juga `tools\PackManTestFixture.ps1`.

## 2. Siapkan sandbox (PowerShell **Administrator**)

```powershell
.\PackManTestFixture.ps1 -Action Setup
.\PackManTestFixture.ps1 -Action State > before.txt      # baseline untuk dibandingkan
```

Jalankan PackMan **sebagai user biasa** (bukan Run as administrator):

```powershell
C:\PackMan\IonPackMan.App.exe --release C:\PackManTest\release --machine C:\PackManTest\machine.ini
```

> **Reset antar-skenario:** `-Action Cleanup`, lalu `-Action Setup`, lalu impor build lagi (T2).

## 3. Skenario

Untuk tiap skenario bandingkan `-Action State` dengan `before.txt` (atau dengan hasil yang diharapkan).

### T1 — Dashboard dan deteksi
- Dashboard: Environment **Development**, Profile **Wind Test (WINDTEST)** terdeteksi, service **Running**.
- Tab **Masalah** kosong.
- **Lulus jika:** Process Explorer/Task Manager menunjukkan `IonPackMan.App.exe` **tidak elevated**.

### T2 — Impor build
- Components → pilih `wind 2.1.0` → **Impor build…** → `C:\PackManTest\buildsrc\wind-2.1.0`.
- **Lulus jika:** status "diimpor (2 file)"; ada `Cache\Builds\wind\2.1.0\files.sha256`; `LocalBuild` di
  `release\Components\wind\wind-2.1.0.ini` terisi; mengimpor lagi ditolak ("tidak boleh ditimpa").

### T3 — Preview
- Developer Console → `wind`, centang **Copy** dan **Terapkan Registry/INI** → **Preview**.
- **Lulus jika:** langkah memuat `StopService`, `CopyFiles`, `ApplyConfig`, `StartService`, `HealthCheck` (Stop/Start
  **otomatis** karena service Running); file `a.dll` = *timpa*, `sub\b.dll` = *baru*; 4 entri config
  (2 Registry, 2 INI); ada `Plan hash`; `keep.txt` tidak tercantum.

### T4 — Eksekusi sukses (jalur utama)
- **Eksekusi…** → Ya → **prompt UAC muncul** → setujui.
- **Lulus jika** hasil `Succeeded` dan `-Action State` menunjukkan:
  - `a.dll` baru, `sub\b.dll` ada, **`keep.txt` hash tidak berubah**;
  - INI: `Support=1` dan `JNLLog=1`, komentar `; INI aplikasi` + `Other=keep` + CRLF **utuh**;
  - Registry: `Support=1` dan `DisplayFlag=1` — **pada key `WOW6432Node\PackManTest`**, bukan di
    `WOW6432Node\WOW6432Node\...` (cek: `reg query HKLM\SOFTWARE\WOW6432Node\WOW6432Node /s` → tidak ada);
  - service **Running**; tidak ada file `*.pmnew`;
  - `Cache\History\<execId>.ini` ada (tab History menampilkannya); `Cache\Journal\<execId>.journal` berakhir `COMMITTED`;
  - `IonPackMan.Agent.exe` **sudah berhenti** atau berhenti ≤5 menit setelahnya.

### T5 — UAC dibatalkan
- Reset → impor → Preview → Eksekusi → **No** di prompt UAC.
- **Lulus jika:** pesan "Elevasi (UAC) dibatalkan"; `State` identik dengan baseline; tidak ada journal.

### T6 — File terkunci
Di PowerShell kedua: `.\PackManTestFixture.ps1 -Action LockFile` (menahan `a.dll`; tekan Enter melepasnya).

- **T6a — kunci sementara (retry):** tahan kunci → Eksekusi → setelah progres "CopyFiles: Running", **tekan Enter dalam ≤7 detik**.
  **Lulus jika:** `Succeeded` (copy dicoba ulang dengan jeda 1/2/4 detik).
- **T6b — copy gagal lalu restore berhasil:** reset → tahan kunci → Eksekusi → **jangan lepas** sampai ±8 detik (copy gagal),
  lalu **tekan Enter dalam ±7 detik berikutnya** (restore mencoba ulang).
  **Lulus jika:** `FailedRestored` dan `State` identik dengan baseline (a.dll lama, ini/registry/service seperti semula).

### T7 — Restore tidak lengkap → kunci → Recover
- Reset, impor, lalu `Set-Service Spooler -StartupType Disabled` (service tak bisa distart ulang).
- Preview (Copy + config) → Eksekusi → **Lulus jika:** `FailedRestoreIncomplete`; tab Developer Console menampilkan
  "Ada eksekusi yang belum selesai…" dan tombol **Recover…**; Eksekusi baru **tidak bisa**.
- `Set-Service Spooler -StartupType Automatic` → **Recover…** (UAC) → **Lulus jika:** `RecoveredAfterCrash`; `State`
  identik baseline; tombol Recover hilang; Eksekusi bisa lagi.

### T8 — Proses Agent dimatikan paksa (crash)
- `.\PackManTestFixture.ps1 -Action LargeBuild` (401 file × 1 MB), impor build ini (hapus `Cache\Builds\wind\2.1.0` dulu bila ada).
- Eksekusi; saat progres di **CopyFiles**, matikan: `Stop-Process -Name IonPackMan.Agent -Force` (admin).
- **Lulus jika:** UI menampilkan "Koneksi ke Agent terputus"; `Cache\Journal` punya journal **tanpa**
  `COMMITTED/RESTORED/ABORTED/RECOVERED`; `target` berisi sebagian file (`State`); buka ulang PackMan → Console terkunci;
  **Recover…** → `RecoveredAfterCrash`, `State` identik baseline, `keep.txt` utuh, folder `sub\` yang baru dibuat hilang lagi.
- Ulangi dengan mematikan Agent pada saat berbeda (sesudah copy / saat ApplyConfig) bila bisa.

### T9 — Gating Environment
- Ubah `Environment=Test` di `C:\PackManTest\machine.ini` → Refresh.
- **Lulus jika:** Console nonaktif dengan pesan "hanya untuk Environment=Development".
- Lanjutan (uji sisi Agent): kembalikan ke `Development`, Preview, lalu **ubah ke `Production` sebelum klik Eksekusi**
  → hasil `Rejected` dari Agent ("Developer Console hanya untuk Environment=Development"); mesin tidak berubah.

### T10 — Plan berubah sejak preview
- Preview (service Running) → `Stop-Service Spooler` → Eksekusi.
- **Lulus jika:** `Rejected` — "Plan berubah sejak preview… Preview ulang."; tidak ada perubahan.

### T11 — Cache di media removable (opsional)
- Ubah `LocalCacheDirectory` ke drive USB → Refresh.
- **Lulus jika:** Console nonaktif dan impor build ditolak ("drive removable/jaringan tidak diizinkan").

### T12 — Hak akses pipe (opsional, keamanan)
- Saat Agent hidup (setelah T4, ≤5 menit): dari **akun user lain** coba buka `\\.\pipe\ionpackman-*`.
- **Lulus jika:** akses ditolak. Nonce salah → Agent menutup koneksi (sudah teruji otomatis; ini verifikasi ACL nyata).

## 4. Yang dicatat untuk setiap skenario

Tanggal, build/commit, hasil (Lulus/Gagal), `-Action State` sesudahnya, isi `Cache\History\*.ini`, dan `Cache\Journal\*.journal`.
Jika ada yang gagal, lampirkan keduanya — journal memuat urutan kejadian tepat sebelum kegagalan.

## 5. Bersihkan

```powershell
.\PackManTestFixture.ps1 -Action Cleanup
```

Menghapus `C:\PackManTest`, key Registry uji, dan mengembalikan service ke Automatic/Running.

## 6. Keputusan setelah uji

| Hasil | Tindakan |
|---|---|
| T1–T10 lulus | Lanjut ke 1d (hardening) dan uji pada data komponen nyata (nama service, path, Registry asli). |
| A2 gagal | Ganti ADR-001 ke Windows Service terpasang sebelum melanjutkan. |
| T4 menulis Registry ke tempat salah | Perbaiki `WindowsRegistryStore` (pemilihan RegistryView) — blocker. |
| T8 state tidak identik | Blocker: tambah fault-injection test untuk titik yang ditemukan. |
