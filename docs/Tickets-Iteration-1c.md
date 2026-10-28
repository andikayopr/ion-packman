# Tiket Implementasi — Iterasi 1c (Developer Console & eksekusi aman)

**Acuan:** PRD Draft v5 (R8–R12), System Design v2 §3.3–§3.8, ADR-001 (satu app admin), ADR-004 (satu `backup.ini`, restore menyeluruh), UI/UX Spec §4.4.
**Ukuran:** S ≤ 1 hari · M ≈ 2–3 hari · L ≈ 4–5 hari.

> **Ini iterasi pertama yang mengubah mesin** (file, Registry, INI aplikasi, service). Aturan kerja:
> 1. Semua logika diuji dulu di **folder sementara dengan Registry dan service palsu**; tidak ada tes otomatis yang menyentuh Registry/service ATM.
> 2. Hanya folder `Deployment/` yang boleh mengubah mesin (System Design §2.3). Layar tidak pernah menyentuh file/Registry/service langsung.
> 3. Uji nyata (T31) hanya di **VM atau ATM dev**, bukan di komputer kerja.
> 4. Interface hanya di dua tempat yang memang punya dua implementasi (asli + palsu untuk tes): `IRegistryAccess` dan `IServiceControl`.

## Tiket

| ID | Tiket | Ukuran | Bergantung | Acceptance (ringkas) |
|---|---|---|---|---|
| **T20** ✅ | **`AppIniEditor`** (`Deployment/`): baca/ubah satu key INI milik aplikasi lewat `Get/WritePrivateProfileString`; `Exists`, `Get`, `Set`, hapus key | S | — | Hanya key yang dituju berubah, sisa file (komentar, urutan, section lain) identik byte-per-byte; key belum ada → `Exists=false`; tes pada salinan file; **cek encoding** (ANSI vs UTF-8 non-ASCII) dan dokumentasikan hasilnya |
| **T21** ✅ | **`IRegistryAccess`** + `RegistryAccess` asli (`Registry64`, view 64-bit) + `FakeRegistry` untuk tes: `Read(key, name)` → ada/nilai, `Write`, `Delete` | S | — | Path "HKLM\SOFTWARE\WOW6432Node\…" tidak dialihkan dua kali; tipe `DWORD`/`String`; tes asli hanya pada kunci uji `HKCU\Software\IonPackMan.Tests\<guid>` yang dibersihkan; tes lain memakai fake |
| **T22** ✅ | **`IServiceControl`** + `ServiceControl` asli (`ServiceController`) + `FakeServiceControl`: `GetState`, `Stop(timeout)`, `StartAndWaitRunning(timeout)` | S | — | Timeout menghasilkan error (bukan hang); service tidak ada → error jelas; fake bisa diprogram gagal di langkah tertentu |
| **T23** ✅ | **`DeploymentPlan` + `PlanBuilder`** (R10): dari Profile + component + build + pilihan operasi menyusun plan atau daftar error; diff file (baru/diganti/sama) dari hash build vs target; nilai Registry/INI lama → baru; langkah Stop/Start otomatis bila Deploy files dan service Running | L | T20–T22 | Semua kondisi blokir R10 menghasilkan pesan jelas (bukan bagian profile, copy tanpa build, build tidak valid, target kosong/di luar base dir, service tanpa nama/tidak ditemukan, cache tidak valid); file di target yang tidak ada di build **tidak** dihapus |
| **T24** ✅ | **`Guards`** (§3.6): Environment Development, path di bawah base directory, allow-list (Registry/INI/service), hash build cocok `files.sha256`, ruang disk, tidak ada backup tertunda | M | T23 | Tiap cek punya tes lulus dan gagal; dipanggil ulang di `Deployer.Run` (tidak hanya di Preview); `..`, UNC, symlink/junction keluar base dir ditolak |
| **T25** ✅ | **`BackupStore`** (ADR-004): `Create` (salin file yang akan diganti, hash ulang dan batalkan bila beda, tulis `backup.ini` `Status=InProgress` **sebelum** perubahan apa pun), `Restore` (seluruh isi, aman diulang), `MarkDone`, `FindUnfinished` | L | T20–T22 | Create → ubah → Restore → isi identik; Restore dua kali tetap identik; Restore saat sebagian langkah belum jalan aman; file baru dihapus, nilai Registry/INI lama dikembalikan atau value dihapus bila `Existed=false`; status `RestoreIncomplete` bila ada yang gagal |
| **T26** ✅ | **`Deployer.Run`** (§3.4) + tulis History: Guards → backup → stop → copy → apply config → start → health check (status Running) | L | T23–T25, T16 | Uji penyuntikan gagal di **setiap langkah** (backup, stop timeout, copy tengah jalan, Registry ditolak, INI gagal, start gagal): hasil `FailedRestored`, isi mesin palsu identik dengan sebelumnya, service kembali ke state awal; sukses menulis `Succeeded`; outcome `Rejected` bila Guards gagal dan **tidak ada** yang berubah |
| **T27** | **`DevConsoleScreen`** (UI/UX §4.4): Profile (terdeteksi/pilihan), Component, Imported build, target & service read-only, tiga operasi, Preview, Execution Preview (ringkas + daftar file di Expander), **action bar lengket**, konfirmasi Execute | L | T23, T24 | Execute nonaktif sampai Preview valid; **setiap perubahan input menghapus preview**; Environment bukan Development → layar terkunci; teks action bar menjelaskan alasan Execute nonaktif; muat 1024×768 |
| **T28** | **`ProgressDialog` + eksekusi di background** (`Task.Run`, `IProgress`): langkah dan tanda ✔, tidak bisa ditutup saat berjalan, jendela utama tidak bisa ditutup saat eksekusi (konfirmasi + peringatan) | M | T26, T27 | UI tidak membeku; hasil akhir (sukses/gagal/restore) tampil di Execution Preview; tombol Close baru muncul setelah selesai |
| **T29** ✅ | **Pemulihan setelah crash**: banner merah + tombol **Restore** di Dashboard, Developer Console terkunci, History `RestoredAfterCrash` | M | T25, T26 | Backup `InProgress`/`RestoreIncomplete` saat aplikasi dibuka → banner + konsol terkunci; Restore mengembalikan semua dan menulis History; titik status topbar kuning selama ada backup tertunda |
| **T30** | **"Use in Developer Console"** (P1-4) dari Components + dialog progres untuk **Import build** (pakai komponen T28) | S | T27, T28 | Component, build, target, dan service terisi di konsol; import build besar tidak membekukan UI |
| **T31** | **Uji manual & fixture** di VM/ATM dev: tulis ulang `docs/Manual-Test-1c.md` dan `tools/PackManTestFixture.ps1` (masih untuk desain lama): deploy WIND, gagalkan di tiap langkah, **matikan aplikasi di tengah copy lalu Restore**, uji gating environment, uji disk penuh/file terkunci | M | T26–T29 | Skrip dan prosedur dijalankan di VM; hasil dicatat; exit criteria PRD §13 terpenuhi |

## Urutan dan paralelisasi

```
Paralel (3 agen, tidak saling bergantung):   T20   T21   T22
Setelah ketiganya:                           T23 → T24            (T23 dan T24 satu orang)
Paralel dengan T23/T24:                      T25 (BackupStore, hanya butuh T20–T22)
Setelah T23–T25:                             T26
UI paralel setelah kontrak T23 jelas:        T27 → T28 → T29 → T30
Terakhir:                                    T31 (manual, di VM)
```

Aturan koordinasi agen paralel (dari pengalaman 1a–1b): tiap agen memakai **folder scratchpad kerja yang unik** (mis. `work-t20`), tidak memakai folder bersama, dan hanya menyalin file barunya ke proyek asli.

## Keputusan yang saya asumsikan (koreksi bila salah)

| # | Asumsi | Alasan |
|---|---|---|
| 1 | Timeout stop dan start service masing-masing **30 detik** | Nilai di prototype dan System Design §3.4 |
| 2 | Health check = **status service Running** setelah start (tanpa health check per component) | PRD R11; health check per component masih open question |
| 3 | File terkunci/read-only di target = **error → restore**, tidak dipaksa menimpa | Aman; bisa dilonggarkan setelah uji di ATM dev |
| 4 | **Apply config** tanpa **Restart service** tidak me-restart service | Dua pilihan terpisah (System Design §3.3) |
| 5 | Registry hanya tipe **DWORD** dan **String** | Keputusan T10 dan asumsi PRD |
| 6 | Operator di History = user Windows saat ini (`Environment.UserDomainName\UserName`) | Open question PRD "operator identity" |
| 7 | Satu eksekusi = **satu component** | Mengikuti PRD R9 (banyak component = Tahap 2) |

## Yang dibutuhkan sebelum T31

- VM atau ATM dev dengan Windows 10 IoT Enterprise 64-bit, service WIND terpasang, dan akses admin.
- Salinan nyata `GPFCNFG.ini` dan `WebUIUXCNFG.ini` (hasil T20 bergantung pada encoding-nya).
- Daftar target directory dan nama service yang benar untuk qraptor dan non-ndc-parser.
