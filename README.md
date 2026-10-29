# ION PackMan

Aplikasi desktop offline untuk men-deploy komponen ION (ION Middleware, WIND, QRAPTOR, Non-NDC Parser, MDD Service) ke mesin ATM/CRM tanpa jaringan. Tahap 1 mencakup Dashboard, Components, Profiles, Clients, Developer Console, dan History. Menu Release ada di Tahap 2.

WPF, .NET 8, hanya Windows. Konfigurasi memakai file INI.

## Menjalankan

```bash
dotnet build IonPackMan.sln
dotnet run --project src/IonPackMan
```

Aplikasi perlu dijalankan sebagai Administrator (mengatur service dan Registry). Di Visual Studio, pilih profil `IonPackMan (1024x768)` atau `IonPackMan (maximized)` di sebelah tombol Start.

Opsi baris perintah (semua opsional):

| Opsi | Fungsi |
|---|---|
| `--machine <path>` | Memakai `machine.ini` lain (bawaan `D:\ION-PackMan\machine.ini`) |
| `--release <folder>` | Memakai folder data lain (bawaan: folder `IonPackMan.exe`) |
| `--window 1024x768` | Membuka jendela dengan ukuran itu, tanpa mengubah resolusi layar |

## Konfigurasi

- **`machine.ini`** dibuat manual di mesin. Contohnya ada di [samples/machine.ini](samples/machine.ini). Isinya Environment (Developer Console hanya aktif di `Development`), Profile aktif, Client, lokasi instalasi, dan folder local cache.
- **Data awal** ada di `src/IonPackMan/Data/` dan ikut disalin ke folder rilis saat build:
  - `Components/` — katalog dan release tiap component
  - `Profiles/` — komposisi component per produk
  - `Config/` — `allowlist.ini` (batas operasi yang diizinkan), `config-entries.ini` (entri Registry/INI), `clients.ini` (Client dan override-nya)

## Client (bank)

Kode ION memakai branch git per bank. Client menandai build mana yang boleh di-deploy di mesin dev: build dari branch Client disimpan terpisah (`id@versi@CLIENT`), dan mesin ber-Client hanya menerima build Client untuk component yang di-override. Pengaturan lewat layar **Clients**. Rinciannya di [ADR-006](docs/adr/ADR-006-multi-client-builds.md).

## Tes

```bash
dotnet test tests/IonPackMan.Tests
```

Jika `IonPackMan.exe` sedang berjalan, file outputnya terkunci. Tutup aplikasinya atau tambahkan `--artifacts-path <folder lain>`.

Uji manual di VM memakai sandbox dari `tools/PackManTestFixture.ps1` (lihat [docs/Manual-Test-1c.md](docs/Manual-Test-1c.md)).

## Dokumentasi

- [PRD](docs/PRD-ION-PackMan-Phase1.md)
- [System Design](docs/System-Design-ION-PackMan-Phase1.md)
- [UI/UX Spec](docs/UI-UX-Spec-ION-PackMan-Phase1.md)
- Keputusan arsitektur: [docs/adr](docs/adr)
