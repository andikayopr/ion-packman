using System.IO;
using System.Security.Cryptography;
using IonPackMan.History;
using IonPackMan.Machine;

namespace IonPackMan.Deployment;

/// <summary>Langkah yang sedang berjalan; dipakai dialog progres untuk menandai ✔ per langkah.</summary>
public enum DeployStep { Checking, BackingUp, StoppingService, CopyingFiles, ApplyingConfig, StartingService, Finalizing, Restoring }

/// <param name="Errors">Alasan penolakan (Rejected) atau pesan kegagalan eksekusi.</param>
/// <param name="RestoreFailures">Bagian backup yang gagal dikembalikan (tidak kosong = FailedRestoreIncomplete).</param>
/// <param name="HistoryProblems">Masalah menulis History (hasil eksekusi tetap berlaku).</param>
/// <param name="Warnings">Hanya pindah Profile: langkah setelah deploy yang gagal (Profile belum aktif); deploy-nya sendiri berhasil.</param>
public sealed record DeploymentResult(
    HistoryOutcome Outcome, string ExecId, IReadOnlyList<string> Errors,
    IReadOnlyList<string> RestoreFailures, string BackupDirectory, IReadOnlyList<string> HistoryProblems,
    IReadOnlyList<string>? Warnings = null)
{
    public bool Succeeded => Outcome == HistoryOutcome.Succeeded;
}

/// <summary>
/// Menjalankan satu <see cref="DeploymentPlan"/> dari awal sampai akhir (System Design §3.4).
/// Satu-satunya class yang mengubah mesin saat eksekusi. Urutannya: Guards → backup → stop service (atau tutup program
/// biasa) → copy file → terapkan Registry/INI → start service (atau jalankan lagi programnya) → tandai backup selesai. Gagal di langkah mana pun = restore seluruh backup.
/// </summary>
public sealed class Deployer
{
    private readonly MachineConfig _machine;
    private readonly Guards _guards;
    private readonly BackupStore _backups;
    private readonly IServiceControl _services;
    private readonly IProcessControl _processes;
    private readonly IRegistryAccess _registry;
    private readonly HistoryStore _history;
    private readonly TimeSpan _serviceTimeout;

    public Deployer(MachineConfig machine, Guards guards, BackupStore backups, IServiceControl services,
        IProcessControl processes, IRegistryAccess registry, HistoryStore history, TimeSpan? serviceTimeout = null)
    {
        _machine = machine;
        _guards = guards;
        _backups = backups;
        _services = services;
        _processes = processes;
        _registry = registry;
        _history = history;
        _serviceTimeout = serviceTimeout ?? TimeSpan.FromSeconds(30);
    }

    public DeploymentResult Run(DeploymentPlan plan, IProgress<DeployStep>? progress = null)
    {
        var started = DateTimeOffset.Now;

        // Guards dicek ulang tepat sebelum eksekusi (bukan hanya saat Preview): keadaan mesin bisa berubah sejak itu.
        progress?.Report(DeployStep.Checking);
        var rejected = _guards.Check(plan);
        if (rejected.Count > 0)
            return Finish(plan, started, HistoryOutcome.Rejected, rejected, [], "");

        // Create hanya menulis di folder backup; bila gagal, mesin belum disentuh.
        progress?.Report(DeployStep.BackingUp);
        var (backup, backupErrors) = _backups.Create(plan);
        if (backup is null)
            return Finish(plan, started, HistoryOutcome.Rejected, backupErrors, [], "");

        // Program yang akan dijalankan lagi: yang sedang berjalan sekarang ditambah yang terlihat saat Preview.
        var programs = new List<string>();
        try
        {
            if (plan.StopServiceBefore)
            {
                progress?.Report(DeployStep.StoppingService);
                _services.Stop(plan.ServiceName, _serviceTimeout);
            }
            if (plan.RestartsPrograms)
            {
                progress?.Report(DeployStep.StoppingService);
                programs.AddRange(_processes.FindExecutablesUnder(plan.TargetDirectory).Concat(plan.ProgramsAtPlan)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
                _processes.StopAllUnder(plan.TargetDirectory, _serviceTimeout);
            }
            if (plan.DeployFiles)
            {
                progress?.Report(DeployStep.CopyingFiles);
                CopyFiles(plan);   // file lama yang tidak ada di build dibiarkan
            }
            if (plan.ApplyConfig)
            {
                progress?.Report(DeployStep.ApplyingConfig);
                ApplyConfig(plan);
            }
            if (plan.StartServiceAfter)
            {
                progress?.Report(DeployStep.StartingService);
                _services.StartAndWaitRunning(plan.ServiceName, _serviceTimeout);   // health check = status Running
            }
            if (plan.RestartsPrograms)
            {
                progress?.Report(DeployStep.StartingService);
                StartPrograms(plan, programs);
            }

            // Bila MarkDone gagal, backup tetap InProgress; lebih aman mengembalikan semuanya daripada mengaku sukses.
            progress?.Report(DeployStep.Finalizing);
            _backups.MarkDone(backup);
            return Finish(plan, started, HistoryOutcome.Succeeded, [], [], backup.BackupDir);
        }
        catch (Exception ex)
        {
            progress?.Report(DeployStep.Restoring);
            var restoreFailures = _backups.Restore(backup.ExecId);
            var outcome = restoreFailures.Count == 0 ? HistoryOutcome.FailedRestored : HistoryOutcome.FailedRestoreIncomplete;
            var errors = new List<string> { ex.Message };

            // Program yang tadi ditutup dinyalakan lagi dari file lama, tapi hanya bila file sudah pulih seluruhnya.
            if (programs.Count > 0 && restoreFailures.Count == 0)
            {
                try { StartPrograms(plan, programs); }
                catch (Exception startEx) { errors.Add($"The program could not be started again after the restore: {startEx.Message}"); }
            }
            return Finish(plan, started, outcome, errors, restoreFailures, backup.BackupDir);
        }
    }

    // Yang sudah berjalan dilewati: dipanggil juga setelah kegagalan di tengah start, supaya tidak ada program yang jalan dobel.
    private void StartPrograms(DeploymentPlan plan, IReadOnlyList<string> programs)
    {
        var running = _processes.FindExecutablesUnder(plan.TargetDirectory);
        foreach (string exe in programs.Except(running, StringComparer.OrdinalIgnoreCase))
            _processes.Start(exe);
    }

    private static void CopyFiles(DeploymentPlan plan)
    {
        foreach (var file in plan.Files.Where(f => f.Change != FileChange.Same))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.TargetPath)!);
            IoRetry.Run(() => File.Copy(file.SourcePath, file.TargetPath, overwrite: true));
            if (!HashOf(file.TargetPath).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"'{file.RelativePath}' was copied but its content does not match the build.");
        }
    }

    // Setiap nilai dibaca kembali setelah ditulis: "ditulis" harus berarti "terbaca sama".
    private void ApplyConfig(DeploymentPlan plan)
    {
        foreach (var r in plan.RegistryChanges)
        {
            _registry.Write(r.Key, r.ValueName, r.Type, r.NewValue);
            var back = _registry.Read(r.Key, r.ValueName);
            if (back is null || back.Type != r.Type || back.Value != r.NewValue)
                throw new IOException($"Registry value '{r.ValueName}' in '{r.Key}' did not keep the new value.");
        }
        foreach (var i in plan.IniChanges)
        {
            AppIniEditor.Write(i.File, i.Section, i.Key, i.NewValue);
            var (exists, value) = AppIniEditor.Read(i.File, i.Section, i.Key);
            if (!exists || value != i.NewValue)
                throw new IOException($"INI key [{i.Section}] {i.Key} in '{i.File}' did not keep the new value.");
        }
    }

    private DeploymentResult Finish(DeploymentPlan plan, DateTimeOffset started, HistoryOutcome outcome,
        IReadOnlyList<string> errors, IReadOnlyList<string> restoreFailures, string backupDir)
    {
        var record = new HistoryRecord
        {
            Id = plan.ExecId,
            StartedAt = started,
            FinishedAt = DateTimeOffset.Now,
            Operator = $"{Environment.UserDomainName}\\{Environment.UserName}",
            Machine = _machine.MachineName,
            Profile = plan.ProfileId,
            Component = plan.ComponentId,
            BuildVersion = plan.ReleaseVersion,
            Operations = plan.OperationNames,
            Outcome = outcome,
            BackupDirectory = backupDir,
            ErrorMessage = string.Join(" | ", errors),
            RestoreFailures = restoreFailures,
        };
        var historyProblems = _history.Write(record);
        return new DeploymentResult(outcome, plan.ExecId, errors, restoreFailures, backupDir, historyProblems);
    }

    private static string HashOf(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
