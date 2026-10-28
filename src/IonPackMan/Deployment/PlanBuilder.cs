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
    bool DeployFiles, bool ApplyConfig, bool RestartService);

/// <summary>
/// Menyusun <see cref="DeploymentPlan"/> dari pilihan pengguna (PRD R10) atau menolaknya dengan daftar alasan.
/// Hanya MEMBACA mesin (status service, nilai Registry/INI, isi target); tidak mengubah apa pun.
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
    private readonly Guards _guards;

    public PlanBuilder(MachineConfig machine, AllowList allowList, ComponentStore components, ProfileStore profiles,
        ProfileValidator validator, BuildImporter builds, IRegistryAccess registry, IServiceControl services, Guards guards)
    {
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

        if (request.RestartService && release.ServiceName.Length == 0)
            errors.Add($"{release.Id} has no Windows service name, so it cannot be restarted.");

        var serviceState = ServiceState.NotFound;
        if (release.ServiceName.Length > 0)
        {
            serviceState = _services.GetState(release.ServiceName);
            if (request.RestartService && serviceState == ServiceState.NotFound)
                errors.Add($"Service '{release.ServiceName}' was not found on this machine.");
        }

        var files = request.DeployFiles ? PlanFiles(request, release, errors) : [];
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
            BuildFilesDirectory = Paths.BuildFilesDir(_machine.LocalCacheDirectory, release.Id, release.ReleaseVersion),
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

    private List<PlannedFile> PlanFiles(PlanRequest request, ComponentRelease release, List<string> errors)
    {
        var files = new List<PlannedFile>();

        if (!_builds.Exists(release.Id, release.ReleaseVersion))
        {
            errors.Add($"No imported build for {release.Id}@{release.ReleaseVersion}. Import it in Components first.");
            return files;
        }

        var (checksums, checksumErrors) = _builds.ReadChecksums(release.Id, release.ReleaseVersion);
        if (checksums is null)
        {
            errors.AddRange(checksumErrors);
            return files;
        }

        string buildDir = Paths.BuildFilesDir(_machine.LocalCacheDirectory, release.Id, release.ReleaseVersion);
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

        // Data diambil dari entri Profile; ConfigResolver hanya dipakai untuk keputusan allow-list (satu sumber aturan).
        var resolved = ConfigResolver.Resolve(profile, _allowList).ToDictionary(e => (e.Scope, e.Name));
        var activeRegistry = profile.RegistryEntries.Where(r => profile.IsFeatureEnabled(r.Feature)).ToList();
        var activeIni = profile.IniEntries.Where(e => profile.IsFeatureEnabled(e.Feature)).ToList();

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
