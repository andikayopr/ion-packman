# ADR-002: Runtime & UI — WPF tanpa MVVM ketat, .NET 8, self-contained

**Status:** Accepted (28 Okt 2026)
**Date:** 2026-10-28
**Deciders:** Product owner ION PackMan, tech lead

## Context

Aplikasi final adalah aplikasi .NET di Windows 10 IoT Enterprise 64-bit yang **offline**, sehingga runtime harus ikut dibawa. Tampilan sebaiknya mendekati prototype (sidebar, kartu, tabel). Tim mencakup junior developer, jadi pola UI harus mudah diikuti. **Keputusan pemilik produk (28 Okt 2026): memakai .NET 8** (standar tim/mesin yang ada). Catatan: dukungan .NET 8 berakhir 10 November 2026; karena aplikasi ini offline dan self-contained, runtime tidak berubah sendiri, tetapi patch keamanan tidak lagi tersedia setelah tanggal itu. Kode ditulis tanpa API khusus .NET 9/10 sehingga dapat dinaikkan ke .NET 10 LTS dengan mengganti `TargetFramework` (lihat "Ditinjau ulang").

## Decision

- **WPF di .NET 8** (`net8.0-windows`), publish `--self-contained -r win-x64`.
- **Tanpa framework MVVM.** Setiap menu adalah `UserControl` dengan XAML untuk tampilan dan **code-behind** untuk event handler. Code-behind memanggil class di folder fitur dan mengisi kontrol (boleh memakai data binding sederhana ke model/list).
- Logika bisnis tidak boleh ada di code-behind (aturan di System Design §2.3).

## Options Considered

### Option A: WPF + code-behind (dipilih)
| Dimension | Assessment |
|---|---|
| Complexity | Low–Medium |
| Tampilan | Dekat dengan prototype |
| Mudah dipahami junior | Tinggi — pola event handler biasa |

**Pros:** tampilan fleksibel; tidak perlu belajar ViewModel/Command/framework MVVM.
**Cons:** tanpa disiplin, code-behind bisa membengkak (dicegah dengan aturan "logika di folder fitur").

### Option B: WPF + MVVM (CommunityToolkit.Mvvm)
| Dimension | Assessment |
|---|---|
| Complexity | Medium |
| Tampilan | Dekat dengan prototype |
| Mudah dipahami junior | Rendah–Sedang |

**Pros:** pemisahan UI/logika yang ketat; mudah di-unit-test.
**Cons:** kurva belajar binding, command, dan ViewModel; dependency tambahan.

### Option C: WinForms
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Tampilan | Lebih sederhana dari prototype |
| Mudah dipahami junior | Tinggi |

**Pros:** paling cepat untuk form.
**Cons:** layout & styling setara prototype lebih sulit.

## Trade-off Analysis

Logika yang perlu diuji sudah berada di folder fitur, bukan di UI, sehingga manfaat utama MVVM (UI yang mudah di-unit-test) tidak terlalu dibutuhkan. Code-behind memberi tampilan WPF dengan pola yang paling mudah dipahami.

## Consequences

- **Lebih mudah:** onboarding junior; satu bahasa (C#) untuk semua bagian; distribusi offline dengan copy folder.
- **Lebih sulit:** ukuran rilis lebih besar (~80–150 MB) karena runtime ikut dibawa.
- **Ditinjau ulang:** jika layar menjadi sangat kompleks, pertimbangkan MVVM per layar. Naikkan ke .NET 10 LTS sebelum rilis produksi atau saat kebijakan keamanan mewajibkan runtime yang masih didukung.

## Action Items
1. [ ] Konfirmasi A1 (OS 64-bit) dan A3 (Windows 10 IoT **Enterprise**).
2. [ ] Buat solusi: satu proyek WPF + satu proyek xUnit.
3. [ ] Uji jalan dari USB di mesin dev tanpa .NET terpasang.
