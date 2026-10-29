using System.Globalization;

namespace IonPackMan.History;

/// <summary>Hasil akhir satu eksekusi (ADR-005). Nama enum = nilai yang ditulis ke file.</summary>
public enum HistoryOutcome { Succeeded, Rejected, FailedRestored, FailedRestoreIncomplete, RestoredAfterCrash }

/// <summary>
/// Laporan satu eksekusi (PRD R12). Ditulis sekali ke Cache\History\&lt;Id&gt;.ini dan tidak pernah diubah.
/// Waktu disimpan per detik (pecahan detik dibuang).
/// </summary>
public sealed record HistoryRecord
{
    public required string Id { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; init; }
    public required string Operator { get; init; }
    public required string Machine { get; init; }
    public string Profile { get; init; } = "";
    public required string Component { get; init; }
    public string BuildVersion { get; init; } = "";
    /// <summary>Client build yang di-deploy (ADR-006); kosong = default atau tidak ada build (tanpa DeployFiles).</summary>
    public string Client { get; init; } = "";
    /// <summary>Branch dan commit git sumber build; kosong bila tidak terbaca atau tanpa DeployFiles.</summary>
    public string GitBranch { get; init; } = "";
    public string GitCommit { get; init; } = "";
    /// <summary>Nama operasi, mis. DeployFiles, ApplyConfig (tanpa koma).</summary>
    public IReadOnlyList<string> Operations { get; init; } = [];
    public required HistoryOutcome Outcome { get; init; }
    public string BackupDirectory { get; init; } = "";
    /// <summary>Boleh multi-baris.</summary>
    public string ErrorMessage { get; init; } = "";
    /// <summary>Satu item per file yang gagal dikembalikan (item satu baris; baris baru diganti spasi saat ditulis).</summary>
    public IReadOnlyList<string> RestoreFailures { get; init; } = [];

    /// <summary>
    /// Id eksekusi: yyyyMMdd-HHmmss-&lt;component&gt;-&lt;4 hex&gt;. Diawali timestamp,
    /// sehingga urutan nama file = urutan waktu. Id ini juga nama folder backup.
    /// </summary>
    public static string NewExecId(string componentId, DateTimeOffset? now = null)
    {
        if (!Components.ComponentRelease.IsValidId(componentId))
            throw new ArgumentException($"'{componentId}' is not a valid component Id.", nameof(componentId));
        var time = now ?? DateTimeOffset.Now;
        return $"{time.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{componentId}-{Random.Shared.Next(0x10000):x4}";
    }
}
