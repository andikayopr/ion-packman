# Tiket Implementasi — Iterasi 1d (Hardening & rilis internal)

**Acuan:** PRD Draft v5 (§9 P0/P1, §13 exit criteria Tahap 1), System Design v2 §7 (risiko), Tickets 1a–1c, Manual-Test-1c.
**Tujuan:** menutup celah yang sudah kita ketahui, membuktikan semua P0 lulus acceptance, membekukan skema INI, dan menyiapkan rilis internal ke tim developer.

**Exit criteria Tahap 1 (PRD §13):** semua P0 lulus acceptance · skenario gagal terbukti ter-restore otomatis **di mesin dev** · skema INI Component/Profile/machine/allow-list dibekukan sebagai `SchemaVersion=1.0` untuk Tahap 2.

## Celah yang sudah kita ketahui (sumber tiket)

| # | Celah | Asal | Tiket |
|---|---|---|---|
| 1 | Import build, Preview (hash file), dan Refresh Dashboard berjalan di thread UI; build besar membuat jendela terasa membeku. Dialog progres untuk **Import build** dijanjikan di T30 tetapi **belum dibuat** | T30, T27 | T42 |
| 2 | Pengecualian tak tertangani menutup aplikasi tanpa jejak; bila terjadi saat eksekusi, backup tertinggal tanpa penjelasan | — | T43 |
| 3 | Metadata build belum mencatat ukuran dan waktu modifikasi per file (R5) | T09 | T44 |
| 4 | Nilai INI lama yang dikembalikan bisa berbeda dari aslinya (Win32 membuang tanda kutip/spasi ujung); file INI yang dibuat oleh deploy tersisa kosong setelah restore; folder backup tanpa `backup.ini` (crash saat `Create`) hanya dilaporkan | T20, T25 | T45 |
| 5 | Border merah field bermasalah (UI/UX §5), pill `FailedRestoreIncomplete` belum lebih tegas, pass keyboard/aksesibilitas belum ada; nama mesin di topbar (`machine.ini`) dan Dashboard (hostname) bisa berbeda | T02, T17, T08 | T46 |
| 6 | Skema INI belum dibekukan dan belum ada dokumen rujukannya | PRD §13 | T47 |
| 7 | `allowlist.ini` ikut dibawa di folder rilis (USB): siapa pun yang bisa mengubah USB dapat melebarkan batas. Opsi `--release/--machine` memilih konfigurasi lain. Belum ada tinjauan keamanan menyeluruh | ADR-001 | T41 |
| 8 | Test symlink-file di Guards tidak bisa dijalankan tanpa hak khusus; encoding INI aplikasi asli belum diketahui | T24, T20 | T50 |
| 9 | Belum ada pembuatan paket rilis (publish, struktur folder, versi) dan panduan untuk tim | — | T48, T49 |

## Tiket

### Gerbang A — Verifikasi dan keselamatan (wajib)

| ID | Tiket | Ukuran | Bergantung | Acceptance (ringkas) |
|---|---|---|---|---|
| **T40** | **Audit acceptance PRD R1–R12**: matriks "requirement → layar/kelas → test" untuk setiap butir centang P0; tandai butir yang belum tercakup; perbaiki yang kecil, buat tiket untuk yang besar | M | — | `docs/Acceptance-Matrix-Tahap1.md` dengan status tiap butir (✔ otomatis / ✔ manual / ✖ celah); tidak ada butir P0 yang tanpa status |
| **T41** | **Tinjauan keamanan** `Deployment/`, `Builds/`, `Ini/`, startup, dan batas kepercayaan (allow-list di USB, `backup.ini` di cache, argumen `--release/--machine`, aplikasi berjalan sebagai admin). Dikerjakan **peninjau independen** (subagen yang tidak ikut menulis kode) | M | — | Daftar temuan berperingkat; temuan tinggi/sedang diperbaiki dengan test; risiko sisa dicatat di System Design §7 |
| **T42** | **UI tidak pernah membeku**: Refresh Dashboard, Preview plan (hash/diff), dan Import build berjalan di background dengan progres; Import build memakai dialog progres (pola `ProgressDialog`) | M–L | T40 | Build 400 file × 1 MB: jendela tetap responsif, progres bergerak, tombol terkait nonaktif selama jalan; tidak bisa memulai dua operasi sekaligus |
| **T43** | **Penanganan crash**: handler pengecualian global → dialog ramah + `crash-<waktu>.txt` (di cache, atau di samping `.exe` bila cache tak tersedia); bila ada backup tertunda tampilkan "Buka PackMan lagi, lalu Restore dari Dashboard" | S–M | — | Pengecualian yang disuntikkan di layar menghasilkan dialog dan file laporan; aplikasi menutup dengan rapi; tidak ada pengecualian yang ditelan diam-diam |
| **T44** | **Metadata build lengkap (R5)**: ukuran dan waktu modifikasi per file; build tidak valid ditandai di tabel Components dan "wajib impor ulang" | S | — | `build.ini`/`files.sha256` memuat ukuran+mtime; build yang filenya berubah tampil "Invalid — re-import" di Components dan memblokir Preview dengan pesan itu |
| **T45** | **Kesetiaan restore**: Restore mengembalikan teks nilai INI **persis** seperti asli (baca baris mentah, bukan hasil `GetPrivateProfileString`); file INI yang dibuat oleh deploy dihapus saat restore bila kosong dan memang dibuat oleh eksekusi itu; folder backup yatim (tanpa `backup.ini`) bisa dibuang lewat tombol "Discard" di Dashboard setelah konfirmasi | M | T41 | Test: nilai berkutip/berspasi kembali identik byte-per-byte; file INI baru hilang setelah restore; folder yatim terdeteksi dan dapat dibuang tanpa menyentuh backup lain |
| **T46** | **Poles UI**: border merah field bermasalah, pill `FailedRestoreIncomplete` lebih tegas, urutan Tab/Enter/Esc dan fokus terlihat, `AutomationProperties.Name` pada tombol ikon, nama mesin konsisten (topbar dan Dashboard memakai sumber yang sama dan menandai bila `machine.ini` berbeda dari hostname) | S–M | — | Checklist UI/UX §5 terpenuhi dan tercakup test; `LayoutQaTests` tetap hijau |
| **T47** | **Bekukan skema INI 1.0** + dokumen rujukan `docs/INI-Schema-1.0.md` (machine, allowlist, catalog, component, profile, build, backup, history) + **golden-file test**: perubahan field apa pun gagal test sampai `SchemaVersion` dinaikkan sadar | S–M | T44, T45 | Satu file contoh valid per jenis disimpan di `tests/`; test memuat, menyimpan ulang, dan membandingkan; dokumen memuat key wajib/opsional dan nilai yang diizinkan |

### Gerbang B — Rilis internal

| ID | Tiket | Ukuran | Bergantung | Acceptance (ringkas) |
|---|---|---|---|---|
| **T48** | **Paket rilis**: `tools/Publish-PackMan.ps1` menghasilkan folder rilis (exe self-contained, `Components/`, `Profiles/`, `Config/`, README singkat, versi), ZIP, dan `SHA256SUMS`; versi tampil di sidebar dan dialog About | S–M | T47 | Satu perintah menghasilkan paket; paket dibuka di folder bersih dan jalan tanpa .NET terpasang; versi di exe = versi di nama ZIP |
| **T49** | **Panduan tim**: `README.md` repo (cara build/test/publish, peta kode), panduan pemeliharaan `allowlist.ini`/component/profile/`machine.ini`, dan pedoman code review (aturan "hanya `Deployment/` yang mengubah mesin") | S | T47 | Developer baru dapat build, menjalankan test, dan menambah component tanpa bertanya |
| **T50** | **Uji lapangan di ATM dev** (dijalankan **Anda**): jalankan `Manual-Test-1c.md` di VM/ATM dev; isi data component nyata (qraptor, non-ndc-parser); cek encoding `GPFCNFG.ini`/`WebUIUXCNFG.ini`; jalankan test symlink-file di mesin dengan hak membuat symlink | M | T48 | Tabel hasil terisi; temuan menjadi tiket; **exit criteria PRD §13 terpenuhi** |

### Pilihan (P1) — rekomendasi

| ID | Tiket | Ukuran | Rekomendasi |
|---|---|---|---|
| **T51** | **Export History ke CSV + filter** (P1-3: tanggal, component, hasil) | S–M | **Ikut**: hanya membaca, bernilai untuk audit, risiko rendah |
| T52 | Tombol Manage service di Dashboard (P1-2) | M | Tunda: mengubah mesin; ulas setelah umpan balik lapangan |
| T53 | Rollback manual dari History (P1-5) | M | Tunda: jalur mengubah mesin kedua; butuh desain preview+konfirmasi tersendiri |
| T54 | System Resources CPU/Memory/Disk (P1-1) | S | Tunda: di luar tujuan Tahap 1 |

## Urutan dan paralelisasi

```
Awal (paralel):       T40 (audit)     T41 (tinjauan keamanan, subagen independen, hanya membaca)
Sesudah T40/T41:      T42   T43   T44   T45   T46          ← file berbeda; T42/T46 menyentuh layar yang sama, kerjakan berurutan
Sesudah T44/T45:      T47 → T48 → T49
Anda:                 T50 (uji lapangan; bisa mulai begitu T48 ada, dan sebagian sudah bisa pakai build sekarang)
Opsional:             T51
```

Aturan koordinasi agen paralel (dari pengalaman 1a–1c): folder kerja **unik per agen** (PowerShell untuk `robocopy`), file baru disalin dengan `cp -n`, file bersama diubah **hanya oleh orkestrator**, dan hasil agen **selalu diverifikasi ulang** (build, seluruh test, bacaan kode untuk bagian yang menghapus/menimpa).

## Risiko yang tidak bisa ditutup oleh kode (perlu keputusan)

| Risiko | Penjelasan | Usulan |
|---|---|---|
| **Allow-list ikut di folder rilis** | `allowlist.ini` dibawa bersama rilis (D5/D12). Siapa pun yang dapat mengubah USB dapat melebarkan batas, lalu menjalankan PackMan (yang berjalan sebagai admin) | Tahap 1: dokumentasikan sebagai risiko sisa (mesin Development saja, operator = developer). Tahap 2: tanda tangan paket/definisi (sudah ada di daftar P2) |
| **.NET 8 berakhir dukungan 10 Nov 2026** | Runtime ikut dibawa sehingga tidak berubah sendiri, tetapi tidak ada patch keamanan baru | Naikkan ke .NET 10 LTS sebelum rilis produksi (cukup ganti `TargetFramework`); jadwalkan setelah T50 |
| **Uji di mesin sungguhan belum pernah dilakukan** | Semua bukti sejauh ini dari mesin palsu | T50 adalah syarat exit criteria; jangan rilis ke tim sebelum T4, T7, T8 di `Manual-Test-1c.md` lulus |
