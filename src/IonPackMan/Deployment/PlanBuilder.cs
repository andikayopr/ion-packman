using System.IO;
using System.Security.Cryptography;
using IonPackMan.Builds;
using IonPackMan.Components;
using IonPackMan.History;
using IonPackMan.Machine;
using IonPackMan.Profiles;

namespace IonPackMan.Deployment;

/// <summary>Pilihan pengguna di Developer Console (PRD R9).</summary>
public sealed record PlanRequest(
    string ProfileId, string ComponentId, string ReleaseVersion,
    bool DeployFiles, bool ApplyConfig, bool RestartService, bool SnapshotFolder = false);

/// <summary>
/// Menyusun <see cref="DeploymentPlan"/> dari pilihan pengguna (PRD R10) atau menolaknya dengan daftar alasan.
/// Hanya MEMBACA mesin (status service, program yang berjalan, nilai Registry/INI, isi target); tidak mengubah apa pun.
/// Pemeriksaan keselamatan (environment, path, allow-list, hash, disk, backup tertunda) dilakukan <see cref="Guards"/>.
/// </summary>
public sealed class PlanBuilder
{
    private readonly MachineConfig _machine;
    private readonly AllowList _allowList;
    private readonly ComponentStore _components;
    private readonly ProfileStore _profiles;
    private readonly ProfileValidator _validator;
    private readonly BuildImporter _builds;
    private readonly IRegistryAccess _registry;
    private readonly IServiceControl _services;
    private readonly IProcessControl _processes;
    private readonly Guards _guards;
    private readonly ClientConfig _clients;

    /// <param name="clients">Client dan override-nya (ADR-006); null = tidak ada Client, semua build default.</param>
    public PlanBuilder(MachineConfig machine, AllowList allowList, ComponentStore components, ProfileStore profiles,
        ProfileValidator validator, BuildImporter builds, IRegistryAccess registry, IServiceControl services,
        IProcessControl processes, Guards guards, ClientConfig? clients = null)
    {
        _clients = clients ?? ClientConfig.None;
        _processes = processes;
        _machine = machine;
        _allowList = allowList;
        _components = components;
        _profiles = profiles;
        _validator = validator;
        _builds = builds;
        _registry = registry;
        _services = services;
        _guards = guards;
    }

    public (DeploymentPlan? Plan, List<string> Errors) Build(PlanRequest request)
    {
        var errors = new List<string>();

        if (!request.DeployFiles && !request.ApplyConfig && !request.RestartService)
            errors.Add("Select at least one operation.");

        string? cacheProblem = _machine.CheckCacheDirectory();
        if (cacheProblem is not null)
            errors.Add(cacheProblem);

        var (profile, release) = LoadProfileAndComponent(request, errors);
        if (profile is null || release is null)
            return (null, errors);

        // Component dengan service: Stop/Start service. Tanpa service: program biasa yang berjalan dari folder target.
        var serviceState = ServiceState.NotFound;
        string serviceExe = "";
        IReadOnlyList<string> programs = [];
        if (release.ServiceName.Length > 0)
        {
            serviceState = _services.GetState(release.ServiceName);
            if (request.RestartService && serviceState == ServiceState.NotFound)
                errors.Add($"Service '{release.ServiceName}' was not found on this machine.");

            // File yang dikirim ke folder lain tidak akan sampai ke program yang dijalankan service.
            if (serviceState != ServiceState.NotFound)
                serviceExe = _services.GetExecutablePath(release.ServiceName);
            if (request.DeployFiles && serviceExe.Length > 0 && !MachineInspector.IsUnder(serviceExe, release.TargetDirectory))
                errors.Add($"Service '{release.ServiceName}' runs '{serviceExe}', which is outside the target directory '{release.TargetDirectory}'. " +
                           "Deployed files would not reach the running program. Fix the target directory in Components.");
        }
        else if (request.DeployFiles || request.RestartService)
        {
            programs = _processes.FindExecutablesUnder(release.TargetDirectory);
            if (request.RestartService && programs.Count == 0)
                errors.Add($"{release.Id} has no Windows service and no program is running from '{release.TargetDirectory}', so there is nothing to restart.");
        }

        // Snapshot hanya bermakna bila file akan diganti. Folder target yang belum ada (instalasi pertama) tidak punya isi untuk disalin.
        bool snapshot = request.SnapshotFolder && request.DeployFiles && Directory.Exists(release.TargetDirectory);
        long snapshotBytes = 0;
        if (snapshot)
        {
            try { snapshotBytes = FolderCopy.SizeOf(release.TargetDirectory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Cannot measure '{release.TargetDirectory}' for the folder snapshot: {ex.Message}");
            }
        }

        // Build yang boleh dipakai ditentukan Client mesin dan component (ADR-006 §4); tidak ada fallback ke default.
        string buildClient = request.DeployFiles ? _clients.BuildClientFor(_machine.Client, release.Id) : "";
        var files = request.DeployFiles ? PlanFiles(release, buildClient, errors) : [];
        var buildInfo = request.DeployFiles && errors.Count == 0 ? _builds.Load(release.Id, release.ReleaseVersion, buildClient).Build : null;
        var (registry, ini) = request.ApplyConfig ? PlanConfig(profile, errors) : ([], []);

        if (errors.Count > 0)
            return (null, errors);

        var plan = new DeploymentPlan
        {
            ExecId = HistoryRecord.NewExecId(release.Id),
            ProfileId = profile.Id,
            ComponentId = release.Id,
            ReleaseVersion = release.ReleaseVersion,
            TargetDirectory = release.TargetDirectory,
            ServiceName = release.ServiceName,
            DeployFiles = request.DeployFiles,
            ApplyConfig = request.ApplyConfig,
            RestartService = request.RestartService,
            ServiceStateAtPlan = serviceState,
            SnapshotFolder = snapshot,
            SnapshotBytes = snapshotBytes,
            ServiceExecutable = serviceExe,
            ProgramsAtPlan = programs,
            BuildFilesDirectory = Paths.BuildFilesDir(_machine.LocalCacheDirectory, release.Id, release.ReleaseVersion, buildClient),
            BuildClient = buildClient,
            BuildGitBranch = buildInfo?.GitBranch ?? "",
            BuildGitCommit = buildInfo?.GitCommit ?? "",
            Files = files,
            RegistryChanges = registry,
            IniChanges = ini,
        };

        var guardProblems = _guards.Check(plan);
        return guardProblems.Count > 0 ? (null, guardProblems) : (plan, []);
    }

    private (Profile?, ComponentRelease?) LoadProfileAndComponent(PlanRequest request, List<string> errors)
    {
        var (profile, profileErrors) = _profiles.Load(request.ProfileId);
        if (profile is null)
        {
            errors.AddRange(profileErrors.Count > 0 ? profileErrors : [$"Profile '{request.ProfileId}' could not be loaded."]);
            return (null, null);
        }

        var profileProblems = _validator.Validate(profile);
        if (profileProblems.Count > 0)
        {
            errors.Add($"Profile '{profile.Id}' is not valid: {profileProblems[0]}" + (profileProblems.Count > 1 ? $" (+{profileProblems.Count - 1} more)" : ""));
            return (null, null);
        }

        if (!profile.Components.Any(c => c.Id == request.ComponentId))
        {
            errors.Add($"Component '{request.ComponentId}' is not part of profile '{profile.Id}'.");
            return (null, null);
        }

        var (release, releaseErrors) = _components.Load(request.ComponentId, request.ReleaseVersion);
        if (release is null)
        {
            errors.AddRange(releaseErrors);
            return (null, null);
        }

        // Target harus di dalam base directory yang diizinkan; service harus terdaftar.
        errors.AddRange(release.Validate(_allowList));
        return (profile, release);
    }

    private List<PlannedFile> PlanFiles(ComponentRelease release, string buildClient, List<string> errors)
    {
        var files = new List<PlannedFile>();

        if (!_builds.Exists(release.Id, release.ReleaseVersion, buildClient))
        {
            string label = BuildImporter.Label(release.Id, release.ReleaseVersion, buildClient);
            errors.Add(buildClient.Length == 0
                ? $"No imported build for {label}. Import it in Components first."
                : $"No imported build for {label}. Client {buildClient} uses its own branch of {release.Id}, so a default build is not accepted; import it in Components as a {buildClient} build.");
            return files;
        }

        var (checksums, checksumErrors) = _builds.ReadChecksums(release.Id, release.ReleaseVersion, buildClient);
        if (checksums is null)
        {
            errors.AddRange(checksumErrors);
            return files;
        }

        string buildDir = Paths.BuildFilesDir(_machine.LocalCacheDirectory, release.Id, release.ReleaseVersion, buildClient);
        string targetRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(release.TargetDirectory));

        foreach (var (relative, hash) in checksums.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            string windowsRelative = relative.Replace('/', '\\');
            string target = Path.GetFullPath(Path.Combine(targetRoot, windowsRelative));
            if (!target.StartsWith(targetRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Build file '{relative}' would be written outside the target directory.");
                continue;
            }

            string source = Path.Combine(buildDir, windowsRelative);
            long size = File.Exists(source) ? new FileInfo(source).Length : 0;
            files.Add(new PlannedFile(relative, source, target, size, hash, ChangeOf(target, hash, errors)));
        }
        return files;
    }

    // File di target yang tidak ada di build dibiarkan (tidak pernah dihapus): hanya file build yang dibandingkan.
    private static FileChange ChangeOf(string target, string buildHash, List<string> errors)
    {
        if (!File.Exists(target))
            return FileChange.New;
        try
        {
            using var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA256.HashData(stream)).Equals(buildHash, StringComparison.OrdinalIgnoreCase)
                ? FileChange.Same : FileChange.Replace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Cannot read target file '{target}' to compare it: {ex.Message}");
            return FileChange.Replace;
        }
    }

    private (List<PlannedRegistryChange>, List<PlannedIniChange>) PlanConfig(Profile profile, List<string> errors)
    {
        var registry = new List<PlannedRegistryChange>();
        var ini = new List<PlannedIniChange>();

        // Data diambil dari katalog global mesin + entri khusus Profile; ConfigResolver hanya dipakai untuk keputusan allow-list (satu sumber aturan).
        var resolved = _validator.Resolve(profile).ToDictionary(e => (e.Scope, e.Name));
        var (allRegistry, allIni) = _validator.Catalog.EntriesFor(profile);
        var activeRegistry = allRegistry.Where(r => profile.IsFeatureEnabled(r.Feature)).ToList();
        var activeIni = allIni.Where(e => profile.IsFeatureEnabled(e.Feature)).ToList();

        if (activeRegistry.Count + activeIni.Count == 0)
            errors.Add($"Profile '{profile.Id}' has no enabled Registry/INI changes to apply.");

        foreach (var r in activeRegistry)
        {
            if (resolved[("Registry", r.Name)].AllowListProblem is { } problem)
            {
                errors.Add($"Registry:{r.Name}: {problem}");
                continue;
            }
            try
            {
                var type = r.Type.Equals("DWORD", StringComparison.OrdinalIgnoreCase) ? RegistryValueType.DWord : RegistryValueType.String;
                registry.Add(new PlannedRegistryChange(r.Name, r.Key, r.ValueName, type, r.Value, _registry.Read(r.Key, r.ValueName)?.Value));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                errors.Add($"Registry:{r.Name}: cannot read the current value ({ex.Message})");
            }
        }

        foreach (var e in activeIni)
        {
            if (resolved[("INI", e.Name)].AllowListProblem is { } problem)
            {
                errors.Add($"Ini:{e.Name}: {problem}");
                continue;
            }
            try
            {
                var current = AppIniEditor.Read(e.File, e.Section, e.Key);
                ini.Add(new PlannedIniChange(e.Name, e.File, e.Section, e.Key, e.Value, current.Exists ? current.Value : null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                errors.Add($"Ini:{e.Name}: cannot read the current value ({ex.Message})");
            }
        }
        return (registry, ini);
    }
}
