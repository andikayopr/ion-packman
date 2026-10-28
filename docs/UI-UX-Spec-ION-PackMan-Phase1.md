# UI/UX Spec — ION PackMan Phase 1

**Status:** Draft v1 (28 Okt 2026)
**Acuan:** prototype `ion_packman_release_devconsole.html`, PRD Draft v5, System Design v2 (WPF code-behind, ADR-002)
**Prinsip:** tampilan dan alur kerja **sama dengan prototype** supaya pengguna tidak perlu belajar ulang. Perubahan hanya di tempat prototype tidak cocok dengan keputusan PRD (INI, tanpa Release, eksekusi nyata).

---

## 1. Pengguna dan konteks pakai

| Pengguna | Menu utama | Yang dibutuhkan dari UI |
|---|---|---|
| Developer ION | Components, Profiles, Developer Console, History | Mengisi data dengan form, melihat apa yang akan berubah **sebelum** menekan Execute |
| Engineer/operator | Dashboard, History | Melihat kondisi mesin, hasil terakhir, dan tombol Restore bila ada yang gagal |

- Layar: monitor ATM/CRM **1024×768** (rasio 4:3), mouse dan keyboard (bukan touch). **Semua layar didesain untuk ukuran ini lebih dulu**; layar lebih besar hanya mendapat ruang kosong tambahan, bukan layout lain. Tidak ada layout mobile; breakpoint prototype **tidak dibawa**.
- **Ruang nyata:** jendela dimaksimalkan (taskbar ±40px, title bar ±30px) → area klien sekitar **1024×700**. Setelah sidebar dan padding, area konten ≈ **810×620**. Semua ukuran di bawah dihitung dari angka ini.
- **Aturan utama 1024×768:** (1) tidak ada scroll horizontal, (2) sidebar dan topbar selalu terlihat, hanya area konten yang boleh scroll vertikal, (3) tombol aksi utama tidak boleh tersembunyi di bawah lipatan, (4) satu kolom lebih disukai daripada dua kolom sempit.
- Window: `MinWidth=800`, `MinHeight=540`, `WindowState=Maximized` (minimum sengaja lebih kecil dari 1024×700 agar tetap muat pada skala 125%, ≈ 820×560 DIP; lihat aturan sidebar-ciut di §2). Desain dan uji tetap pada 1024×768 skala 100%.
- Bahasa antarmuka: **Inggris** (sama dengan prototype dan istilah teknis ION). Dokumen ini Indonesia.

## 2. Kerangka layar (tetap seperti prototype)

```
┌─────────┬────────────────────────────────────────────────┐
│ ION     │ PackMan / <halaman>        ● ATM-DEV-001 · Development │ ← topbar 52px
│ PackMan ├────────────────────────────────────────────────┤
│         │ Judul halaman (22px)                  [aksi]   │
│ ▦ Dash  │ ┌──────── card ────────────────────────────┐   │ ← area konten
│ ⚙ Comp  │ │                                          │   │   scroll vertikal
│ ▦ Prof  │ └──────────────────────────────────────────┘   │   saja
│ ⌘ Dev   ├────────────────────────────────────────────────┤
│ ◷ Hist  │ [action bar lengket — hanya Developer Console] │ ← 56px
│ v1.0.0  │                                                │
└─────────┴────────────────────────────────────────────────┘
 sidebar 184px  |  konten ≈ 810px lebar (padding 16)
```

- **Sidebar 184px** (dari 226): logo "ION" 32px + nama, 5 menu, tinggi tombol menu 38. **Menu Release dihapus** (Phase 2). Menu aktif: latar biru muda, teks biru, tebal. Label "WORKSPACE" dihapus (hemat ruang).
- **Sidebar ciut otomatis** ke 56px (hanya ikon + tooltip) bila lebar jendela < 900 DIP (mis. skala DPI 125%).
- **Topbar 52px** (dari 68): breadcrumb di kiri; di kanan titik status + nama mesin + `Environment` (dari `machine.ini`). Titik hijau = siap, kuning = ada backup yang belum selesai, merah = `machine.ini` tidak valid.
- **Footer sidebar:** hanya versi PackMan (lokasi release folder dipindah ke Dashboard).
- **Konten:** padding 16, **page head** ringkas: judul 22px + deskripsi satu baris abu-abu 12px + tombol aksi di kanan. Jarak antar-card 12 (dari 16–17).
- **Menu Developer Console** disembunyikan/dinonaktifkan jika `Environment` bukan `Development`, dengan tooltip "Only available on Development machines".

## 3. Gaya visual (token)

Dipindahkan apa adanya dari CSS prototype ke satu `Resources/Theme.xaml` (satu tempat, dipakai semua layar).

| Token | Nilai | Pemakaian |
|---|---|---|
| `Bg` | `#F5F7FB` | latar halaman |
| `Panel` | `#FFFFFF` | card, sidebar, topbar |
| `Line` | `#E2E8F0` | border, pemisah tabel |
| `Text` / `Muted` | `#172033` / `#64748B` | teks utama / sekunder |
| `Blue` / `BlueSoft` | `#2563EB` / `#EFF6FF` | tombol primary, menu aktif, fokus |
| `Green` `Amber` `Red` | `#15803D` `#B45309` `#B91C1C` | status |
| Font | Segoe UI **13 px** dasar (tabel/pill 12, **minimum 12**); judul halaman 22; judul card 14 | dipadatkan untuk 1024×768 |
| Ukuran kontrol | tombol & input tinggi **32**, baris tabel **34**, padding card **14** | target klik ≥ 32 |
| Radius | card 12, tombol/input 8, pill 999 | |

**Komponen yang dibuat sekali (style/UserControl), dipakai ulang:**

| Komponen | Isi | Catatan |
|---|---|---|
| `Card` | border + radius 12 + judul | |
| `StatusPill` | bulat kecil: hijau (Running/Succeeded), kuning (Warning), merah (Failed/Rejected), biru (info), abu (Stopped) | **selalu teks + warna** (jangan hanya warna) |
| `Notice` | kotak kuning peringatan; `SuccessBox` hijau; `ErrorBox` merah | pesan validasi dan hasil |
| `PrimaryButton` / `Button` | biru solid / outline | `IsEnabled=false` → opacity 45% |
| `KeyValueRow` | label abu + nilai | detail mesin, ringkasan |
| `DataTable` | `DataGrid` read-only, header abu muda, tinggi baris 40 | cari + paging 5/halaman seperti prototype |
| `EmptyState` | ikon besar + judul + satu kalimat | tabel/list kosong |
| `Toast` | kotak gelap pojok kanan bawah, hilang 3 detik | konfirmasi singkat |

## 4. Layar per menu

### 4.1 Dashboard
Sama dengan prototype, tetapi **data nyata** (R1–R3):

Urutan dari atas ke bawah (hal terpenting terlihat tanpa scroll di 1024×768):

1. **Banner recovery** (hanya bila ada backup belum selesai, tinggi 44): merah *"Last execution on <waktu> was interrupted. Restore before continuing."* + tombol **Restore**. Selama tampil, Developer Console terkunci.
2. **4 kartu metrik dalam satu baris** (masing-masing ≈ 195px, tinggi 72, tanpa ikon): Machine Name · Detected Profile (pill kuning "Not detected" bila tidak cocok) · Windows · Deployment State (hasil History terakhir, atau "Needs recovery" merah). Nilai 16px, label 11–12px.
3. **Card ION Applications** satu baris (3 `KeyValueRow` mendatar): Base directory · Profile config (valid / error pertama) · Last deployment. Menggantikan card dua-kolom prototype.
4. **Tabel ION Services** (kotak cari + Refresh di kepala card): kolom **Service, Component, Status, Startup Type**. Kolom *Executable Path* **tidak ditampilkan di tabel** (lebar 810px tidak cukup tanpa scroll horizontal); path tampil sebagai tooltip dan pada panel detail saat baris dipilih. Kolom Action "Manage" prototype dihapus (pengelolaan lewat Developer Console).
5. **Tabel Related Processes:** kolom **Process, Component, PID, Status, Memory**; path juga lewat tooltip.

- Tabel yang panjang scroll di dalam halaman; tinggi tabel tidak dipaksa, header kolom tetap.
- **System Resources (CPU/Memory/Disk)** di prototype hanya ilustrasi → **dikeluarkan dari Phase 1** (di luar PRD).

### 4.2 Components (R4–R5)
Mengikuti prototype (katalog + form), tetapi **editor JSON diganti form + pratinjau INI**.

Satu kolom, dua bagian bertumpuk; pratinjau INI di tab agar tidak memakan lebar.

```
Components                          [Validate] [Save Component]
┌ Component catalog ─────────────────────────────────────────┐
│ [Search…………]  [Filter ▾]                                   │
│ Component | Saved versions | Local build | [Open]           │ 4 baris/halaman
│ ‹ Prev   Page 1 of 2 · 5 components   Next ›               │
└─────────────────────────────────────────────────────────────┘
┌ Component release ─────────────────────────────────────────┐
│ [ Form ]  [ INI preview ]            ← tab                  │
│ Component ▾            Release version                      │ dua kolom
│ Build reference        Windows service name                 │ field (≈390px)
│ Target directory (lebar penuh)                              │
│ [Import local build…] [Use in Developer Console]            │
└─────────────────────────────────────────────────────────────┘
```

- Baris katalog dibatasi **4 per halaman** (dari 5) agar form tetap terlihat tanpa scroll panjang.
- **Import local build…** membuka dialog pilih folder (menggantikan upload folder browser). Setelah berhasil: "Imported: <nama folder> · N files" di bawah tombol, dan build muncul di kolom *Local build* katalog.
- **Tab INI preview** read-only (monospace 12px, tinggi 300 lalu scroll). Edit lewat form saja, sehingga tidak ada editor teks bebas yang bisa menghasilkan INI tidak valid.
- Validasi tampil di `Notice`/`ErrorBox` di bawah form, satu baris per masalah dengan nomor baris untuk file yang dibaca.
- **Hapus** dari prototype: Download/Import JSON, tab JSON Editor, tab Installation/Service/Configuration/Recovery (sudah tidak ada di desain; di prototype pun disembunyikan).
- **Use in Developer Console:** pindah ke Developer Console dengan Component, build, target directory, dan service terisi (perilaku sama dengan prototype).

### 4.3 Profiles (R6–R7)
Mengikuti prototype: katalog profile, definisi profile, **composition**, fitur WindWebUI/JnlLog, tabel Registry/INI hasil resolve.

- **Layout 1024×768:** katalog profile di bagian atas (4 baris/halaman), lalu card *Profile definition* dengan **tab**: `Components` · `Registry & INI` · `INI preview`. Tidak ada tiga kartu berdampingan.
- Form (tab Components): Profile ID, Display name (satu baris, dua kolom), Add component (pilih component + versi). Setiap baris component menampilkan `DependsOn` sebagai chip; dependency yang belum ada ditandai merah.
- Kotak centang **Enable WIND WebUI** dan **Enable JNLLog**; JNLLog **nonaktif** (abu) bila WebUI mati, dengan keterangan "Requires WIND WebUI" (di prototype hanya dipaksa diam-diam).
- Tabel **Registry & INI** (hasil resolve dari fitur): Type, Path/File, Key, Value, status allow-list (✔ / ✖). Entri yang tidak ada di allow-list tampil merah dan membuat **Save** nonaktif.
- Pratinjau `Profiles\<Id>.ini` read-only di kolom kanan.

### 4.4 Developer Console (R8–R11) — layar terpenting
Dua kolom seperti prototype (masing-masing ≈ 395px), dengan **tiga operasi** (bukan lima) dan satu alur terkunci: **pilih → Preview → Execute**. Tombol Preview/Execute berada di **action bar lengket di bawah jendela** (selalu terlihat tanpa scroll); Execution Preview tampil di bawah form dan di-scroll bila panjang.

```
Developer Console                              [Direct mode]
[Notice 1 baris: Executes on THIS machine. Backup is created first.]

┌ 1. Target & source ──────────┐ ┌ 2. Operations ────────────────┐
│ Profile (terdeteksi)         │ │ ☑ Deploy files                │
│ Component ▾                  │ │   (service Running → stop/start otomatis) │
│ Imported build ▾             │ │ ☐ Apply config (Registry/INI) │
│ Target directory (readonly)  │ │ ☐ Restart service             │
│ Service name (readonly)      │ │                               │
└──────────────────────────────┘ └───────────────────────────────┘
┌ Execution Preview ─────────────────────────────────────────────┐
│ Files: 12 replace, 3 new   Backup: D:\ION-PackMan\Cache\...    │
│ Service: WIND Running → stop → start                           │
│ Config: Registry …  / INI …  (nilai lama → baru)               │
│ Guards: ✔ Environment  ✔ Path in base dir  ✔ Allow-list        │
└────────────────────────────────────────────────────────────────┘
══ action bar lengket ═══════════════════════════════════════════
 Preview required        [Preview plan]  [Execute]
```

Penyesuaian 1024×768:
- Kartu 1 dan 2 tingginya ≈ 230px, jadi keduanya + notice muat di atas lipatan (≈ 300px) dan **area Preview mendapat sisa ≈ 250px** sebelum scroll.
- Baris Execution Preview dipadatkan: satu baris ringkasan per jenis (Files / Service / Config / Guards); daftar file lengkap berada di `Expander` "Show file list" (tertutup default, tinggi maksimum 160 lalu scroll).
- Path panjang (Target directory, Backup) dipotong di tengah (`D:\APCONTAINER\…\WIND`) dengan tooltip penuh; tidak ada scroll horizontal.
- Teks status di action bar menjelaskan kenapa Execute nonaktif: *"Preview required"*, *"Guard failed: …"*, atau *"Recovery needed"*.

Aturan interaksi:
1. **Execute nonaktif** sampai Preview berhasil. **Mengubah input apa pun menghapus preview** dan menonaktifkan Execute lagi (R10).
2. Target directory dan service name **read-only**, berasal dari Component (satu sumber). Prototype membolehkan ketik bebas; itu dihapus karena bertentangan dengan Guards.
3. Preview menampilkan **diff** yang bisa dibaca: jumlah file diganti/baru, nilai Registry/INI lama → baru, rencana service. Guard yang gagal tampil merah dan menjelaskan alasannya; Execute tetap nonaktif.
4. **Execute** meminta konfirmasi dialog: *"Execute on ATM-DEV-001 (Development)? Backup will be created first."* [Cancel] [Execute].
5. Selama berjalan: dialog progres modal (**480×320**, di tengah jendela) dengan langkah (Backup → Stop service → Copy files → Apply config → Start service) dan tanda ✔ per langkah. **Tidak bisa ditutup**, tombol Close muncul setelah selesai. Jendela utama tidak boleh dimatikan lewat X saat berjalan (konfirmasi + peringatan).
6. Hasil tampil di card Execution Preview: `SuccessBox` hijau, atau `ErrorBox` merah dengan pesan, **hasil restore otomatis** (`FailedRestored` / `FailedRestoreIncomplete`), dan tautan "Open backup folder".
7. Jika `Environment` bukan Development: halaman menampilkan `EmptyState` terkunci, tidak ada form.

### 4.5 History (R12)
- Tabel: **Time, Component, Build, Outcome (pill)** (Operations dan Operator dipindah ke panel detail agar muat 810px). Urut terbaru di atas, kotak cari, paging 8 baris.
- Klik baris → **panel detail di bawah tabel** (bukan di kanan; lebar tidak cukup untuk dua kolom): KeyValueRow dari file `History\<execId>.ini` dalam dua kolom, pesan error, lokasi backup, tombol **Open backup folder**.
- Pill outcome: `Succeeded` hijau · `Rejected` kuning · `FailedRestored` merah · `FailedRestoreIncomplete` merah tebal · `RestoredAfterCrash` biru.
- Kosong: `EmptyState` "No deployments yet".

## 5. Pola umum

| Situasi | Perilaku |
|---|---|
| Validasi gagal | `Notice` di atas form; field bermasalah diberi border merah; daftar masalah satu baris per item |
| Simpan berhasil | `Toast` + `SuccessBox` singkat di card terkait |
| File INI tidak valid | halaman tetap terbuka, tabel menampilkan baris error (nama file + nomor baris); tidak crash |
| Aksi yang mengubah mesin | selalu didahului Preview dan dialog konfirmasi |
| Aksi lama (>1 detik) | kursor *wait* + tombol nonaktif; >3 detik: dialog progres |
| Keyboard | Tab berurutan, Enter pada tombol default, Esc menutup dialog, fokus terlihat (outline biru `#BFDBFE`) |
| Teks error | bahasa sederhana: apa yang salah + apa yang harus dilakukan |
| Teks panjang (path, nama build) | dipotong di tengah/akhir + tooltip penuh; tidak membuat scroll horizontal |
| Dialog | maksimum 640×480, di tengah jendela, tidak melebihi area 1024×700 |
| Aksesibilitas dasar | status selalu teks + warna; kontras sesuai token; `AutomationProperties.Name` pada tombol ikon |

## 6. Perbedaan dari prototype (ringkas)

| Prototype | Phase 1 | Alasan |
|---|---|---|
| Menu Release | dihapus | Phase 2 |
| Editor JSON, Download/Import JSON | form + pratinjau INI read-only | keputusan D11 (INI) dan cegah file tidak valid |
| Developer Console: Copy, Stop, Start, Restart (checkbox) | Deploy files, Apply config, Restart service | ADR/System Design v2 |
| Target dir & service name bisa diketik | read-only dari Component | Guards (path harus di base directory) |
| System Resources (angka ilustrasi) | dihapus | di luar PRD |
| Kolom Action "Manage" nonaktif | dihapus | di luar Phase 1 |
| Simulasi | eksekusi nyata + konfirmasi + progres | R9–R11 |
| Sidebar 226 / topbar 68 / font 14 / grid dua-tiga kolom | sidebar 184 / topbar 52 / font 13 / kolom dikurangi, tab untuk bagian sekunder | dioptimalkan untuk monitor 1024×768 |
| Responsif mobile | tidak ada | aplikasi desktop, target 1024×768 |
| Tidak ada banner recovery | ada | R11 restore setelah crash |

## 7. Implementasi WPF (mengikuti ADR-002)

```
Screens/
  MainWindow.xaml(.cs)        sidebar + topbar + ContentControl
  DashboardScreen.xaml(.cs)
  ComponentsScreen.xaml(.cs)
  ProfilesScreen.xaml(.cs)
  DevConsoleScreen.xaml(.cs)
  HistoryScreen.xaml(.cs)
  ProgressDialog.xaml(.cs)
Resources/
  Theme.xaml                  warna, font, style tombol/card/pill/tabel (satu tempat)
  Controls/                   Card, StatusPill, Notice, KeyValueRow, EmptyState
```

- Navigasi: `MainWindow` menukar `UserControl` di satu `ContentControl`; tombol menu memakai style `NavButton` (Aktif = trigger).
- Code-behind hanya memanggil class fitur dan mengisi kontrol (aturan System Design §2.3 tetap berlaku).
- `Execute` berjalan di background (`Task.Run`) agar UI tidak membeku; progres dikirim lewat `IProgress<string>` ke `ProgressDialog`.

## 8. Urutan pembuatan UI (selaras iterasi PRD)

| Iterasi | Layar |
|---|---|
| 1a | `Theme.xaml`, `MainWindow` (sidebar, topbar), Components, Profiles (form, validasi, pratinjau INI) |
| 1b | Dashboard (data mesin, service, process), History skeleton |
| 1c | Developer Console, ProgressDialog, banner Restore |
| 1d | History, poles (empty state, pesan error, keyboard) |

## 9. Cara uji tata letak (wajib sebelum iterasi dinyatakan selesai)

Setiap layar diperiksa pada **1024×768** (monitor nyata atau jendela 1024×700 di VM), skala DPI 100% lalu 125%:
1. Tidak ada scroll horizontal di layar mana pun.
2. Sidebar dan topbar tidak ikut scroll; sidebar ciut ke ikon pada 125%.
3. Dashboard: 4 kartu metrik dan tabel service terlihat tanpa scroll.
4. Developer Console: tombol Preview/Execute terlihat tanpa scroll; Execution Preview dapat dibaca.
5. Semua teks ≥ 12px dan tidak terpotong tanpa tooltip; dialog muat di jendela.
6. Isi dengan data terpanjang (path 100+ karakter, 30 component) untuk memastikan pemotongan dan scroll bekerja.

## 10. Pertanyaan terbuka untuk UI

1. ~~Apakah monitor ATM dev memang 1024×768 pada skala DPI 100%?~~ **Terjawab (28 Okt 2026): ya, 1024×768, skala 100%, jendela maximize.**
2. Apakah label menu/tombol tetap Inggris seperti prototype?
3. Apakah perlu logo/warna korporat selain biru prototype?
