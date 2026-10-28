using IonPackMan.History;
using IonPackMan.Machine;

namespace IonPackMan.Deployment;

/// <param name="Component">Diambil dari ExecId (yyyyMMdd-HHmmss-&lt;component&gt;-&lt;hex&gt;); "unknown" bila tidak terbaca.</param>
public sealed record PendingRecovery(string ExecId, BackupStatus Status, string Component);

public sealed record RecoveryResult(bool Restored, IReadOnlyList<string> Failures, IReadOnlyList<string> HistoryProblems);

/// <summary>
/// Pemulihan eksekusi yang terputus (PRD R11): backup berstatus InProgress atau RestoreIncomplete saat aplikasi dibuka,
/// misalnya setelah mati listrik atau aplikasi dimatikan di tengah copy. Mengembalikan SELURUH isi backup lewat
/// <see cref="BackupStore.Restore(string)"/> (aman diulang) dan mencatat hasilnya di History.
/// </summary>
public sealed class RecoveryService
{
    private readonly BackupStore _backups;
    private readonly HistoryStore _history;
    private readonly MachineConfig _machine;

    public RecoveryService(BackupStore backups, HistoryStore history, MachineConfig machine)
    {
        _backups = backups;
        _history = history;
        _machine = machine;
    }

    /// <summary>Backup yang menunggu pemulihan (terlama dulu) dan backup yang tidak terbaca (tetap harus dibereskan manual).</summary>
    public (List<PendingRecovery> Pending, List<string> Problems) FindPending()
    {
        var (unfinished, problems) = _backups.FindUnfinished();
        return (unfinished.OrderBy(b => b.ExecId, StringComparer.Ordinal)
                          .Select(b => new PendingRecovery(b.ExecId, b.Status, ComponentOf(b.ExecId))).ToList(),
                problems);
    }

    public bool HasPending()
    {
        var (pending, problems) = FindPending();
        return pending.Count > 0 || problems.Count > 0;
    }

    /// <summary>Memulihkan satu backup dan menulis History (RestoredAfterCrash, atau FailedRestoreIncomplete bila belum tuntas).</summary>
    public RecoveryResult Restore(string execId)
    {
        var started = DateTimeOffset.Now;
        var failures = _backups.Restore(execId);
        string component = ComponentOf(execId);

        var record = new HistoryRecord
        {
            Id = HistoryRecord.NewExecId(component),
            StartedAt = started,
            FinishedAt = DateTimeOffset.Now,
            Operator = $"{Environment.UserDomainName}\\{Environment.UserName}",
            Machine = _machine.MachineName,
            Component = component,
            Operations = ["Restore"],
            Outcome = failures.Count == 0 ? HistoryOutcome.RestoredAfterCrash : HistoryOutcome.FailedRestoreIncomplete,
            BackupDirectory = Paths.BackupDir(_machine.LocalCacheDirectory, execId),
            ErrorMessage = $"Restored backup {execId} after an interrupted execution.",
            RestoreFailures = failures,
        };
        return new RecoveryResult(failures.Count == 0, failures, _history.Write(record));
    }

    // ExecId: yyyyMMdd-HHmmss-<component>-<4 hex>; component boleh memuat '-'.
    public static string ComponentOf(string execId)
    {
        int first = execId.IndexOf('-');
        int second = first < 0 ? -1 : execId.IndexOf('-', first + 1);
        int last = execId.LastIndexOf('-');
        return second >= 0 && last > second ? execId[(second + 1)..last] : "unknown";
    }
}
