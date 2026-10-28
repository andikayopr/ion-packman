using IonPackMan.Builds;
using IonPackMan.Components;
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
            Dashboard = new DashboardReader(machine, machineErrors, inspector, detector, profiles, validator, components, history),
        };
    }
}
