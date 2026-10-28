namespace IonPackMan.Deployment;

/// <summary>
/// Status satu backup (ADR-004). Hanya <see cref="InProgress"/> dan <see cref="RestoreIncomplete"/>
/// yang berarti "mesin mungkin belum kembali ke kondisi semula" dan mengunci Developer Console.
/// </summary>
public enum BackupStatus { InProgress, Done, Restored, RestoreIncomplete }

/// <summary>Nilai Registry sebelum eksekusi. <c>Type</c> hanya terisi bila <c>Existed</c> = true.</summary>
public sealed record BackupRegistryEntry(string Name, string Key, string ValueName, bool Existed, RegistryValueType? Type, string OldValue);

/// <summary>Nilai INI aplikasi sebelum eksekusi.</summary>
public sealed record BackupIniEntry(string Name, string File, string Section, string Key, bool Existed, string OldValue);

/// <summary>
/// Isi satu <c>backup.ini</c> (System Design §3.5). Hanya data; sumber kebenarannya adalah file di disk,
/// objek ini adalah salinan yang dibaca dari sana.
/// </summary>
public sealed record Backup
{
    public required string ExecId { get; init; }

    /// <summary>Folder backup ini: <c>Cache\Backups\&lt;execId&gt;</c>.</summary>
    public required string BackupDir { get; init; }

    public required BackupStatus Status { get; init; }

    /// <summary>Kosong bila component tidak berjalan sebagai Windows service.</summary>
    public string ServiceName { get; init; } = "";

    /// <summary>Service berstatus Running saat backup dibuat; dipakai Restore untuk mengembalikan state awal.</summary>
    public bool ServiceWasRunning { get; init; }

    /// <summary>Path file target yang akan diganti; salinannya ada di <c>files\</c>.</summary>
    public IReadOnlyList<string> ReplacedFiles { get; init; } = [];

    /// <summary>File yang belum ada sebelum eksekusi; Restore menghapusnya.</summary>
    public IReadOnlyList<string> NewFiles { get; init; } = [];

    /// <summary>Direktori yang belum ada sebelum eksekusi (terdalam dulu); Restore menghapusnya hanya bila kosong.</summary>
    public IReadOnlyList<string> NewDirectories { get; init; } = [];

    public IReadOnlyList<BackupRegistryEntry> RegistryEntries { get; init; } = [];

    public IReadOnlyList<BackupIniEntry> IniEntries { get; init; } = [];
}
