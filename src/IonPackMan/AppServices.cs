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
    /// <summary>Masalah di Config\config-entries.ini; tidak kosong = layar yang memakai config (Profiles, Developer Console) tidak dibuka.</summary>
    public List<string> ConfigCatalogErrors { get; private init; } = [];
    /// <summary>Client dan override-nya (Config\clients.ini); <see cref="ClientConfig.None"/> bila file tidak ada atau rusak.</summary>
    public ClientConfig Clients { get; private init; } = new();
    /// <summary>Folder rilis (tempat Config\clients.ini yang disimpan layar Clients).</summary>
    public string ReleaseDirectory { get; private init; } = "";
    /// <summary>Masalah saat membaca clients.ini; dikosongkan setelah layar Clients menyimpan file yang valid.</summary>
    public List<string> ClientFileErrors { get; private init; } = [];
    public void ClientsSaved() => ClientFileErrors.Clear();

    /// <summary>Masalah di clients.ini atau Client= di machine.ini; tidak kosong = Developer Console tidak dibuka (ADR-006).</summary>
    public List<string> ClientErrors
    {
        get
        {
            var errors = new List<string>(ClientFileErrors);
            if (errors.Count == 0 && Machine is { Client.Length: > 0 } m && !Clients.IsKnown(m.Client))
                errors.Add($"machine.ini [Machine] Client '{m.Client}' is not listed in {Paths.ClientsIni(ReleaseDirectory)}.");
            return errors;
        }
    }

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
    public ProfileSwitcher? Switcher { get; private init; }
    /// <summary>Satu eksekusi pada satu waktu: dipakai Developer Console dan pindah Profile.</summary>
    public ExecutionLock Gate { get; private init; } = new();
    public ProfileDetector? Detector { get; private init; }
    public RecoveryService? Recovery { get; private init; }

    public static AppServices Create(string releaseDir, string machineIniPath)
    {
        var (machine, machineErrors) = MachineConfig.Load(machineIniPath);
        var (allowList, allowListErrors) = AllowList.Load(Paths.AllowListIni(releaseDir));

        var components = new ComponentStore(releaseDir);
        var inspector = new MachineInspector();

        var (configCatalog, configCatalogErrors) = ConfigCatalog.Load(Paths.ConfigCatalogIni(releaseDir));

        // Satu instance dipegang semua layanan; layar Clients memperbaruinya di tempat setelah menyimpan.
        var (loadedClients, clientErrors) = ClientConfig.Load(Paths.ClientsIni(releaseDir), components.LoadCatalog().Entries.Select(e => e.Id).ToList());
        var clients = new ClientConfig();
        if (loadedClients is not null)
            clients.Replace(loadedClients);

        ProfileValidator? validator = allowList is null ? null : new ProfileValidator(components, allowList, configCatalog);
        ProfileStore? profiles = validator is null ? null : new ProfileStore(releaseDir, validator);
        BuildImporter? builds = machine is null ? null : new BuildImporter(machine.LocalCacheDirectory, clients: clients);
        HistoryStore? history = machine is null ? null : new HistoryStore(machine.LocalCacheDirectory);
        ProfileDetector? detector = profiles is null ? null : new ProfileDetector(components, profiles);

        // Registry dan service sungguhan hanya dibuat di sini; semua tes memakai versi palsu.
        IRegistryAccess registry = new RegistryAccess();
        IServiceControl serviceControl = new ServiceControl();
        IProcessControl processControl = new ProcessControl(inspector);

        BackupStore? backups = machine is null || allowList is null ? null : new BackupStore(machine.LocalCacheDirectory, registry, serviceControl, allowList);
        Guards? guards = machine is null || allowList is null || builds is null || backups is null ? null
            : new Guards(machine, allowList, builds, () => PendingBackupLines(backups), clients: clients);
        PlanBuilder? planner = machine is null || allowList is null || profiles is null || validator is null || builds is null || guards is null ? null
            : new PlanBuilder(machine, allowList, components, profiles, validator, builds, registry, serviceControl, processControl, guards, clients);
        Deployer? deployer = machine is null || guards is null || backups is null || history is null ? null
            : new Deployer(machine, guards, backups, serviceControl, processControl, registry, history);
        var gate = new ExecutionLock();
        ProfileSwitcher? switcher = machine is null || allowList is null || profiles is null || validator is null || detector is null || planner is null || deployer is null ? null
            : new ProfileSwitcher(machine, allowList, profiles, validator, components, detector, serviceControl, planner, deployer, gate);

        return new AppServices
        {
            Machine = machine,
            MachineErrors = machineErrors,
            AllowList = allowList,
            AllowListErrors = allowListErrors,
            ConfigCatalogErrors = configCatalogErrors,
            Clients = clients,
            ClientFileErrors = clientErrors,
            ReleaseDirectory = releaseDir,
            Components = components,
            Inspector = inspector,
            Validator = validator,
            Profiles = profiles,
            Builds = builds,
            History = history,
            Detector = detector,
            Recovery = machine is null || backups is null || history is null ? null : new RecoveryService(backups, history, machine),
            Backups = backups,
            Guards = guards,
            Planner = planner,
            Deployer = deployer,
            Switcher = switcher,
            Gate = gate,
            Dashboard = new DashboardReader(machine, machineErrors, inspector, detector, profiles, validator, components, history, clients),
        };
    }

    /// <summary>Backup yang belum selesai atau bermasalah, sebagai baris teks untuk Guards dan pesan ke pengguna.</summary>
    public static List<string> PendingBackupLines(BackupStore backups)
    {
        var (unfinished, problems) = backups.FindUnfinished();
        return unfinished.Select(b => $"{b.ExecId} ({b.Status})").Concat(problems).ToList();
    }
}
