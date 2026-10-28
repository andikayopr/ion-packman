# Tiket Implementasi — Iterasi 1a & 1b

**Acuan:** PRD Draft v5 (§9 R1–R8, R12 skeleton, §13), System Design v2, ADR-001…005, UI/UX Spec (1024×768).
**Ukuran:** S ≤ 1 hari · M ≈ 2–3 hari · L ≈ 4–5 hari (satu developer).
**Aturan:** setiap tiket selesai bila acceptance terpenuhi, unit test hijau, dan kode mengikuti aturan System Design §2.3 (Screens hanya tampilan, hanya `Deployment/` mengubah mesin; iterasi ini **tidak menulis ke Registry/service/folder aplikasi ATM** sama sekali).

---

## Iterasi 1a — Fondasi data (Components, Profiles, INI)

| ID | Tiket | Ukuran | Bergantung | Acceptance (ringkas) |
|---|---|---|---|---|
| **T01** ✅ | **Scaffold solusi.** Proyek WPF `net8.0-windows` + proyek xUnit; manifest `requireAdministrator`; mutex satu-instance; class `Paths` (semua path di satu tempat) | S | — | Aplikasi terbuka, UAC muncul; instance kedua menampilkan pesan lalu keluar; publish `self-contained win-x64` berjalan dari folder tanpa .NET |
| **T02** ✅ | **Theme & kontrol bersama.** `Theme.xaml` (token UI/UX §3) + `Card`, `StatusPill`, `Notice/SuccessBox/ErrorBox`, `KeyValueRow`, `EmptyState`, `Toast`, style `DataGrid` | M | T01 | Halaman contoh menampilkan semua kontrol; warna/ukuran sesuai spec (font 13, kontrol 32, baris 34) |
| **T03** ✅ | **Shell `MainWindow`.** Sidebar 184px (5 menu), topbar 52px (breadcrumb, titik status, mesin·environment), `ContentControl` untuk layar, sidebar ciut < 900 DIP | M | T02 | Navigasi antar layar kosong; tampil benar di 1024×700; tidak ada scroll horizontal |
| **T04** ✅ | **`IniFile`** (baca ketat: duplikat section/key = error dengan nomor baris; tulis ulang dari data via `.tmp`→rename) + `IniErrors` | M | T01 | Unit test: duplikat, key tidak dikenal, baris rusak, komentar `;`/`#`, round-trip, tulis atomik; error menyebut file + baris |
| **T05** ✅ | **`MachineConfig`** — load `machine.ini` (Environment, lokasi instalasi, `LocalCacheDirectory`), validasi (cache bukan removable, bisa ditulis) | S | T04 | File hilang/rusak → status merah di topbar + pesan; cache drive removable → ditolak dengan pesan perbaikan (D10) |
| **T06** ✅ | **`AllowList`** — load `allowlist.ini` (Registry key, base directory, INI file, service) + `IsAllowed*` | S | T04 | Deny-by-default; test: path di luar base dir, `..`, case-insensitive, `WOW6432Node` |
| **T07** ✅ | **`ComponentStore`** — list/load/save `Components\<id>\<id>-<ver>.ini`, identitas `componentId@releaseVersion`, validasi (R4) | M | T04 | Simpan versi baru tidak menimpa versi lain; import INI ditolak jika `Id` beda; data persisten setelah restart |
| **T08** ✅ | **`ComponentsScreen`** — katalog (cari, filter, paging 4), form dua kolom, tab INI preview read-only, Validate/Save/Import/Save As | M | T03, T07 | Sesuai UI/UX §4.2; pesan validasi satu baris per masalah; muat di 1024×768 tanpa scroll horizontal |
| **T09** ✅ | **`BuildImporter`** — pilih folder, cek ruang disk, salin ke `Cache\Builds\<id>@<ver>\`, hash SHA-256 per file, tulis `build.ini` (metadata), tandai invalid bila hash berubah/file hilang | L | T05, T07 | Ruang tidak cukup → ditolak; import ulang menimpa versi yang sama dengan konfirmasi; `Verify` mendeteksi file diubah/hilang; tidak menghapus apa pun otomatis (D8) |
| **T10** ✅ | **`ProfileStore` + `ProfileValidator`** (R6, R8) — load/save `Profiles\<Id>.ini`, 6 aturan validasi, deteksi dependency melingkar | M | T07 | Test untuk tiap aturan R8 + siklus A→B→A; komponen duplikat ditolak; versi harus ada di katalog |
| **T11** ✅ | **`FeatureResolver`** (R7) — WindWebUI/JnlLog → daftar entri Registry/INI, cek allow-list; JNLLog otomatis mati bila WebUI mati | S | T06, T10 | Entri di luar allow-list ditandai dan membuat Profile tidak valid; hasil identik untuk versi WIND berbeda |
| **T12** ✅ | **`ProfilesScreen`** — katalog, tab Components / Registry & INI / INI preview, chip `DependsOn`, centang fitur | M | T03, T10, T11 | Sesuai UI/UX §4.3; Save nonaktif bila tidak valid; QRISTAP & MDD tampil valid |
| **T13** ✅ | **Data awal** — 5 component, Profile QRISTAP & MDD, `allowlist.ini` awal dari prototype, contoh `machine.ini` Development | S | T04 | Dibuka di aplikasi → kedua Profile valid; dependency MDD sesuai D2 |

**Selesai 1a bila:** R3–R8 lulus acceptance, Profile QRIS TAP & MDD tervalidasi (exit PRD §13), skema INI diberi `SchemaVersion=1.0`.

## Iterasi 1b — Observability (Dashboard, History skeleton)

| ID | Tiket | Ukuran | Bergantung | Acceptance (ringkas) |
|---|---|---|---|---|
| **T14** ✅ | **`MachineInspector`** — nama mesin, versi Windows, daftar service (`ServiceController`) + startup type + executable path, proses (nama, PID, memori, path) — **hanya baca** | M | T05 | Service/proses yang tidak ada → "Not detected"; status service dan proses terpisah (R2); tanpa hak tulis |
| **T15** ✅ | **`ProfileDetector`** (R1) — cocokkan komponen/service di lokasi instalasi dengan Profile tersimpan | M | T10, T14 | 0 atau >1 cocok → "Unknown" + alasan; tidak pernah menampilkan nilai sample |
| **T16** ✅ | **`HistoryStore`** — `Write/GetAll/GetLatest`, satu `History\<execId>.ini` per eksekusi (ADR-005), tulis sekali | S | T04, T05 | Test: urutan nama = urutan waktu; file rusak tidak merusak daftar; tidak ada API ubah/hapus |
| **T17** ✅ | **`DashboardScreen`** — banner recovery (placeholder, aktif di 1c), 4 kartu metrik, ION Applications, tabel Services (4 kolom), tabel Processes, cari, Refresh | M | T03, T14, T15, T16 | Sesuai UI/UX §4.1; 4 kartu + tabel service terlihat tanpa scroll di 1024×768; Refresh membaca ulang mesin |
| **T18** ✅ | **`HistoryScreen` skeleton** — tabel (Time, Component, Build, Outcome), detail di bawah tabel, EmptyState | S | T16 | "No deployments yet" saat kosong; tabel terisi dari file contoh |
| **T19** ✅ | **QA tata letak 1024×768** — checklist UI/UX §9 untuk semua layar (100% dan 125%) | S | T08, T12, T17, T18 | Checklist lulus; temuan dicatat sebagai tiket |

**Selesai 1b bila:** R1–R2 lulus acceptance; engineer dapat membuka PackMan di mesin dev dan melihat kondisi mesin; History kosong tampil benar.

## Urutan kerja yang disarankan

```
T01 → T04 → T05/T06 → T07 → T10 → T11 → T13        (data & aturan, tanpa UI)
 └→ T02 → T03 ─────────→ T08 / T12                  (UI paralel setelah T01)
T09 setelah T05+T07                                  (paling berisiko, mulai awal)
1b: T14 → T15 → T16 → T17 → T18 → T19
```

Dua developer dapat berjalan paralel: **A** = T04–T07, T09–T11, T13 (logika); **B** = T02, T03, T08, T12 (UI). Mereka bertemu di T08/T12.

## Yang masih dibutuhkan sebelum/selama 1a–1b

| # | Kebutuhan | Dipakai di | Sifat |
|---|---|---|---|
| 1 | Salinan nyata `GPFCNFG.ini` & `WebUIUXCNFG.ini` (cek encoding) | T06, T11 | Tidak memblokir 1a; wajib sebelum 1c |
| 2 | Target directory & nama service tiap component (selain WIND) | T13 | Isi sementara dari prototype, ditandai "unverified" |
| 3 | Contoh `machine.ini` dari tim developer ION | T05, T13 | Format dibuat dulu dari PRD §7.2 |
| 4 | Konfirmasi A1–A3 (OS 64-bit, hak admin, Windows 10 IoT **Enterprise**) | T01 | Sebelum rilis 1c |
| 5 | Mesin dev/VM untuk uji | T14, T19 | Diperlukan mulai 1b |

## Di luar iterasi ini (tidak dikerjakan di 1a–1b)

Developer Console, `Deployer`, `BackupStore`, Restore, ProgressDialog (1c); uji kegagalan & hardening (1d); P1-4 "Use in Developer Console" (1c); menu Release (Tahap 2).
