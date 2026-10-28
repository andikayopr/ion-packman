# ADR-004: Keamanan eksekusi — satu `backup.ini` per eksekusi, restore menyeluruh

**Status:** Accepted (28 Okt 2026, menggantikan usulan "write-ahead journal")
**Date:** 2026-10-28
**Deciders:** Tech lead, developer ION

## Context

PRD R11 mewajibkan backup terverifikasi sebelum perubahan dan restore otomatis jika gagal. Mesin ATM bisa mati listrik atau aplikasi bisa dimatikan di tengah eksekusi, sehingga restore juga harus bisa dilakukan **setelah** aplikasi dibuka kembali. Solusinya harus mudah dipahami junior developer.

## Decision

`BackupStore.Create(plan)` dijalankan sebelum perubahan apa pun dan menulis **satu file `backup.ini`** yang mencatat semua yang **akan** diubah:
- file yang akan diganti (salinannya disimpan dan diverifikasi hash),
- file yang akan dibuat baru,
- nilai lama Registry/INI (termasuk "belum ada"),
- status service sebelum eksekusi,
- `Status=InProgress`.

`BackupStore.Restore(backup)` selalu mengembalikan **seluruh isi** `backup.ini`, apa pun langkah yang sudah berjalan. Karena itu restore aman diulang. Saat aplikasi dibuka, backup dengan `Status=InProgress` atau `RestoreIncomplete` mengunci Developer Console dan menampilkan tombol **Restore**.

## Options Considered

### Option A: Satu `backup.ini` + restore menyeluruh (dipilih)
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Tahan crash/mati listrik | Ya |
| Mudah dipahami junior | Tinggi — satu file, satu aturan |

**Pros:** satu mekanisme untuk gagal biasa dan crash; mudah diperiksa manual.
**Cons:** restore bisa menulis ulang item yang sebenarnya belum diubah (tidak berbahaya, karena nilainya sama).

### Option B: Write-ahead journal per langkah
| Dimension | Assessment |
|---|---|
| Complexity | Medium |
| Tahan crash/mati listrik | Ya |
| Mudah dipahami junior | Rendah |

**Pros:** tahu persis langkah mana yang sudah berjalan.
**Cons:** setiap jenis perubahan butuh entri journal dan aksi undo sendiri; dua file untuk dipahami.

### Option C: Backup + try/catch saja
| Dimension | Assessment |
|---|---|
| Complexity | Paling rendah |
| Tahan crash/mati listrik | **Tidak** |

**Cons:** setelah crash tidak ada catatan apa yang harus dikembalikan.

## Trade-off Analysis

Karena seluruh perubahan sudah diketahui dari plan sebelum eksekusi, mencatatnya sekali di depan sudah cukup untuk restore setelah crash. Ini memberi keamanan yang sama dengan journal dengan kode yang jauh lebih sedikit.

## Consequences

- **Lebih mudah:** satu alur restore untuk semua kasus; developer bisa membuka `backup.ini` untuk melihat apa yang diubah.
- **Lebih sulit:** urutan restore harus aman diulang (salin balik file, hapus file baru, tulis nilai lama, kembalikan status service).
- **Ditinjau ulang:** Tahap 2 (banyak komponen sekaligus), yaitu satu `backup.ini` per paket.

## Action Items
1. [ ] Implementasi `BackupStore.Create / Restore / MarkDone / FindUnfinished`.
2. [ ] Test dengan folder sementara: create → ubah → restore → isi identik; restore dua kali → tetap identik.
3. [ ] Test manual di ATM dev: matikan aplikasi di tengah copy, buka lagi, Restore.
