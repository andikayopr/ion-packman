using IonPackMan.Builds;
using IonPackMan.Components;
using IonPackMan.Deployment;
using IonPackMan.History;
using IonPackMan.Machine;
using IonPackMan.Profiles;

namespace IonPackMan;

/// <summary>
/// Perakitan semua class fitur saat aplikasi dibuka (satu-satunya tempat yang tahu cara menyambungkannya).
/// Bagian yang bergantung pada file konfigurasi yang rusak bernilai null; alasannya ada di daftar error-nya,
/// supaya layar bisa menampilkan penjelasan, bukan crash.
/// </summary>
public sealed class AppServices
{
    public MachineConfig? Machine { get; private init; }
    public List<string> MachineErrors { get; private init; } = [];
    public AllowList? AllowList { get; private init; }
    public List<string> AllowListErrors { get; private init; } = [];

    public required ComponentStore Components { get; init; }
    public required MachineInspector Inspector { get; init; }
    public ProfileValidator? Validator { get; private init; }
    public ProfileStore? Profiles { get; private init; }
    public BuildImporter? Builds { get; private init; }
    public HistoryStore? History { get; private init; }
    public required DashboardReader Dashboard { get; init; }

    // Eksekusi (iterasi 1c). Semua null bila machine.ini atau allowlist.ini tidak valid: tanpa keduanya tidak ada yang boleh dijalankan.
    public BackupStore? Backups { get; private init; }
    public Guards? Guards { get; private init; }
    public PlanBuilder? Planner { get; private init; }
    public Deployer? Deployer { get; private init; }
    public ProfileDetector? Detector { get; private init; }

    public static AppServices Create(string releaseDir, string machineIniPath)
    {
        var (machine, machineErrors) = MachineConfig.Load(machineIniPath);
        var (allowList, allowListErrors) = AllowList.Load(Paths.AllowListIni(releaseDir));

        var components = new ComponentStore(releaseDir);
        var inspector = new MachineInspector();

        ProfileValidator? validator = allowList is null ? null : new ProfileValidator(components, allowList);
        ProfileStore? profiles = validator is null ? null : new ProfileStore(releaseDir, validator);
        BuildImporter? builds = machine is null ? null : new BuildImporter(machine.LocalCacheDirectory);
        HistoryStore? history = machine is null ? null : new HistoryStore(machine.LocalCacheDirectory);
        ProfileDetector? detector = profiles is null ? null : new ProfileDetector(components, profiles);

        // Registry dan service sungguhan hanya dibuat di sini; semua tes memakai versi palsu.
        IRegistryAccess registry = new RegistryAccess();
        IServiceControl serviceControl = new ServiceControl();

        BackupStore? backups = machine is null || allowList is null ? null : new BackupStore(machine.LocalCacheDirectory, registry, serviceControl, allowList);
        Guards? guards = machine is null || allowList is null || builds is null || backups is null ? null
            : new Guards(machine, allowList, builds, () => PendingBackupLines(backups));
        PlanBuilder? planner = machine is null || allowList is null || profiles is null || validator is null || builds is null || guards is null ? null
            : new PlanBuilder(machine, allowList, components, profiles, validator, builds, registry, serviceControl, guards);
        Deployer? deployer = machine is null || guards is null || backups is null || history is null ? null
            : new Deployer(machine, guards, backups, serviceControl, registry, history);

        return new AppServices
        {
            Machine = machine,
            MachineErrors = machineErrors,
            AllowList = allowList,
            AllowListErrors = allowListErrors,
            Components = components,
            Inspector = inspector,
            Validator = validator,
            Profiles = profiles,
            Builds = builds,
            History = history,
            Detector = detector,
            Backups = backups,
            Guards = guards,
            Planner = planner,
            Deployer = deployer,
            Dashboard = new DashboardReader(machine, machineErrors, inspector, detector, profiles, validator, components, history),
        };
    }

    /// <summary>Backup yang belum selesai atau bermasalah, sebagai baris teks untuk Guards dan pesan ke pengguna.</summary>
    public static List<string> PendingBackupLines(BackupStore backups)
    {
        var (unfinished, problems) = backups.FindUnfinished();
        return unfinished.Select(b => $"{b.ExecId} ({b.Status})").Concat(problems).ToList();
    }
}
