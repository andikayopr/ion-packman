using System.IO;
using IonPackMan.Components;
using IonPackMan.Controls;
using IonPackMan.History;
using IonPackMan.Profiles;

namespace IonPackMan.Machine;

public sealed record ServiceRow(string Service, string DisplayName, string Component, string Status, StatusKind Kind, string StartType, string Path);

public sealed record ProcessRow(string Process, string Component, string Pid, string Status, StatusKind Kind, string Memory, string Path);

/// <summary>Semua yang ditampilkan Dashboard, sudah berupa teks siap pakai (PRD R1–R3). Tidak ada nilai contoh.</summary>
public sealed record DashboardSnapshot(
    string MachineName, string WindowsVersion, string Environment,
    string ProfileText, StatusKind ProfileKind, string ProfileReason,
    string DeploymentState, StatusKind DeploymentKind,
    string BaseDirectory, string ProfileConfig, string LastDeployment,
    List<ServiceRow> Services, List<ProcessRow> Processes,
    string ClientText = "", IReadOnlyList<string>? ClientWarnings = null,
    string Processor = "Unknown", string Memory = "Unknown");

/// <summary>Membaca kondisi mesin untuk Dashboard. Hanya membaca; tidak mengubah apa pun.</summary>
public sealed class DashboardReader
{
    private readonly MachineConfig? _machine;
    private readonly IReadOnlyList<string> _machineErrors;
    private readonly MachineInspector _inspector;
    private readonly ProfileDetector? _detector;
    private readonly ProfileStore? _profiles;
    private readonly ProfileValidator? _validator;
    private readonly ComponentStore _components;
    private readonly HistoryStore? _history;
    private readonly ClientConfig _clients;

    /// <param name="clients">Client dan override-nya (ADR-006); null = tidak ada Client.</param>
    /// <param name="machine">null bila machine.ini tidak valid; alasannya ada di <paramref name="machineErrors"/>.</param>
    public DashboardReader(MachineConfig? machine, IReadOnlyList<string> machineErrors, MachineInspector inspector,
        ProfileDetector? detector, ProfileStore? profiles, ProfileValidator? validator, ComponentStore components, HistoryStore? history, ClientConfig? clients = null)
    {
        _clients = clients ?? ClientConfig.None;
        _machine = machine;
        _machineErrors = machineErrors;
        _inspector = inspector;
        _detector = detector;
        _profiles = profiles;
        _validator = validator;
        _components = components;
        _history = history;
    }

    public DashboardSnapshot Read()
    {
        var os = _inspector.Inspect();
        var detection = _machine is not null && _detector is not null
            ? _detector.Detect(_machine)
            : new DetectionResult(null, _machineErrors.FirstOrDefault() ?? "machine.ini is not available.");

        Profile? profile = null;
        List<string> profileProblems = [];
        if (detection.ProfileId is not null && _profiles is not null)
        {
            var (loaded, errors) = _profiles.Load(detection.ProfileId);
            profile = loaded;
            profileProblems = errors.Count > 0 ? errors : _validator?.Validate(loaded!) ?? [];
        }

        var (deployText, deployKind, lastText, records) = ReadHistory();
        var (services, processes) = ReadServicesAndProcesses(profile);

        return new DashboardSnapshot(
            os.MachineName, os.WindowsVersion, _machine?.Environment ?? "Unknown",
            profile is null ? "Unknown" : string.IsNullOrEmpty(profile.DisplayName) ? profile.Id : profile.DisplayName,
            profile is null ? StatusKind.Warning : StatusKind.Success,
            detection.Reason,
            deployText, deployKind,
            _machine is null ? "Unknown" : string.Join(", ", _machine.InstallLocations.Values),
            ProfileConfigText(profile, profileProblems),
            lastText,
            services, processes,
            _machine is null ? "" : _machine.Client.Length > 0 ? _machine.Client : "none",
            ClientWarnings(profile, records),
            os.Processor, os.Memory);
    }

    // ADR-006 §5: build terakhir yang di-deploy per component harus sesuai Client mesin saat ini
    // (mis. setelah Client= di machine.ini diganti). Hanya eksekusi yang benar-benar menyalin file build yang dihitung.
    private List<string> ClientWarnings(Profile? profile, List<HistoryRecord> records)
    {
        var warnings = new List<string>();
        if (_machine is null || profile is null)
            return warnings;

        foreach (string id in profile.Components.Select(c => c.Id).Distinct())
        {
            var last = records.FirstOrDefault(r => r.Component == id && r.Outcome == HistoryOutcome.Succeeded && r.Operations.Contains("DeployFiles"));
            string expected = _clients.BuildClientFor(_machine.Client, id);
            if (last is null || last.Client.Equals(expected, StringComparison.OrdinalIgnoreCase))
                continue;

            string source = last.GitBranch.Length > 0 ? $" (branch {last.GitBranch})" : "";
            warnings.Add($"{id}: the last deployed build is {(last.Client.Length == 0 ? "default" : last.Client)}{source}, "
                         + $"but this machine needs the {(expected.Length == 0 ? "default" : expected)} build. Deploy it again in the Developer Console.");
        }
        return warnings;
    }

    private string ProfileConfigText(Profile? profile, List<string> problems)
    {
        if (_machine is null) return "machine.ini not valid";
        if (profile is null) return "Not verified (no profile)";
        return problems.Count == 0 ? "Valid" : $"{problems.Count} problem(s): {problems[0]}";
    }

    private (string State, StatusKind Kind, string Last, List<HistoryRecord> Records) ReadHistory()
    {
        if (_history is null)
            return ("Unknown", StatusKind.Neutral, "Not available", []);

        var (records, _) = _history.GetAll();
        var latest = records.FirstOrDefault();
        if (latest is null)
            return ("No deployments yet", StatusKind.Neutral, "Not available", records);

        var kind = latest.Outcome switch
        {
            HistoryOutcome.Succeeded => StatusKind.Success,
            HistoryOutcome.Rejected => StatusKind.Warning,
            HistoryOutcome.RestoredAfterCrash => StatusKind.Info,
            _ => StatusKind.Error,
        };
        return (latest.Outcome.ToString(), kind, $"{latest.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {latest.Component}", records);
    }

    private (List<ServiceRow>, List<ProcessRow>) ReadServicesAndProcesses(Profile? profile)
    {
        var services = new List<ServiceRow>();
        var processes = new List<ProcessRow>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Enumerasi proses mahal: sekali saja untuk semua component dan folder program.
        var allProcesses = _inspector.ListProcesses();

        foreach (var pc in profile?.Components ?? [])
        {
            var release = _components.Load(pc.Id, pc.Version).Release;
            if (release is null)
                continue;

            if (release.ServiceName.Length > 0)
            {
                listed.Add(release.ServiceName);
                var info = _inspector.GetService(release.ServiceName);
                services.Add(info is null
                    ? new ServiceRow(release.ServiceName, "", pc.Id, "Not found", StatusKind.Warning, "—", "")
                    : ToRow(info, pc.Id));
            }

            var running = MachineInspector.ProcessesUnder(allProcesses, release.TargetDirectory);
            if (running.Count == 0)
                processes.Add(new ProcessRow("—", pc.Id, "—", "Not detected", StatusKind.Neutral, "—", release.TargetDirectory));
            processes.AddRange(running.Select(p => new ProcessRow(p.Name, pc.Id, p.Pid.ToString(), "Running", StatusKind.Success,
                $"{p.MemoryBytes / (1024 * 1024)} MB", p.ExecutablePath)));
        }

        if (_machine is not null)
        {
            AddMachineServices(_machine, services, listed);
            AddMachinePrograms(_machine, processes, allProcesses);
        }

        // R2: service lain yang berjalan dari base directory ION juga ditampilkan.
        if (_machine is not null)
            foreach (var info in _inspector.FindServicesUnder(_machine.InstallLocations.Values).Where(s => listed.Add(s.Name)))
                services.Add(ToRow(info, "—"));

        return (services, processes);
    }

    // [SERVICE] di machine.ini: label = Component, nilai = nama Windows Service. Yang tidak ada ditandai, bukan disembunyikan.
    private void AddMachineServices(MachineConfig machine, List<ServiceRow> services, HashSet<string> listed)
    {
        foreach (var (label, serviceName) in machine.Services)
        {
            if (!listed.Add(serviceName))
                continue;
            var info = _inspector.GetService(serviceName);
            services.Add(info is null
                ? new ServiceRow(serviceName, "", label, "Not found", StatusKind.Warning, "—", "")
                : ToRow(info, label));
        }
    }

    // [PROGRAM] di machine.ini: label = Component, nilai = folder program. Proses yang berjalan dari folder itu ditampilkan.
    private static void AddMachinePrograms(MachineConfig machine, List<ProcessRow> processes, IReadOnlyList<ProcessInfo> allProcesses)
    {
        foreach (var (label, folder) in machine.Programs)
        {
            var running = MachineInspector.ProcessesUnder(allProcesses, folder);
            if (running.Count == 0)
                processes.Add(new ProcessRow("—", label, "—",
                    Directory.Exists(folder) ? "Not detected" : "Folder not found", StatusKind.Neutral, "—", folder));
            processes.AddRange(running.Select(p => new ProcessRow(p.Name, label, p.Pid.ToString(), "Running", StatusKind.Success,
                $"{p.MemoryBytes / (1024 * 1024)} MB", p.ExecutablePath)));
        }
    }

    private static ServiceRow ToRow(ServiceInfo s, string component) => new(s.Name, s.DisplayName, component, s.Status,
        s.Status.Equals("Running", StringComparison.OrdinalIgnoreCase) ? StatusKind.Success : StatusKind.Neutral,
        s.StartType, s.ExecutablePath);
}
