# PRD — ION PackMan (Package Manager) · Tahap 1

| | |
|---|---|
| **Produk** | ION PackMan — tool offline deployment untuk aplikasi ION di mesin ATM/CRM |
| **Fase** | Tahap 1 (MVP Developer/Integration) — **menu Release (Package Builder & Deploy) di luar scope** |
| **Sumber** | Prototype UI `ion_packman_release_devconsole.html` (mock data, simulasi) |
| **Platform target** | Aplikasi desktop .NET, berjalan lokal di mesin Windows 10 IoT (ATM/CRM) |
| **Status** | Draft v5 — diselaraskan dengan [System Design versi sederhana](docs/System-Design-ION-PackMan-Phase1.md), ADR-001…005, dan [UI/UX Spec](docs/UI-UX-Spec-ION-PackMan-Phase1.md) |
| **Tanggal** | 28 Oktober 2026 |

---

## 1. Ringkasan

ION PackMan adalah aplikasi lokal yang membantu engineer melakukan deployment komponen aplikasi ION (ION Middleware, WIND, QRAPTOR, Non-NDC Parser, MDD Service) ke mesin ATM/CRM **tanpa koneksi jaringan**. Tahap 1 fokus pada fondasi: **katalog Component**, **Profile deployment**, **Developer Console** (eksekusi operasi terarah, **khusus mesin environment Development**), **Dashboard** status mesin, dan **History** audit. Menu Release (pembuatan paket rilis dan deployment paket penuh) ditunda ke Tahap 2 dan akan dibangun di atas model data Tahap 1.

## 2. Problem Statement

Deployment aplikasi ION ke mesin ATM/CRM saat ini dilakukan manual di lokasi mesin yang tidak terhubung jaringan: engineer menyalin folder build, menghentikan/menjalankan Windows Service, dan mengubah Registry/INI satu per satu. Proses ini rawan salah path, salah versi, langkah terlewat (mis. service tidak di-stop sebelum file diganti), dan perubahan konfigurasi yang tidak tercatat — tanpa backup otomatis maupun jejak audit. Akibatnya waktu integrasi/testing per iterasi lama, hasil antar-mesin tidak konsisten, dan pemulihan saat gagal bergantung pada keahlian individu.

## 3. Persona

| Persona | Deskripsi | Kebutuhan utama |
|---|---|---|
| **Developer / Integrator ION** (primer) | Membangun komponen (mis. WIND) dan perlu mencoba build terbaru di ATM development berulang kali | Deploy satu komponen cepat, aman, dengan preview |
| **Deployment / Release Engineer** | Memelihara definisi komponen & profile standar per produk/klien | Satu sumber kebenaran untuk path, service, dan Registry/INI |
| **Field / Support Engineer** | Memeriksa kondisi mesin di lapangan | Melihat status service/proses, profile terdeteksi, dan riwayat deployment |
| **QA / Tester** | Memverifikasi build di mesin development (deploy ke mesin test lewat Release, Tahap 2) | Tahu persis versi apa yang terpasang dan kapan |

## 4. Goals

**User goals**
1. **Mempercepat siklus deploy komponen di mesin development** — waktu deploy satu komponen (copy + restart service + config) turun dari proses manual menjadi **≤ 5 menit** end-to-end.
2. **Menghilangkan kesalahan deployment manual** — **0 deployment** dengan target path/service salah yang lolos ke eksekusi (diblokir oleh validasi & preview).
3. **Setiap perubahan dapat dipulihkan** — **100%** operasi copy/replace dan Registry/INI didahului backup terverifikasi; restore otomatis saat gagal.

**Business goals**
4. **Standarisasi konfigurasi** — 100% profile produk aktif (QRIS TAP, MDD) didefinisikan sebagai Profile INI tervalidasi dan versioned, siap dipakai Tahap 2 (Release).
5. **Auditability** — 100% eksekusi tercatat di History lokal (siapa, kapan, apa, hasil) dan dapat diekspor.

## 5. Non-Goals (Tahap 1)

| Non-goal | Alasan |
|---|---|
| **Menu Release: Package Builder & Deploy paket** (manifest, checksums, ZIP/folder release, USB layout, preflight paket penuh) | Ditunda ke Tahap 2 sesuai keputusan; bergantung pada model Component/Profile yang distabilkan di Tahap 1 |
| **Deployment jarak jauh / terhubung jaringan / fleet management** | Produk didesain offline; satu instance mengelola satu mesin lokal |
| **Eksekusi perintah shell arbitrer dari UI** | Risiko keamanan; hanya operasi allow-listed |
| **Rollback migrasi database atau side-effect eksternal** | Backup file tidak bisa membalik efek tersebut; butuh prosedur terpisah |
| **Live resource monitoring (CPU/Memory/Disk real-time berkelanjutan)** | Nice-to-have; snapshot saat refresh cukup untuk Tahap 1 |
| **Manajemen startup type service & pembuatan service baru** | Mengubah startup type butuh approval; di luar alur deploy dasar |
| **Developer Console di environment selain Development** (Test, Production) | Keputusan: Developer Console hanya untuk environment dev; deployment ke mesin lain lewat Release (Tahap 2) |
| **Urutan stop/start service berdasarkan dependency** | Keputusan: belum diperlukan saat ini; `dependsOn` hanya dipakai untuk validasi komposisi Profile |
| **Pengaturan retensi / pembersihan otomatis backup & build** | Keputusan: untuk saat ini semua disimpan; retensi dipertimbangkan nanti (lihat P2) |
| **Konfigurasi Registry/INI per versi komponen** | Keputusan: Registry/INI dideklarasikan di Profile; perbedaan kebutuhan antar versi WIND diterima, tidak dimodelkan per versi |

## 6. Ruang Lingkup Fitur Tahap 1

| Menu | Tujuan | Prioritas |
|---|---|---|
| Home / Dashboard | Status mesin, profile terdeteksi, service & proses ION | P0 (core), P1 (resources) |
| Components | Katalog komponen standalone + metadata per versi rilis + import build lokal | P0 |
| Profiles | Komposisi komponen per produk/klien, dependency, konfigurasi Registry/INI level profile | P0 |
| Developer Console | Eksekusi operasi terarah (copy, service control, apply config) dengan preview | P0 |
| History | Log eksekusi & hasil recovery | P0 (log), P1 (export/filter) |
| ~~Release~~ | ~~Package Builder, Deploy~~ | Tahap 2 |

## 7. Konsep & Model Data

```
Component (standalone, reusable)          Profile (resep deployment per produk/klien)
  componentId@releaseVersion                Id, Version, DeploymentMode=in-place
  ├─ [Installation] TargetDirectory         ├─ [Component:<id>] Version, DependsOn
  ├─ [WindowsService] ServiceName           ├─ [Features] WindWebUI, JnlLog
  └─ [LocalBuild] (metadata build)          ├─ [Registry:<nama>] ...
                                            └─ [Ini:<nama>] ...
Machine (machine.ini) ──(1 profile aktif)──> Profile ──(N referensi versi exact)──> Component@version
```

**Aturan kepemilikan (dari prototype revisi):**
- **Component** memiliki metadata spesifik komponen: target directory, nama Windows Service, build lokal. Dikunci per `componentId + releaseVersion`; versi baru = file INI baru, tidak menimpa versi lain.
- **Profile** memiliki komposisi, relasi dependency, dan **konfigurasi Registry/INI** (dideklarasikan sekali per profile, tidak diduplikasi per komponen).
- **Registry/INI hanya di Profile** (keputusan #1). Konfigurasi ini tidak terikat versi WIND; jika versi WIND berbeda membutuhkan entry berbeda, hal itu diterima dan dikelola dengan memperbarui Profile, bukan Component.
- **Satu mesin = satu Profile aktif.**
- **Deteksi Profile mesin** (keputusan #3) memakai **file konfigurasi mesin** lokal yang mereferensikan lokasi instalasi service ION (mis. base directory `D:\ION`, `D:\APCONTAINER`). PackMan memeriksa komponen/service yang terpasang di lokasi tersebut dan mencocokkannya dengan Profile tersimpan. File yang sama juga menyatakan **environment** mesin (Development / lainnya) (keputusan D7).
- **Penyimpanan data** (keputusan D4 + D10): definisi (Component/Profile INI, allow-list) disimpan di **folder rilis ION Package Manager** (aplikasi portable, boleh di USB). Data operasional (build hasil import, backup, History) disimpan di **local cache** di disk lokal mesin, sehingga backup tetap ada walau USB dicabut.
- **Format konfigurasi = INI** (keputusan D11). Semua file konfigurasi (machine, allow-list, Component, Profile) berformat `.ini` dan menjadi source of truth; dapat di-import/export sebagai file portable (mendukung transfer via USB ke mesin offline). JSON tidak dipakai.

Komposisi Profile **MDD** (keputusan #2):

| Komponen | dependsOn |
|---|---|
| ion-middleware | — |
| wind | ion-middleware |
| non-ndc-parser | ion-middleware |
| mdd-service | ion-middleware, wind, non-ndc-parser |

Contoh konfigurasi Registry/INI profile (fitur WIND WebUI & JNLLog):

| Scope | Target | Entry | Value | Fitur |
|---|---|---|---|---|
| Registry | `HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI` | Support | 1 | windWebUI |
| Registry | `HKLM\...\User\Online_OPL\Welcome\LocalScreen` | DisplayFlag | 1 | windWebUI |
| INI | `D:\APCONTAINER\Config\GPFCNFG.ini` | [WebSocket] Support | 1 | windWebUI |
| INI | `D:\APCONTAINER\Config\WebUIUXCNFG.ini` | [WebUI] Support | 1 | windWebUI |
| INI | `D:\APCONTAINER\Config\WebUIUXCNFG.ini` | [Setting] Support | 1 | windWebUI |
| INI | `D:\APCONTAINER\Config\WebUIUXCNFG.ini` | [Debug] JNLLog | 1 | jnlLog |

### 7.1 Struktur folder (rekomendasi)

```
<folder rilis PackMan>\            ← boleh di USB atau disk lokal
  IonPackMan.exe
  Config\allowlist.ini              ← dikelola developer ION
  Components\<componentId>\<componentId>-<version>.ini
  Profiles\<ProfileId>.ini

D:\ION-PackMan\                     ← per mesin, selalu di disk lokal
  machine.ini                       ← dikelola developer ION
  Cache\                            ← local cache (path dari machine.ini)
    Builds\<componentId>\<version>\  (file build + files.sha256)
    Backups\<execId>\                (files\ + backup.ini)
    History\<execId>.ini             (satu laporan immutable per eksekusi)
```

`execId` = `yyyyMMdd-HHmmss-<componentId>-<4 hex acak>`.

Jika PackMan dijalankan dari disk lokal, folder rilis dan `D:\ION-PackMan\` boleh sama.

### 7.2 Skema file INI

Aturan umum untuk semua file:
- Encoding UTF-8, komentar diawali `;`, nama section & key tidak case-sensitive.
- Setiap file punya `[Meta] SchemaVersion=1.0`.
- Daftar nilai dipisah koma (mis. `DependsOn=ion-middleware,wind`).
- Item berulang ditulis sebagai section bernama: `[Component:<id>]`, `[Registry:<nama>]`, `[Ini:<nama>]`.
- Section/key duplikat, key wajib yang kosong, atau key yang tidak dikenal → error validasi (bukan diabaikan diam-diam).

**`machine.ini`** — identitas mesin, environment, lokasi instalasi (D3, D7, D9)
```ini
[Meta]
SchemaVersion=1.0

[Machine]
MachineName=ATM-DEV-001
Environment=Development        ; Development | Test | Production
Profile=                       ; opsional: isi jika deteksi otomatis ambigu

[InstallLocations]
ION=D:\ION
APCONTAINER=D:\APCONTAINER

[PackMan]
LocalCacheDirectory=D:\ION-PackMan\Cache
```

**`allowlist.ini`** — batas operasi yang diizinkan (D5, D12). Isi awal diambil dari data prototype dan dapat disesuaikan developer per mesin/profile.
```ini
[Meta]
SchemaVersion=1.0

[BaseDirectories]
ION=D:\ION
APCONTAINER=D:\APCONTAINER

[RegistryKeys]
WebUI=HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI
LocalScreen=HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\Online_OPL\Welcome\LocalScreen

[IniFiles]
GPFCNFG=D:\APCONTAINER\Config\GPFCNFG.ini
WebUIUXCNFG=D:\APCONTAINER\Config\WebUIUXCNFG.ini

[Services]
IonMiddleware=IonMiddleware
MddService=MddService
WIND=WIND
```

**`Components\wind\wind-2.1.0.ini`** — definisi component release
```ini
[Meta]
SchemaVersion=1.0

[Component]
Id=wind
DisplayName=WIND
ReleaseVersion=2.1.0
BuildReference=

[Installation]
Mode=in-place
TargetDirectory=D:\APCONTAINER\WIND

[WindowsService]
ServiceName=WIND               ; opsional (kosong/tanpa section jika bukan Windows service)
```
`Id` huruf kecil/angka/`-`; `ReleaseVersion` huruf/angka/`.`/`_`/`+`/`-` (dipakai membentuk nama file dan folder, jadi dibatasi). `DisplayName`, `BuildReference`, `Mode` opsional (Mode default `in-place`).
Metadata build yang diimpor **tidak** disimpan di file ini: folder rilis boleh berada di USB hanya-baca, sehingga metadata (nama folder, jumlah file, hash per file, waktu import) ditulis ke `Cache\Builds\<id>@<versi>\build.ini` (D10, R5).

**`Components\catalog.ini`** — daftar component yang dikenal (R3)
```ini
[Meta]
SchemaVersion=1.0

[Components]
ion-middleware=ION Middleware
wind=WIND
qraptor=QRAPTOR
non-ndc-parser=Non-NDC Parser
mdd-service=MDD Service
```

**`Profiles\QRISTAP.ini`** — komposisi, dependency, Registry/INI
```ini
[Meta]
SchemaVersion=1.0

[Profile]
Id=QRISTAP
DisplayName=QRIS TAP
Version=1.0.0
DeploymentMode=in-place

[Features]
WindWebUI=true
JnlLog=true                    ; hanya berlaku jika WindWebUI=true

[Component:ion-middleware]
Version=1.8.2
DependsOn=

[Component:wind]
Version=2.1.0
DependsOn=ion-middleware

[Component:qraptor]
Version=1.3.1
DependsOn=ion-middleware

[Registry:WebUISupport]
Key=HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI
ValueName=Support
Type=DWORD
Value=1
Feature=WindWebUI

[Ini:WebUIJnlLog]
File=D:\APCONTAINER\Config\WebUIUXCNFG.ini
Section=Debug
Key=JNLLog
Value=1
Feature=JnlLog
```
(Entry Registry/INI lain mengikuti pola yang sama sesuai tabel di atas.)

Daftar file hasil import build beserta hash disimpan di `Cache\Builds\<id>@<versi>\build.ini` (local cache). History disimpan sebagai satu file INI per eksekusi (`Cache\History\<execId>.ini`, ADR-005), bukan file konfigurasi.
Entri `[Registry:<nama>]` dan `[Ini:<nama>]` bertipe `String` atau `DWORD` (nilai DWORD berupa angka 0–4294967295); `Feature` kosong berarti selalu berlaku, selain itu `WindWebUI` atau `JnlLog`.

## 8. User Stories

### Developer / Integrator
1. Sebagai developer, saya ingin men-deploy build terbaru satu komponen (mis. WIND) ke ATM dev tanpa membuat paket rilis, agar saya bisa menguji perubahan dalam hitungan menit.
2. Sebagai developer, saya ingin melihat execution plan (file yang diganti, service yang di-stop/start, entry Registry/INI) sebelum eksekusi, agar saya yakin tidak ada perubahan tak terduga.
3. Sebagai developer, saya ingin file yang diganti otomatis di-backup dan dipulihkan bila eksekusi gagal, agar mesin development tidak tertinggal dalam kondisi rusak.
4. Sebagai developer, saya ingin target directory dan nama service terisi otomatis dari metadata Component, agar saya tidak salah mengetik path.
5. Sebagai developer, saya ingin diblokir ketika komponen yang dipilih bukan bagian dari Profile mesin, agar saya tidak memasang komponen yang tidak kompatibel.

### Deployment / Release Engineer
6. Sebagai release engineer, saya ingin mendaftarkan versi rilis komponen beserta target directory dan service-nya, agar metadata ini dipakai ulang oleh semua profile.
7. Sebagai release engineer, saya ingin menyusun Profile dari versi komponen yang exact beserta dependency-nya, agar kombinasi yang teruji dapat diulang di mesin lain.
8. Sebagai release engineer, saya ingin mengaktifkan fitur konfigurasi (WIND WebUI, JNLLog) di level Profile, agar perubahan Registry/INI terdefinisi sekali dan konsisten.
9. Sebagai release engineer, saya ingin validasi otomatis (dependency hilang, JNLLog tanpa WebUI, WebUI tanpa WIND), agar profile yang rusak tidak bisa disimpan/dipakai.
10. Sebagai release engineer, saya ingin export/import file INI Component & Profile, agar definisi bisa dibawa ke mesin offline via USB dan disimpan di repository.

### Field / Support Engineer & QA
11. Sebagai field engineer, saya ingin melihat nama mesin, versi Windows, profile terdeteksi, dan status service/proses ION di satu layar, agar diagnosis awal cepat.
12. Sebagai QA, saya ingin melihat riwayat eksekusi (komponen, versi, operasi, hasil, operator, waktu), agar saya tahu persis build apa yang terpasang.

### Edge cases
13. Sebagai developer, ketika build yang diimpor sudah tidak tersedia (file dipindah/dihapus), saya ingin diberi tahu dan diminta import ulang, bukan eksekusi gagal di tengah jalan.
14. Sebagai developer, ketika service gagal berhenti dalam batas timeout, saya ingin eksekusi dibatalkan sebelum file disentuh.
15. Sebagai field engineer, ketika profile mesin tidak terdeteksi, saya ingin status "Unknown" yang jelas dan Developer Console mewajibkan pemilihan profile eksplisit.

## 9. Requirements

### 9.1 Must-Have (P0)

#### R1 — Dashboard status mesin
Menampilkan: Machine Name, Detected Profile, versi Windows, Deployment State (hasil deploy terakhir), ION base directory, status profile config, last deployment.
- [ ] Data dibaca dari mesin lokal saat halaman dibuka dan saat **Refresh** ditekan.
- [ ] Detected Profile ditentukan dari file konfigurasi lokasi instalasi: komponen/service yang ditemukan di lokasi tersebut dicocokkan dengan komposisi Profile tersimpan.
- [ ] Given file konfigurasi tidak ada/tidak valid, atau tidak ada Profile yang cocok, atau lebih dari satu Profile cocok, When Dashboard dimuat, Then tampil "Unknown / not detected" beserta alasannya (bukan nilai sample).
- [ ] Dashboard menampilkan **Environment** mesin (Development / lainnya) yang dibaca dari file konfigurasi mesin yang sama.
- [ ] Deployment State & Last deployment diambil dari entri History terakhir.

#### R2 — Tabel ION Services & Related Processes
- [ ] Services: Service name, display name, Component, Status (Running/Stopped/…), Startup Type, Executable Path — hanya service yang dideklarasikan pada Component di Profile aktif (+ yang cocok dengan base directory ION).
- [ ] Processes: nama proses, Component, PID, Status, Memory, Executable Path; proses tidak berjalan tampil "Not detected".
- [ ] Pencarian teks pada kedua tabel.
- [ ] Status service dan status proses ditampilkan terpisah (service bisa Stopped sementara proses masih hidup, dan sebaliknya).

#### R3 — Katalog Component
- [ ] Daftar komponen dengan kolom: nama/ID, versi rilis yang tersimpan, status build lokal, aksi Open.
- [ ] Pencarian (nama, ID, versi), filter (All / Saved releases / Has local build / Not saved), paginasi.
- [ ] Katalog awal: ion-middleware, wind, qraptor, non-ndc-parser, mdd-service.

#### R4 — Definisi Component Release
- [ ] Field: Component, Build/Release version, Build reference (opsional), Target directory, Windows service name.
- [ ] Identitas konfigurasi = `componentId@releaseVersion`; menyimpan versi baru **tidak menimpa** versi lain.
- [ ] Pratinjau INI **read-only** yang selalu mengikuti form (skema §7.2); tidak ada editor teks bebas (UI/UX Spec §4.2).
- [ ] Validate: `componentId` sesuai komponen terpilih, `releaseVersion` wajib, section `installation` wajib.
- [ ] Save, Load version, Import INI (tolak jika `Id` berbeda), Download `{componentId}-{version}.ini`.
- [ ] Data tersimpan **persisten di disk lokal** (bukan memori sesi).

#### R5 — Import build lokal
- [ ] Engineer memilih folder build (via Browse) untuk satu component release.
- [ ] Sistem mencatat metadata: nama folder, jumlah file, path relatif, ukuran, waktu modifikasi, **hash SHA-256 per file**, waktu import.
- [ ] Build disalin ke **local cache** (`LocalCacheDirectory` di `machine.ini`) agar tetap tersedia setelah aplikasi ditutup dan tidak bergantung pada folder sumber/USB.
- [ ] Sebelum import, sistem memeriksa ruang disk kosong di local cache; import ditolak dengan pesan jelas jika tidak cukup.
- [ ] Build yang sudah di-import disimpan terus (tidak dihapus otomatis) (D8).
- [ ] Build yang sudah di-import tersedia di Developer Console.
- [ ] Jika hash berubah/file hilang di staging → build ditandai tidak valid dan wajib import ulang.

#### R6 — Profile: komposisi & dependency
- [ ] Katalog profile (search, filter: All / With components / Empty, paginasi).
- [ ] Field: Profile ID, Display name, Profile version.
- [ ] Tambah/hapus komponen; komponen yang sama tidak bisa ditambah dua kali.
- [ ] Pilih **versi rilis komponen exact** dari versi yang tersimpan di Components.
- [ ] Definisikan `dependsOn` per komponen.
- [ ] New profile, Save, Validate, Download/Import Profile INI (`{ProfileId}.ini`).

#### R7 — Profile: konfigurasi Registry/INI
- [ ] Toggle fitur **Enable WIND WebUI** dan **Enable JNLLog**; tabel entry (Scope, Target, Entry, Value, State Enabled/Not applied) ter-update otomatis.
- [ ] JNLLog hanya bisa aktif jika WIND WebUI aktif (otomatis dinonaktifkan jika WebUI dimatikan).
- [ ] Registry hanya boleh ke key dalam **allow-list** (policy deny-by-default); path arbitrer ditolak. Hal yang sama berlaku untuk base directory yang boleh menjadi target copy dan file INI.
- [ ] Allow-list (Registry key, base directory, file INI, nama service) **ditetapkan dan dikelola oleh tim developer ION** (D5) di `Config\allowlist.ini` (skema §7.2), dikirim bersama rilis PackMan dan tidak dapat diubah dari UI. Isi awal diambil dari data prototype dan dapat disesuaikan per konfigurasi (D12).
- [ ] Entry Registry/INI berlaku untuk Profile, terlepas dari versi WIND yang dipilih.

#### R8 — Validasi Profile
Profile tidak bisa disimpan sebagai "valid" atau dipakai di Developer Console jika:
- [ ] `profileId` kosong atau tidak ada komponen.
- [ ] Ada `dependsOn` ke komponen yang tidak ada di profile.
- [ ] Ada dependency melingkar.
- [ ] JNLLog aktif tanpa WIND WebUI.
- [ ] WIND WebUI aktif tetapi WIND tidak ada di profile.
- [ ] Versi komponen yang direferensikan tidak ada di katalog Components.

Catatan: `dependsOn` dipakai untuk validasi kelengkapan komposisi saja; **tidak** menentukan urutan stop/start service pada Tahap 1.

#### R9 — Developer Console: input
- [ ] **Gating environment:** Given environment mesin (dari file konfigurasi mesin) bukan Development atau tidak terbaca, When pengguna membuka Developer Console, Then halaman tampil read-only dengan pesan "Developer Console hanya tersedia di environment Development" dan tombol Preview/Execute nonaktif. Pengecekan yang sama dilakukan ulang oleh execution service sebelum eksekusi (tidak hanya di UI).
- [ ] Pilih Profile mesin (default = profile terdeteksi); dropdown Component hanya menampilkan komponen dalam profile tersebut.
- [ ] Pilih **Imported build** (hanya dari Components; Developer Console tidak menerima folder sumber langsung).
- [ ] Target directory & service name terisi otomatis dari metadata Component (versi yang direferensikan profile).
- [ ] Operasi (boleh lebih dari satu): **Deploy files** (copy/replace file build), **Apply config** (Registry/INI dari Profile), **Restart service**.

#### R10 — Developer Console: preview execution plan
- [ ] Tombol **Preview** menghasilkan plan: profile, component, build, target, service, daftar operasi berurutan, daftar file yang ditambah/diganti (diff vs target), entry Registry/INI beserta **nilai saat ini → nilai baru**.
- [ ] Plan **diblokir** dengan pesan jelas bila: komponen bukan bagian profile; copy dipilih tanpa build; build tidak tersedia/invalid; target directory kosong atau di luar base directory yang diizinkan; operasi service dipilih tanpa nama service; service tidak ditemukan di mesin.
- [ ] Given **Deploy files** dipilih dan service komponen sedang Running, When plan disusun, Then langkah Stop sebelum copy dan Start sesudahnya ditambahkan otomatis dan ditampilkan di preview.
- [ ] Setiap perubahan input menghapus preview dan menonaktifkan Execute.

#### R11 — Developer Console: eksekusi aman
- [ ] **Execute** hanya aktif setelah preview valid dan input tidak berubah sejak preview; wajib konfirmasi.
- [ ] Urutan baku: preflight → backup (file yang akan diganti + nilai Registry/INI lama) → verifikasi backup → stop service (jika perlu, dengan timeout) → copy → apply config → start service → health check (status service Running).
- [ ] Backup disimpan di **local cache**, per eksekusi (`Cache\Backups\<execId>`), dan **tidak pernah dihapus otomatis** (D8).
- [ ] Sebelum perubahan apa pun, `backup.ini` ditulis dengan `Status=InProgress` dan memuat semua yang akan diubah: file yang diganti (salinan terverifikasi hash), file baru, nilai lama Registry/INI (termasuk "belum ada"), dan status service sebelumnya. Restore selalu mengembalikan seluruh isinya dan aman diulang.
- [ ] Given ada `backup.ini` dengan `Status=InProgress` atau `RestoreIncomplete` (mis. setelah crash atau mati listrik), When PackMan dibuka, Then Developer Console terkunci dan Dashboard menampilkan tombol **Restore**; Restore mengembalikan semua isi backup dan menulis History dengan outcome `RestoredAfterCrash`.
- [ ] Given local cache berada di removable drive, tidak dapat ditulis, atau tidak dikonfigurasi, When pengguna menekan Preview/Execute, Then plan diblokir dengan pesan yang menjelaskan cara memperbaiki `LocalCacheDirectory` (D10).
- [ ] Jika satu langkah gagal: hentikan, **restore** file & Registry/INI dari backup, kembalikan service ke state semula, laporkan hasil restore.
- [ ] File yang tidak ada di build **tidak dihapus** dari target (preserve undeclared files).
- [ ] PackMan adalah **satu aplikasi yang berjalan sebagai admin** (UAC saat dibuka). Guards (environment Development, path di bawah base directory, allow-list, hash build, ruang disk, tidak ada backup tertunda) dicek ulang tepat sebelum eksekusi. Tidak ada perintah shell arbitrer.
- [ ] Hanya satu instance PackMan yang boleh berjalan di satu mesin.
- [ ] Progres per langkah ditampilkan real-time.

#### R12 — History
- [ ] Setiap eksekusi (sukses, gagal, di-restore, dibatalkan) dicatat: timestamp, operator (user Windows), mesin, profile, component, versi build + hash, operasi, hasil per langkah, lokasi backup, hasil recovery.
- [ ] Daftar history urut terbaru, dapat dibuka detailnya.
- [ ] Disimpan sebagai satu file `Cache\History\<execId>.ini` per eksekusi, ditulis sekali dan tidak pernah diubah (append-only; tidak bisa diedit dari UI).

### 9.2 Nice-to-Have (P1)

| ID | Requirement | Acceptance criteria |
|---|---|---|
| P1-1 | System Resources (CPU, Memory, Disk C:) | Snapshot saat refresh; nilai sesuai Task Manager ±5% |
| P1-2 | Tombol **Manage** service di Dashboard (Start/Stop/Restart) | Lewat jalur eksekusi & audit yang sama dengan Developer Console |
| P1-3 | Export History (CSV) & filter (tanggal, komponen, hasil) | File ekspor memuat semua field R12 |
| P1-4 | "Use in Developer Console" dari halaman Component | Membuka Developer Console dengan profile kompatibel, component, target, service terisi |
| P1-5 | Manual rollback dari History ("Restore backup ini") | Hanya untuk eksekusi terakhir per komponen; melalui preview + konfirmasi |
| P1-6 | Validasi INI berbasis definisi skema terversi (`[Meta] SchemaVersion`) | Error menunjukkan file, section, dan key yang salah |
| P1-7 | Deteksi otomatis profile mesin | Aturan deteksi terdokumentasi; hasil tampil di Dashboard |

### 9.3 Future Considerations (P2) — desain jangan menghalangi

- **Release (Tahap 2):** Package Builder (mode integration vs standard release, reuse versi sebelumnya, manifest + checksums.sha256, Folder/ZIP, USB layout PackMan+Profiles+Releases) dan Deploy paket penuh/selektif dengan preflight. → Model `componentId@version`, hash build, dan Profile INI Tahap 1 harus langsung dapat dipakai sebagai input paket.
- **Machine-specific override** di atas profile standar (eksplisit, ter-audit).
- **Konfigurasi aplikasi per komponen** (config file replace/merge, ServiceToggler WIND per versi).
- **Role & approval** (mis. perubahan startup type / Registry butuh approver).
- **Retensi & pembersihan backup/build** (mis. simpan N backup terakhir per komponen) — struktur folder `Backups/` dan `Builds/` per komponen agar mudah dibersihkan nanti.
- **Signing paket/definisi** untuk memastikan asal-usul file yang dibawa via USB.

## 10. Kebutuhan Non-Fungsional

| Area | Kebutuhan |
|---|---|
| **Offline** | Semua fungsi berjalan tanpa jaringan; tidak ada dependency ke layanan cloud/internet |
| **Portable & penyimpanan** | Aplikasi dan definisinya (Components, Profiles, allow-list) berada di folder rilis ION Package Manager (boleh di USB). Build, backup, dan History hanya ditulis ke local cache di disk lokal mesin. Tidak menulis ke lokasi lain selain target deployment yang diizinkan |
| **Format konfigurasi** | Semua konfigurasi berformat INI (UTF-8, skema §7.2); tidak ada file konfigurasi JSON |
| **Kapasitas disk** | Karena tidak ada retensi (D8), ukuran local cache (builds + backups) ditampilkan di Dashboard dan preflight eksekusi menolak jika ruang disk tidak cukup untuk backup + copy |
| **Environment** | Fungsi mutasi (Developer Console) hanya aktif di environment Development; Dashboard, Components, Profiles, History tetap bisa dipakai read/edit definisi |
| **Keamanan** | Allow-list operasi & Registry key; target path harus di bawah base directory yang disetujui; satu aplikasi admin (khusus mesin Development) dengan Guards yang dicek ulang sebelum eksekusi; input path dinormalisasi (cegah path traversal) |
| **Data safety** | Backup sebelum setiap mutasi, terverifikasi; restore otomatis saat gagal; tidak ada penghapusan file yang tidak dideklarasikan |
| **Auditability** | History append-only, menyimpan hash build |
| **Platform** | .NET, Windows 10 IoT (x86/x64 sesuai mesin), resolusi layar mesin dev |
| **Kinerja** | Dashboard tampil < 3 dtk; preview plan < 5 dtk untuk build ≤ 2.000 file |
| **Portabilitas data** | File INI Component & Profile dapat di-export/import tanpa kehilangan data (round-trip: baca → simpan → baca menghasilkan nilai identik) |

## 11. Success Metrics

**Leading (2–4 minggu setelah rollout ke tim developer)**

| Metrik | Target sukses | Stretch | Cara ukur |
|---|---|---|---|
| Adopsi: % deploy di mesin development yang memakai PackMan (vs manual) | ≥ 70% | ≥ 90% | History PackMan vs log/self-report tim |
| Waktu median deploy 1 komponen (preview → selesai) | ≤ 5 menit | ≤ 2 menit | Timestamp History |
| Task success rate eksekusi | ≥ 95% | ≥ 99% | History (sukses / total) |
| Eksekusi gagal yang berhasil di-restore otomatis | 100% | — | History (recovery result) |

**Lagging (1–3 bulan)**

| Metrik | Target |
|---|---|
| Insiden "salah path / salah versi / config tertinggal" di mesin development | turun ≥ 80% vs baseline |
| Profile produk aktif terdefinisi & tervalidasi di PackMan | 100% (QRIS TAP, MDD) |
| Kesiapan Tahap 2: file INI Profile/Component dipakai langsung oleh Package Builder tanpa migrasi | Ya |

> Baseline waktu & insiden manual perlu dikumpulkan sebelum rollout (lihat Open Questions).

## 12. Decision Log & Open Questions

### 12.1 Keputusan (28 Okt 2026)

| # | Topik | Keputusan | Dampak di PRD |
|---|---|---|---|
| D1 | Lokasi konfigurasi Registry/INI | Di **Profile**. Perbedaan kebutuhan antar versi WIND diterima | §7, R7, Non-goals |
| D2 | Dependency Profile MDD | Komposisi: ion-middleware, wind, non-ndc-parser, mdd-service; `mdd-service` depends on ion-middleware, wind, non-ndc-parser. Urutan eksekusi berdasarkan dependency **belum diperlukan** | §7, R8, Non-goals |
| D3 | Deteksi profile mesin | Dari file konfigurasi yang mereferensikan lokasi instalasi service | §7, R1 |
| D4 | Lokasi penyimpanan data | Folder yang sama dengan rilis ION Package Manager | §7, R5, R11, NFR |
| D5 | Pengelola allow-list | Tim developer ION | R7 |
| D6 | Environment Developer Console | Hanya environment Development | §1, R9, NFR, Non-goals |
| D7 | Penanda environment | File konfigurasi lokasi instalasi (D3) yang sama juga menyatakan environment mesin | §7, R1, R9 |
| D8 | Retensi backup & build | Untuk saat ini semua disimpan (keep); belum ada pengaturan retensi | R5, R11, NFR, Non-goals |
| D9 | Skema file konfigurasi mesin | Mengikuti rekomendasi PRD: `D:\ION-PackMan\machine.ini` (§7.2), dibuat & dipelihara developer ION | §7.1–7.2, R1, R9 |
| D10 | PackMan dari USB | Boleh; build, backup, dan History disimpan di **local cache** di disk lokal mesin | §7, R5, R11, NFR |
| D11 | Format konfigurasi | **INI**, bukan JSON, untuk semua file konfigurasi | §7.2, R4, R6, NFR |
| D12 | Isi awal allow-list | Pakai data prototype yang ada; disesuaikan di masing-masing config | §7.2, R7 |

### 12.2 Open Questions tersisa

Tidak ada pertanyaan blocking yang tersisa. Yang tersisa bisa dijawab selama development:

| # | Pertanyaan | Pemilik | Blocking? |
|---|---|---|---|
| 1 | Nilai **Type** Registry yang benar untuk `Support` & `DisplayFlag` (asumsi: DWORD) | Developer ION (tim WIND) | Tidak |
| 2 | Encoding file INI target milik aplikasi (`GPFCNFG.ini`, `WebUIUXCNFG.ini`) — ANSI/UTF-16/UTF-8? PackMan harus mempertahankan encoding asli saat menulis | Developer ION | Tidak |
| 3 | Health check apa yang cukup setelah start service (status Running saja, atau endpoint/log tertentu per komponen)? | Engineering (per komponen) | Tidak |
| 4 | Siapa operator yang tercatat — user Windows lokal cukup, atau perlu login PackMan? | Security | Tidak |
| 5 | Nilai Registry/INI saat **fitur dimatikan**: dikembalikan ke nilai asal, set 0, atau tidak disentuh? | Engineering (tim WIND) | Tidak |
| 6 | Baseline waktu deploy manual & jumlah insiden saat ini untuk pengukuran metrik | Product / Team Lead | Tidak |

## 13. Timeline & Phasing

**Dependensi:** daftar service & path terverifikasi di ATM dev resmi (per komponen); jawaban Open Questions blocking §12.2 #1–#3 (skema file konfigurasi mesin, kebijakan USB, isi allow-list); akses ke mesin development untuk uji eksekusi nyata.

| Iterasi | Isi | Keluaran |
|---|---|---|
| **1a — Fondasi data** | Components (R3–R5), Profiles (R6–R8), parser & validator INI, `machine.ini`, `allowlist.ini`, local cache, import/export INI | Profile QRIS TAP & MDD tervalidasi |
| **1b — Observability** | Dashboard (R1–R2), History skeleton (R12) | Engineer bisa melihat kondisi mesin |
| **1c — Eksekusi** | Local execution service, Developer Console (R9–R11), backup/restore, History lengkap | Deploy 1 komponen nyata di mesin dev dengan auto-restore |
| **1d — Hardening** | Uji kegagalan (service timeout, file terkunci, disk penuh), uji gating environment, P1 terpilih | Rilis internal ke tim developer |
| **Tahap 2** | Menu Release: Package Builder & Deploy | — |

**Exit criteria Tahap 1:** semua P0 lulus acceptance criteria; skenario gagal (stop timeout, copy gagal di tengah, Registry write ditolak) terbukti ter-restore otomatis di mesin dev; skema INI Component/Profile/machine/allow-list dibekukan sebagai `SchemaVersion=1.0` untuk Tahap 2.

## Lampiran A — Temuan dari Prototype

- Prototype adalah **simulasi**: tidak ada copy file, kontrol service, atau penulisan Registry/INI; data dashboard ilustratif; penyimpanan memakai `localStorage` browser. PRD ini mendefinisikan perilaku nyata.
- Ada **dua generasi desain** di file: halaman "Component Configuration" lama (konfigurasi terikat profile, tab Installation/Service/Configuration/Recovery) disembunyikan dan digantikan desain revisi (Component standalone + Profile memiliki komposisi & Registry/INI). PRD mengikuti desain revisi; kebijakan recovery/backup dari desain lama diadopsi sebagai default sistem (R11), bukan field yang diisi per komponen.
- Prototype memakai JSON untuk Component/Profile; produk final memakai **INI** (D11), sehingga editor JSON di prototype menjadi editor INI.
- Inkonsistensi prototype yang sudah diputuskan: lokasi Registry/INI (→ Profile, D1) dan dependency `mdd-service` (→ ion-middleware, wind, non-ndc-parser, D2).
- Prototype menyatakan arah implementasi: aplikasi .NET dengan tombol Browse, dan "local privileged execution service with allow-listed operations, target identity checks, backup, audit logging, and failure recovery".
