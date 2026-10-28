# ADR-003: INI — reader/writer kecil untuk file PackMan, Win32 API untuk INI aplikasi

**Status:** Accepted (28 Okt 2026, menggantikan usulan "engine Strict + Surgical")
**Date:** 2026-10-28
**Deciders:** Tech lead, developer ION

## Context

Semua konfigurasi PackMan berformat INI (D11) dengan aturan ketat: duplikat dan key tidak dikenal adalah error (PRD §7.2). PackMan juga mengubah INI milik aplikasi (`GPFCNFG.ini`, `WebUIUXCNFG.ini`) dan tidak boleh merusak isi lainnya. Kode harus mudah dipahami junior developer.

## Decision

Dua class kecil di folder `Ini/`:

| Class | File | Baca | Tulis |
|---|---|---|---|
| `IniFile` | Milik PackMan (machine, allowlist, component, profile, backup, history) | Parser baris per baris → `section → key → value`; error dengan nomor baris | Buat ulang seluruh file dari data (`.tmp` → rename) |
| `AppIniEditor` | Milik aplikasi ATM | `GetPrivateProfileString` | `WritePrivateProfileString` (hanya mengubah key yang dituju) |

Validasi isi (key wajib, nilai yang diizinkan) ditulis langsung di method `Load` setiap store.

## Options Considered

### Option A: `IniFile` kecil + Win32 untuk INI aplikasi (dipilih)
| Dimension | Assessment |
|---|---|
| Complexity | Low — ~150 baris |
| Keamanan file aplikasi | Tinggi — Windows hanya mengubah satu key |
| Mudah dipahami junior | Tinggi |

**Pros:** sederhana; perilaku penulisan INI aplikasi sama dengan aplikasi Windows lain.
**Cons:** komentar di file INI yang **dibuat** PackMan (Component/Profile) hilang saat disimpan ulang; Win32 API hanya mendukung ANSI dan UTF-16 (cukup jika INI aplikasi ANSI, lihat Open Q#2).

### Option B: Engine sendiri dengan document model (Strict + Surgical)
| Dimension | Assessment |
|---|---|
| Complexity | Medium — ~400 baris + banyak test encoding |
| Keamanan file aplikasi | Tinggi |
| Mudah dipahami junior | Rendah |

**Pros:** kontrol penuh, termasuk UTF-8.
**Cons:** kode paling rumit di seluruh aplikasi untuk masalah yang sudah diselesaikan Windows.

### Option C: Library pihak ketiga
**Cons:** sebagian hanya membaca, sebagian membuang format saat menulis; menambah dependency yang harus dibawa offline.

## Trade-off Analysis

File `machine.ini` dan `allowlist.ini` ditulis manual oleh developer dan **tidak pernah ditulis** oleh PackMan, jadi komentarnya aman. Component/Profile dikelola lewat UI, sehingga kehilangan komentar dapat diterima. Untuk file aplikasi, Win32 API adalah solusi bawaan yang paling aman dan paling sederhana.

## Consequences

- **Lebih mudah:** kode INI kecil dan mudah diuji.
- **Lebih sulit:** jika ternyata INI aplikasi berformat UTF-8 tanpa BOM dan berisi karakter non-ASCII, Win32 API tidak cocok, dan Option B perlu dipertimbangkan untuk `AppIniEditor` saja.
- **Ditinjau ulang:** setelah Open Q#2 (encoding INI aplikasi) terjawab.

## Action Items
1. [ ] Ambil salinan `GPFCNFG.ini` dan `WebUIUXCNFG.ini` asli; cek encoding-nya.
2. [ ] Implementasi `IniFile` + unit test (duplikat, key tidak dikenal, round-trip data).
3. [ ] Implementasi `AppIniEditor` + test di salinan file asli.
