# ADR-001: Model proses — satu aplikasi yang berjalan sebagai admin

**Status:** Accepted (28 Okt 2026, menggantikan usulan "UI + Agent on-demand")
**Date:** 2026-10-28
**Deciders:** Product owner ION PackMan, tech lead

## Context

PackMan harus mengubah mesin (copy ke `D:\ION`/`D:\APCONTAINER`, tulis `HKLM`, kontrol Windows Service), sehingga butuh hak admin. Aplikasi harus portable dari USB tanpa installer (D4, D10) dan hanya dipakai di mesin **Development** (D6). Tim pengembang termasuk junior developer, sehingga arsitektur harus sederhana dan mudah di-debug.

## Decision

Satu executable `IonPackMan.exe` dengan manifest `requireAdministrator`. UAC muncul sekali saat aplikasi dibuka. Keamanan dijaga oleh:
- **Guards** (environment Development, path di bawah base directory, allow-list) yang selalu dicek ulang di `Deployer.Run` tepat sebelum eksekusi.
- Aturan kode: hanya folder `Deployment/` yang boleh mengubah mesin.
- Satu instance aplikasi (mutex).

## Options Considered

### Option A: Satu aplikasi admin (dipilih)
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Cost | Rendah |
| Portabilitas | Tinggi |
| Mudah dipahami junior | Tinggi — tidak ada IPC |

**Pros:** paling sederhana; debugging satu proses; tidak perlu install.
**Cons:** seluruh UI berjalan dengan hak admin, sehingga bug di UI berdampak lebih besar.

### Option B: UI non-admin + Agent admin on-demand (named pipe)
| Dimension | Assessment |
|---|---|
| Complexity | Medium–High — IPC, ACL pipe, nonce, validasi ulang plan |
| Cost | Sedang |
| Portabilitas | Tinggi |
| Mudah dipahami junior | Rendah |

**Pros:** least privilege; trust boundary antar-proses.
**Cons:** kode komunikasi antar-proses dan keamanannya sulit dirawat dan di-debug.

### Option C: Windows Service terpasang
| Dimension | Assessment |
|---|---|
| Complexity | High — installer, lifecycle service |
| Cost | Sedang |
| Portabilitas | Rendah |
| Mudah dipahami junior | Sedang |

**Pros:** operator non-admin bisa eksekusi.
**Cons:** bertentangan dengan portabilitas (D4/D10).

## Trade-off Analysis

Karena PackMan hanya untuk mesin Development dan dipakai oleh developer yang memang admin di mesin tersebut, manfaat least privilege dari B tidak sebanding dengan kompleksitasnya untuk tim. Pengamanan yang benar-benar melindungi mesin (Guards, allow-list, backup & restore) tetap ada di A.

## Consequences

- **Lebih mudah:** satu proses dan satu proyek; alur deploy bisa diikuti langsung di debugger.
- **Lebih sulit:** disiplin kode diperlukan agar logika yang mengubah mesin tidak tersebar ke luar `Deployment/` (cek saat code review).
- **Ditinjau ulang:** jika PackMan dipakai di Test/Production atau oleh operator non-admin → pindah ke Option B atau C.
- **PRD:** R11 dan NFR Keamanan diubah dari "UI non-admin" menjadi "satu aplikasi admin + Guards".

## Action Items
1. [ ] Konfirmasi asumsi A2: operator di mesin dev punya kredensial admin lokal.
2. [ ] Tambahkan `app.manifest` dengan `requireAdministrator`.
3. [ ] Checklist code review: tidak ada `File.Copy/Delete`, `Registry.SetValue`, atau `ServiceController.Stop/Start` di luar `Deployment/`.
