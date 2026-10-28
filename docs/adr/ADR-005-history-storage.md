# ADR-005: Penyimpanan History — satu file INI per eksekusi

**Status:** Accepted (28 Okt 2026)
**Date:** 2026-10-28
**Deciders:** Tech lead

## Context

PRD R12 meminta History append-only yang mencatat timestamp, operator, mesin, profile, component, versi build, operasi, hasil, lokasi backup, dan hasil restore. Volume kecil (≤ 50 eksekusi/hari, satu mesin). Semua konfigurasi memakai INI (D11), dan engineer sering membuka file langsung dengan Notepad.

## Decision

`HistoryStore` menulis **satu file `Cache\History\<execId>.ini` per eksekusi**, sekali tulis (`.tmp` → rename) dan tidak pernah diubah. `execId` diawali timestamp, sehingga urutan nama file sama dengan urutan waktu. Daftar History dibuat dengan membaca isi folder. Pesan error dan detail restore juga ditulis di file ini, sehingga tidak perlu file log terpisah.

```ini
[Meta]
SchemaVersion=1.0

[Execution]
Id=20261028-101530-wind-3f9a
StartedAt=2026-10-28T10:15:30+07:00
FinishedAt=2026-10-28T10:16:42+07:00
Operator=ATM-DEV-001\developer
Machine=ATM-DEV-001
Profile=QRISTAP
Component=wind
BuildVersion=2.1.2-dev.17
Operations=DeployFiles,ApplyConfig
Outcome=Succeeded             ; Succeeded | Rejected | FailedRestored | FailedRestoreIncomplete | RestoredAfterCrash
BackupDirectory=D:\ION-PackMan\Cache\Backups\20261028-101530-wind-3f9a

[Error]                        ; hanya jika gagal
Message=
RestoreFailures=
```

## Options Considered

### Option A: File INI per eksekusi (dipilih)
| Dimension | Assessment |
|---|---|
| Complexity | Low |
| Dependency | Tidak ada |
| Mudah dipahami junior | Tinggi |

**Pros:** konsisten dengan D11; terbaca dengan Notepad; satu file rusak tidak merusak yang lain.
**Cons:** filter harus membaca seluruh folder (tetap cepat untuk ribuan file).

### Option B: SQLite
**Cons:** dependency native; tidak terbaca tanpa tool; berlebihan untuk volume ini.

### Option C: Satu file log gabungan
**Cons:** sulit menyimpan detail per eksekusi; baris terakhir bisa rusak saat crash.

## Trade-off Analysis

Dengan volume sekecil ini, kelebihan database tidak terasa, sedangkan biayanya nyata. File per eksekusi paling sederhana dan paling mudah diaudit.

## Consequences

- **Lebih mudah:** audit manual; ekspor cukup dengan menyalin folder.
- **Ditinjau ulang:** jika History perlu digabung lintas mesin (Tahap 2+).

## Action Items
1. [ ] Implementasi `HistoryStore.Write / GetAll / GetLatest`.
