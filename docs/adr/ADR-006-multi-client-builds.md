# ADR-006: Multi-client di mesin dev — build diberi tag Client

**Status:** Proposed
**Date:** 2026-10-29
**Deciders:** Tech lead
**Scope:** Developer Console di environment Development. Release (Tahap 2) di luar cakupan.

## Context

Kode ION belum memakai feature flag. Kebutuhan khusus bank dikerjakan di **branch git per bank (Client)**, dan hanya sebagian component yang bercabang. Contoh saat ini:

- **BCA** — pengembangan QRISTAP: `qraptor` dan `wind` memakai branch BCA; `ion-middleware` tetap default.
- **BRI** — offline auth: perubahan di branch BRI component yang sudah ada (mis. `ion-middleware`). Jika offline auth berupa service baru, ia cukup didaftarkan sebagai component dan profile biasa.

Di mesin dev, developer meng-import build dari folder output branch yang sedang dikerjakan, lalu men-deploy **satu component per eksekusi** lewat Developer Console (R9–R11). Model sekarang tidak tahu build berasal dari branch Client mana, sehingga:

1. Build `wind` BCA dan `wind` default saling menimpa di cache (`Builds\<id>@<versi>`).
2. Mesin bisa tercampur tanpa disadari, mis. `wind` BCA berjalan bersama `qraptor` default padahal BCA punya branch `qraptor` sendiri.
3. History tidak bisa menjawab "branch/commit mana yang terpasang" saat menelusuri bug.

Data release component (`TargetDirectory`, `ServiceName`) sama untuk semua Client; yang berbeda hanya binary, yaitu **build**.

Istilah yang dipakai: **Client** (bukan tenant). **Default** = build dari branch utama, berlaku untuk semua Client.

## Decision

Client menjadi **atribut build hasil import**, bukan atribut release component. Model release component dan aturan "satu component satu release" tidak berubah.

### 1. Daftar Client dan override — `Data\Config\clients.ini`

Dikelola developer ION dan dapat dilihat serta diubah dari layar **Clients** (hanya di mesin Development). Penulisan memakai aturan validasi yang sama dengan pembacaan, dan perubahan langsung dipakai semua layar tanpa restart. File tetap bisa diedit manual.

```ini
[Meta]
SchemaVersion=1.0

[Clients]                 ; Id=Nama tampilan. Id mengikuti pola component id.
BCA=Bank BCA
BRI=Bank BRI

[Overrides]               ; Client=component yang memakai branch Client tersebut
BCA=wind,qraptor
BRI=ion-middleware
```

Validasi: setiap component di `[Overrides]` harus ada di `catalog.ini`; setiap Client di `[Overrides]` harus ada di `[Clients]`.

### 2. Client aktif mesin — `machine.ini`

```ini
[Machine]
Environment=Development
Profile=QRISTAP
Client=BCA                ; opsional; kosong/tidak ada = default
```

Dipilih di layar **Clients** (atau Dashboard) dan disimpan ke `machine.ini`, atau diisi manual seperti `Profile=` (D9). Client yang tidak ada di `clients.ini` membuat Developer Console terkunci dengan pesan yang jelas.

### 3. Import build diberi tag Client

- Pilihan Client saat import: **Default**, atau Client yang meng-override component tersebut. Kombinasi lain ditolak.
- PackMan **menyarankan** Client dari branch git folder sumber (`.git\HEAD` di folder induk); developer tetap mengonfirmasi.
- Lokasi cache: `Builds\<id>@<versi>` untuk default, `Builds\<id>@<versi>@<CLIENT>` untuk Client. `@` sudah ditolak oleh pola id/versi, jadi tidak bisa bentrok.
- `build.ini` bertambah field:

```ini
[Build]
ComponentId=qraptor
ReleaseVersion=1.4.0
Client=BCA                ; kosong = default
GitBranch=feature/bca-qristap   ; kosong bila tidak terbaca
GitCommit=9f3c2a1
```

### 4. Aturan build yang boleh di-deploy (Guard)

Untuk component `C` di mesin dengan Client `K`:

| Kondisi | Build yang boleh |
|---|---|
| `K` meng-override `C` | hanya build `@K` |
| selain itu (termasuk `K` kosong) | hanya build default |

Tidak ada fallback: jika `K` meng-override `C` tetapi build `@K` belum di-import, deploy ditolak — tidak diam-diam memakai default. Aturan ini dicek di **Guards** (PlanBuilder) dan dicek ulang di **Deployer** sebelum eksekusi, seperti gating environment. Dropdown build di Developer Console hanya menampilkan build yang lolos aturan ini.

### 5. Dashboard dan History

- Dashboard menampilkan `Profile: QRISTAP · Client: BCA`.
- `HistoryRecord` bertambah `Client`, `GitBranch`, `GitCommit`.
- Dashboard memberi peringatan bila build terakhir yang di-deploy (dari History) untuk suatu component tidak sesuai aturan §4 bagi Client mesin saat ini, mis. setelah developer mengganti `Client=`.

### Contoh

| Mesin | ion-middleware | wind | qraptor |
|---|---|---|---|
| `Client=` (kosong) | default | default | default |
| `Client=BCA` | default | `@BCA` | `@BCA` |
| `Client=BRI` | `@BRI` | default | default |

Pindah BCA → BRI: developer mengubah `Client=BRI`; Dashboard menandai `ion-middleware`, `wind`, `qraptor` tidak sesuai; developer men-deploy ulang ketiganya lewat Developer Console. Build `@BCA` tetap di cache, jadi kembali ke BCA tidak perlu import ulang.

Branch Client di-merge ke default: hapus component dari `[Overrides]`; build `@CLIENT` yang tersisa diabaikan dengan peringatan (pola yang sama dengan file release sisa).

## Options Considered

### Option A: Client sebagai atribut build + `clients.ini` (dipilih)
| Dimension | Assessment |
|---|---|
| Complexity | Low — satu file baru, field tambahan di model yang ada |
| Dampak ke model release | Tidak ada |
| Mencegah mesin tercampur | Ya (Guard + peringatan Dashboard) |

**Pros:** sesuai alur dev (deploy per component dari build); kompatibel ke belakang; `[Overrides]` sekaligus mendokumentasikan branch mana yang menyimpang per Client.
**Cons:** developer harus memastikan `[Overrides]` diperbarui saat membuat/menutup branch Client.

### Option B: Component dan Profile terpisah per Client (`wind-bca`, `QRISTAP-BCA`)
**Pros:** tanpa perubahan kode.
**Cons:** jumlah file component × Client; profile terduplikasi; tidak ada guard pencampuran.

### Option C: Release component per Client (`Components\wind\BCA\...`)
**Pros:** siap untuk Release Tahap 2.
**Cons:** melonggarkan aturan satu release per component dan menduplikasi target/service yang identik; tidak dibutuhkan untuk alur dev.

### Option D: Fallback ke default bila build Client tidak ada
**Cons:** binary yang salah terpasang diam-diam; bug terlihat seperti bug aplikasi. Ditolak.

## Trade-off Analysis

Perbedaan antar Client hanya ada di binary, dan di mesin dev binary = build hasil import. Menaruh Client di build menyelesaikan masalah di lapisan pemiliknya tanpa menyentuh model release. Satu-satunya biaya adalah menjaga `[Overrides]` tetap akurat — biaya yang sama juga menjadi nilai, karena daftar itu adalah sumber kebenaran tentang branch yang menyimpang.

## Consequences

- **Kompatibel ke belakang:** build lama (tanpa `Client`) dan `machine.ini` tanpa `Client=` dianggap default.
- **Ditunda:** pin versi untuk build Client; nilai Registry/INI berbeda per Client; pengecekan kecocokan antar branch; pembatasan Profile per Client; mengganti nama (Id) Client yang sudah disimpan.
- **Ditinjau ulang:** saat Release Tahap 2 (paket per Client kemungkinan butuh Option C), atau bila tim pindah ke feature flag (Client berubah dari pemilih build menjadi pemilih nilai config).

## Action Items
1. [ ] `clients.ini` + pembaca dan validasinya.
2. [ ] `MachineConfig.Client`.
3. [ ] `BuildInfo` / `BuildImporter` / `Paths`: field Client, GitBranch, GitCommit; path cache `@<CLIENT>`; saran Client dari branch git.
4. [ ] Guard kecocokan Client di PlanBuilder dan Deployer; filter build di Developer Console.
5. [ ] `HistoryRecord`: Client, GitBranch, GitCommit.
6. [ ] Dashboard: tampilkan Client dan peringatan component yang tidak sesuai.
7. [x] Layar **Clients**: lihat data mesin, pilih `Client=`, dan kelola `clients.ini` (New/Save/Delete; Delete ditolak untuk Client yang dipakai mesin ini).
