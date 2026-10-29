# System Design — ION PackMan · Tahap 1 

| | |
|---|---|
| **Referensi** | [PRD-ION-PackMan-Phase1.md](PRD-ION-PackMan-Phase1.md), keputusan D1–D12 |
| **Keputusan arsitektur** | [ADR-001](adr/ADR-001-process-model.md) · [ADR-002](adr/ADR-002-runtime-ui.md) · [ADR-003](adr/ADR-003-ini-engine.md) · [ADR-004](adr/ADR-004-crash-safe-execution.md) · [ADR-005](adr/ADR-005-history-storage.md) · [ADR-006](adr/ADR-006-multi-client-builds.md) |
| **Status** | Proposed — v2 |
| **Tanggal** | 28 Oktober 2026 |

> **Prinsip desain:** sederhana, modular, dan mudah dipahami junior developer.
> Satu aplikasi, satu proyek, satu folder per fitur, satu class per tugas. Tidak ada IPC, framework MVVM, interface tanpa alasan, atau mekanisme yang belum dibutuhkan PRD.

---

## 1. Requirements singkat

| Area | PRD | Yang harus didukung |
|---|---|---|
| Dashboard | R1–R2 | Baca `machine.ini`, deteksi Profile, status service & proses, versi OS |
| Components | R3–R5 | Kelola file INI `componentId@version`, import build ke local cache + SHA-256 |
| Profiles | R6–R8 | Kelola Profile INI, dependency, fitur WIND WebUI/JNLLog, validasi |
| Developer Console | R9–R11 | Gating environment, preview, eksekusi dengan backup & restore otomatis |
| History | R12 | Satu laporan per eksekusi |

**Batasan:** offline, portable (boleh dari USB), hanya environment Development untuk eksekusi, format konfigurasi INI.

**Asumsi** (perlu dikonfirmasi):

| # | Asumsi | Jika salah |
|---|---|---|
| A1 | OS 64-bit (karena path Registry memuat `WOW6432Node`) | Publish `win-x86` dan sesuaikan cara buka Registry |
| A2 | Operator di mesin dev punya kredensial admin lokal | Aplikasi tidak bisa dijalankan; model proses perlu ditinjau |
| A3 | Windows 10 IoT **Enterprise** (bukan IoT Core) | WPF tidak bisa berjalan |

---

## 2. Arsitektur

### 2.1 Gambaran besar

```
┌─────────────────── IonPackMan.exe (WPF, .NET 8, run as admin) ────────────────────┐
│                                                                                     │
│  Screens/   (tampilan saja)                                                         │
│   Dashboard · Components · Profiles · DevConsole · History                          │
│        │  memanggil ↓  (Screens tidak pernah menyentuh file/Registry/service langsung)│
│  ───────────────────────────────────────────────────────────────────────────────── │
│  Ini/          Machine/          Components/      Profiles/        History/         │
│  Deployment/   ← satu-satunya folder yang boleh MENGUBAH mesin                       │
└─────────────────────────────────────────────────────────────────────────────────────┘
        │ baca/tulis                                 │ ubah (hanya lewat Deployment/)
        ▼                                            ▼
  Folder rilis PackMan (USB/disk)              Target di mesin
   Config\allowlist.ini                         D:\ION\..., D:\APCONTAINER\...
   Components\<id>\<id>-<ver>.ini               HKLM\...\HitachiExternalParameter\...
   Profiles\<Id>.ini                            GPFCNFG.ini, WebUIUXCNFG.ini
  D:\ION-PackMan\ (disk lokal)                  Windows Services
   machine.ini
   Cache\Builds\  Cache\Backups\  Cache\History\
```

Hanya ada **satu aplikasi**. Aplikasi meminta hak admin (UAC) sekali saat dibuka ([ADR-001](adr/ADR-001-process-model.md)). Keamanan tidak bergantung pada pemisahan proses, tetapi pada **Guards** yang selalu dicek tepat sebelum eksekusi.

### 2.2 Struktur proyek

```
IonPackMan.sln
├─ src/IonPackMan/                 ← satu proyek WPF (net8.0-windows)
│  ├─ App.xaml, MainWindow.xaml    ← sidebar + area konten
│  ├─ Screens/                     ← satu UserControl per menu (XAML + code-behind)
│  │   DashboardScreen, ComponentsScreen, ProfilesScreen, DevConsoleScreen, HistoryScreen,
│  │   ProgressDialog              ← tata letak & perilaku: docs/UI-UX-Spec-ION-PackMan-Phase1.md
│  ├─ Resources/
│  │   Theme.xaml, Controls/       ← warna, style, Card, StatusPill, Notice (satu tempat)
│  ├─ Ini/
│  │   IniFile.cs                  ← baca INI dengan ketat, tulis ulang dari data
│  │   AppIniEditor.cs             ← ubah satu key di INI milik aplikasi (Win32 API)
│  ├─ Machine/
│  │   MachineConfig.cs            ← model + load machine.ini
│  │   MachineInspector.cs         ← baca service, proses, OS, disk
│  │   ProfileDetector.cs
│  ├─ Components/
│  │   ComponentRelease.cs         ← model
│  │   ComponentStore.cs           ← load/save file INI component
│  │   BuildImporter.cs            ← copy build ke cache + files.sha256
│  ├─ Profiles/
│  │   Profile.cs                  ← model
│  │   ProfileStore.cs             ← load/save file INI profile
│  │   ProfileValidator.cs
│  ├─ Deployment/
│  │   AllowList.cs                ← model + load allowlist.ini
│  │   Guards.cs                   ← cek environment, path, allow-list
│  │   DeploymentPlan.cs           ← model plan
│  │   PlanBuilder.cs              ← susun plan + daftar error
│  │   BackupStore.cs              ← buat backup, restore, cari backup yang belum selesai
│  │   Deployer.cs                 ← jalankan plan dari awal sampai akhir
│  └─ History/
│      HistoryStore.cs             ← tulis & baca laporan eksekusi
└─ tests/IonPackMan.Tests/         ← xUnit, folder mengikuti src
```

### 2.3 Aturan untuk developer

1. **Screens hanya tampilan.** Code-behind boleh berisi event handler yang memanggil class di folder fitur dan mengisi kontrol. Tidak ada logika bisnis, akses file, Registry, atau service di Screens.
2. **Hanya `Deployment/` yang mengubah mesin.** Folder lain hanya membaca mesin atau menulis file milik PackMan.
3. **Satu class, satu tugas.** Nama class menjelaskan tugasnya (`ProfileValidator`, `BackupStore`).
4. **Model adalah class data biasa** (`record` atau properti publik), tanpa logika I/O.
5. **Tidak membuat interface** kecuali memang ada dua implementasi.
6. **Error yang bisa diperkirakan dikembalikan sebagai daftar pesan** (`List<string> errors`), misalnya hasil validasi. Exception hanya untuk kegagalan tak terduga (disk penuh, akses ditolak).
7. **Semua path dan nama file PackMan ada di satu tempat:** `MachineConfig` (cache) dan konstanta di `ComponentStore`/`ProfileStore`.

### 2.4 Dependency

| Package | Untuk | Alasan |
|---|---|---|
| (bawaan .NET) `System.ServiceProcess.ServiceController` | Kontrol & status service | Bawaan platform |
| (bawaan .NET) `Microsoft.Win32.Registry` | Baca/tulis Registry | Bawaan platform |
| xUnit | Test | Standar |

Tidak ada package lain. Win32 `WritePrivateProfileString` dipanggil lewat `LibraryImport` di `AppIniEditor`.

---

## 3. Alur utama

### 3.1 Dashboard
```
MachineConfig.Load()                → nama mesin, environment, lokasi instalasi, path cache
MachineInspector.GetServices()      → status service yang dideklarasikan component di Profile
MachineInspector.GetProcesses()     → proses terkait
ProfileDetector.Detect(...)         → nama Profile atau "Unknown" + alasan
HistoryStore.GetLatest()            → status & waktu deploy terakhir
BackupStore.FindUnfinished()        → jika ada: tampilkan peringatan + tombol Restore
```

### 3.2 Import build
```
BuildImporter.Import(componentId, version, sourceFolder):
  cek ruang disk cache
  copy folder → Cache\Builds\<id>\<ver>.tmp\
  hitung SHA-256 setiap file → tulis files.sha256
  rename .tmp → <ver>
  ComponentStore: isi [LocalBuild] lalu simpan
```

### 3.3 Developer Console: Preview → Execute

Pilihan operasi di layar hanya tiga:

| Pilihan | Arti |
|---|---|
| **Deploy files** | Copy build ke target. Jika service sedang Running, otomatis di-stop sebelum copy dan di-start lagi sesudahnya |
| **Apply config** | Terapkan entry Registry/INI yang aktif di Profile |
| **Restart service** | Restart service komponen (bisa dipilih sendiri tanpa Deploy) |

```
Preview:
  plan, errors = PlanBuilder.Build(profile, component, buildVersion, pilihan)
  errors kosong → tampilkan plan (file baru/diganti, Registry/INI lama → baru, langkah service)
  errors ada    → tampilkan daftar error, tombol Execute nonaktif
  setiap input berubah → plan dihapus, Execute nonaktif lagi

Execute (setelah konfirmasi):
  Deployer.Run(plan)
```

### 3.4 `Deployer.Run` — satu method yang bisa dibaca dari atas ke bawah

```csharp
public DeploymentResult Run(DeploymentPlan plan)
{
    var errors = Guards.Check(plan, machine, allowList);       // dicek ulang tepat sebelum eksekusi
    if (errors.Count > 0) return DeploymentResult.Rejected(errors);

    var backup = backupStore.Create(plan);                       // tulis backup.ini Status=InProgress
    try
    {
        if (plan.StopServiceFirst) services.Stop(plan.ServiceName, TimeSpan.FromSeconds(30));
        if (plan.DeployFiles)      CopyFiles(plan);              // file lama yang tidak ada di build dibiarkan
        if (plan.ApplyConfig)      ApplyConfig(plan);            // Registry + AppIniEditor
        if (plan.StartServiceAfter) services.StartAndWaitRunning(plan.ServiceName, TimeSpan.FromSeconds(30));

        backupStore.MarkDone(backup);
        return history.Write(plan, backup, Outcome.Succeeded);
    }
    catch (Exception ex)
    {
        var restoreErrors = backupStore.Restore(backup);         // kembalikan semua isi backup
        var outcome = restoreErrors.Count == 0 ? Outcome.FailedRestored : Outcome.FailedRestoreIncomplete;
        return history.Write(plan, backup, outcome, ex, restoreErrors);
    }
}
```

### 3.5 Backup & restore ([ADR-004](adr/ADR-004-crash-safe-execution.md))

`BackupStore.Create(plan)` dijalankan **sebelum perubahan apa pun**:
1. Salin setiap file target yang akan diganti ke `Cache\Backups\<execId>\files\`.
2. Hash ulang salinan dan bandingkan dengan file asli. Jika berbeda, eksekusi dibatalkan.
3. Tulis `backup.ini`:

```ini
[Meta]
SchemaVersion=1.0

[Backup]
ExecId=20261028-101530-wind-3f9a
Status=InProgress              ; InProgress | Done | Restored | RestoreIncomplete
ServiceName=WIND
ServiceWasRunning=true

[ReplacedFiles]                ; restore = salin balik dari files\
1=D:\APCONTAINER\WIND\WIND.exe

[NewFiles]                     ; restore = hapus
1=D:\APCONTAINER\WIND\new.dll

[Registry:WebUISupport]        ; restore = tulis nilai lama, atau hapus value jika Existed=false
Key=HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI
ValueName=Support
Existed=true
OldValue=0

[Ini:WebUIJnlLog]
File=D:\APCONTAINER\Config\WebUIUXCNFG.ini
Section=Debug
Key=JNLLog
Existed=true
OldValue=0
```

`BackupStore.Restore(backup)` selalu mengembalikan **seluruh isi** `backup.ini`, apa pun langkah yang sudah berjalan. Karena itu restore aman diulang dan bisa dipakai setelah crash.

**Setelah crash atau mati listrik:** saat aplikasi dibuka, `BackupStore.FindUnfinished()` mencari `backup.ini` dengan `Status=InProgress` atau `RestoreIncomplete`. Jika ada, Developer Console dikunci dan Dashboard menampilkan tombol **Restore**.

### 3.6 Guards

| Cek | Aturan |
|---|---|
| Environment | `machine.ini` valid dan `Environment=Development` |
| Path | `Path.GetFullPath(target)` harus berada di bawah salah satu `[BaseDirectories]` di allow-list; tolak path relatif, `..`, dan UNC |
| Allow-list | Registry key, file INI, dan nama service harus ada di `allowlist.ini` |
| Build | Hash file build di cache cocok dengan `files.sha256` |
| Disk | Ruang kosong cukup untuk backup + copy |
| Backup tertunda | Tidak ada backup dengan `Status=InProgress` atau `RestoreIncomplete` |

### 3.7 INI ([ADR-003](adr/ADR-003-ini-engine.md))

| Class | Untuk | Cara kerja |
|---|---|---|
| `IniFile` | File milik PackMan (machine, allowlist, component, profile, backup, history) | Baca baris per baris menjadi `section → key → value`. Error dengan nomor baris untuk duplikat, key tidak dikenal, atau key wajib kosong. Tulis dengan membuat ulang seluruh file dari data |
| `AppIniEditor` | INI milik aplikasi (`GPFCNFG.ini`, `WebUIUXCNFG.ini`) | `GetPrivateProfileString` / `WritePrivateProfileString` bawaan Windows: hanya mengubah key yang dituju, sisa file tidak disentuh |

Aturan per file (key wajib, nilai yang diizinkan) ditulis langsung di method `Load` masing-masing store, sehingga mudah dibaca tanpa framework skema.

### 3.8 Registry

Path di Profile sudah memuat `WOW6432Node`. Selalu buka dengan:

```csharp
using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
```

Jika dibuka dengan view 32-bit, Windows mengalihkan path menjadi `WOW6432Node\WOW6432Node\...` dan nilainya tidak sampai ke aplikasi ATM.

### 3.9 Deteksi Profile

```
jika machine.ini mengisi Profile → pakai itu
selain itu:
  Profile cocok jika TargetDirectory semua komponennya ada di mesin
  tepat satu cocok → terdeteksi
  nol atau lebih dari satu → "Unknown" + alasan (isi Profile di machine.ini untuk memilih)
```

### 3.10 Satu instance

`App.OnStartup` membuat `Mutex` bernama `IonPackMan.SingleInstance`. Jika sudah ada, tampilkan pesan lalu tutup. Ini mencegah dua deploy berjalan bersamaan.

---

## 4. Penyimpanan

| Data | Lokasi | Ditulis oleh |
|---|---|---|
| `machine.ini` | `D:\ION-PackMan\` | Developer (manual) |
| `allowlist.ini` | `<rilis>\Config\` | Developer (manual) |
| Component INI | `<rilis>\Components\<id>\<id>-<ver>.ini` | `ComponentStore` |
| Profile INI | `<rilis>\Profiles\<Id>.ini` | `ProfileStore` |
| Build | `Cache\Builds\<id>\<ver>\` + `files.sha256` | `BuildImporter` |
| Backup | `Cache\Backups\<execId>\` (`files\` + `backup.ini`) | `BackupStore` |
| History | `Cache\History\<execId>.ini` | `HistoryStore` |

`execId` = `yyyyMMdd-HHmmss-<componentId>-<4 hex acak>`.

Semua file PackMan ditulis dengan pola **tulis ke `.tmp` → rename**, agar tidak pernah tertinggal setengah tertulis.

---

## 5. Error handling

| Situasi | Penanganan |
|---|---|
| Input atau konfigurasi tidak valid | Ditampilkan sebagai daftar error saat Preview. Tidak ada perubahan pada mesin |
| Guard gagal saat Execute | Ditolak. Tidak ada perubahan pada mesin |
| Error saat eksekusi (service tidak berhenti, file terkunci, akses ditolak, disk penuh) | Restore otomatis, lalu catat ke History |
| Restore tidak tuntas | Outcome `FailedRestoreIncomplete`. History mencantumkan item yang gagal. Backup berstatus `RestoreIncomplete` sehingga Developer Console tetap terkunci dan tombol Restore tetap tersedia |
| Crash / mati listrik | Restore ditawarkan saat aplikasi dibuka lagi |

---

## 6. Pengujian

| Yang diuji | Cara |
|---|---|
| `IniFile` | Unit test: parse, error duplikat/key tidak dikenal, tulis lalu baca menghasilkan data sama |
| `ProfileValidator`, `Guards`, `PlanBuilder`, `ProfileDetector` | Unit test dengan data di memori dan folder sementara |
| `BuildImporter`, `BackupStore` | Test dengan folder sementara (create → ubah file → restore → isi sama dengan awal) |
| `Deployer` + `AppIniEditor` + Registry + service | Test manual/terskrip di VM atau ATM dev: deploy WIND, gagalkan di tiap langkah, matikan aplikasi di tengah copy, lalu Restore |

---

## 7. Beban & risiko

| Hal | Estimasi / risiko | Penanganan |
|---|---|---|
| Eksekusi | ≤ 50/hari/mesin | Tidak perlu optimasi |
| Build | ≤ 2.000 file, 100–500 MB | Hash dan copy beberapa detik |
| Cache tanpa retensi (D8) | Bisa tumbuh beberapa GB per hari | Dashboard menampilkan ukuran cache; Guard menolak jika disk tidak cukup |
| Aplikasi berjalan sebagai admin | Bug di UI bisa berdampak lebih besar | Hanya `Deployment/` yang mengubah mesin; Guards selalu dicek sebelum eksekusi; aplikasi khusus mesin Development |

---

## 8. Yang ditinjau ulang nanti

| Kapan | Apa |
|---|---|
| Tahap 2 (Release) | `Deployer` dipakai untuk banyak komponen sekaligus; urutan stop/start mengikuti `dependsOn` |
| PackMan dipakai di Test/Production atau oleh operator non-admin | Pisahkan bagian yang mengubah mesin menjadi proses/service terpisah (lihat ADR-001 opsi B) |
| Cache terlalu besar | Tambah retensi (simpan N backup terakhir) |
| Health check per komponen | Ganti cek "service Running" dengan cek spesifik per komponen |
