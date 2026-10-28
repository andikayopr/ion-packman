namespace IonPackMan.Deployment;

public enum FileChange { New, Replace, Same }

/// <summary>Satu file build yang akan disalin. <c>Same</c> = isi sama dengan file di target, tidak disalin.</summary>
public sealed record PlannedFile(string RelativePath, string SourcePath, string TargetPath, long Size, string Sha256, FileChange Change);

/// <param name="CurrentValue">Nilai saat Preview disusun; null = value belum ada. Hanya untuk tampilan: backup membaca ulang nilai terbaru.</param>
public sealed record PlannedRegistryChange(string Name, string Key, string ValueName, RegistryValueType Type, string NewValue, string? CurrentValue);

public sealed record PlannedIniChange(string Name, string File, string Section, string Key, string NewValue, string? CurrentValue);

/// <summary>
/// Semua yang akan dilakukan satu eksekusi (PRD R10), disusun oleh <see cref="PlanBuilder"/> dan dijalankan <see cref="Deployer"/>.
/// Plan hanya data; mengubahnya setelah Preview tidak mengizinkan Execute (layar menghapus plan bila input berubah).
/// </summary>
public sealed record DeploymentPlan
{
    public required string ExecId { get; init; }
    public required string ProfileId { get; init; }
    public required string ComponentId { get; init; }
    public required string ReleaseVersion { get; init; }
    public required string TargetDirectory { get; init; }
    /// <summary>Kosong bila component tidak berjalan sebagai Windows service.</summary>
    public string ServiceName { get; init; } = "";

    public bool DeployFiles { get; init; }
    public bool ApplyConfig { get; init; }
    public bool RestartService { get; init; }

    /// <summary>Status service saat plan disusun; menentukan perlu tidaknya Stop/Start otomatis.</summary>
    public ServiceState ServiceStateAtPlan { get; init; } = ServiceState.NotFound;

    /// <summary>Folder file build di local cache (sumber copy).</summary>
    public string BuildFilesDirectory { get; init; } = "";

    public IReadOnlyList<PlannedFile> Files { get; init; } = [];
    public IReadOnlyList<PlannedRegistryChange> RegistryChanges { get; init; } = [];
    public IReadOnlyList<PlannedIniChange> IniChanges { get; init; } = [];

    /// <summary>Deploy files atau Restart service pada service yang sedang Running: stop dulu (PRD R10).</summary>
    public bool StopServiceBefore =>
        ServiceName.Length > 0 && ServiceStateAtPlan == ServiceState.Running && (DeployFiles || RestartService);

    /// <summary>Service dinyalakan lagi bila tadi dihentikan, atau bila Restart service dipilih.</summary>
    public bool StartServiceAfter => ServiceName.Length > 0 && (StopServiceBefore || RestartService);

    /// <summary>Nama operasi untuk History: DeployFiles, ApplyConfig, RestartService.</summary>
    public IReadOnlyList<string> OperationNames =>
        new[] { DeployFiles ? "DeployFiles" : null, ApplyConfig ? "ApplyConfig" : null, RestartService ? "RestartService" : null }
            .OfType<string>().ToList();
}
