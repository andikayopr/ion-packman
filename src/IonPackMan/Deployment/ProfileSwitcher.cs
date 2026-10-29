using System.IO;
using IonPackMan.Components;
using IonPackMan.History;
using IonPackMan.Machine;
using IonPackMan.Profiles;

namespace IonPackMan.Deployment;

/// <summary>Service milik component Profile lama (yang tidak ada di Profile baru) yang sedang Running dan akan dihentikan.</summary>
public sealed record ServiceToStop(string ComponentId, string ServiceName);

/// <param name="PreviousProfileId">Profile aktif sebelumnya; null = tidak diketahui (tidak ada service yang dihentikan).</param>
/// <param name="Steps">Satu plan per component Profile baru, urut menurut DependsOn.</param>
public sealed record SwitchPlan(
    string ProfileId, string? PreviousProfileId, IReadOnlyList<DeploymentPlan> Steps, IReadOnlyList<ServiceToStop> ServicesToStop);

/// <summary>
/// Pindah ke sebuah Profile (PRD "satu mesin = satu Profile aktif"): semua component Profile di-deploy sekaligus dengan build yang
/// dikunci Profile (file yang sama tidak disalin), config Profile diterapkan, service dinyalakan; lalu service milik Profile lama
/// yang tidak dipakai Profile baru dihentikan, dan Profile baru ditandai aktif dengan menulis <c>Profile=</c> di machine.ini.
/// Tiap component tetap satu eksekusi biasa lewat <see cref="PlanBuilder"/> dan <see cref="Deployer"/> (Guards, backup, restore otomatis).
/// Penanda aktif ditulis paling akhir: bila ada langkah yang gagal, Profile lama tetap yang tercatat.
/// </summary>
public sealed class ProfileSwitcher
{
    private readonly MachineConfig _machine;
    private readonly AllowList _allowList;
    private readonly ProfileStore _profiles;
    private readonly ProfileValidator _validator;
    private readonly ComponentStore _components;
    private readonly ProfileDetector _detector;
    private readonly IServiceControl _services;
    private readonly PlanBuilder _planner;
    private readonly Deployer _deployer;
    private readonly ExecutionLock _gate;
    private readonly TimeSpan _serviceTimeout;

    public ProfileSwitcher(MachineConfig machine, AllowList allowList, ProfileStore profiles, ProfileValidator validator,
        ComponentStore components, ProfileDetector detector, IServiceControl services, PlanBuilder planner, Deployer deployer,
        ExecutionLock gate, TimeSpan? serviceTimeout = null)
    {
        _gate = gate;
        _machine = machine;
        _allowList = allowList;
        _profiles = profiles;
        _validator = validator;
        _components = components;
        _detector = detector;
        _services = services;
        _planner = planner;
        _deployer = deployer;
        _serviceTimeout = serviceTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>Profile aktif saat ini (machine.ini, atau deteksi bila kosong); null = tidak diketahui.</summary>
    public string? ActiveProfileId() => _detector.Detect(_machine).ProfileId;

    /// <summary>Hanya MEMBACA mesin. Satu error pada component mana pun menggagalkan seluruh rencana (tidak ada yang setengah jalan).</summary>
    public (SwitchPlan? Plan, List<string> Errors) Build(string profileId)
    {
        var (profile, loadErrors) = _profiles.Load(profileId);
        if (profile is null)
            return (null, loadErrors.Count > 0 ? loadErrors : [$"Profile '{profileId}' could not be loaded."]);

        var problems = _validator.Validate(profile);
        if (problems.Count > 0)
            return (null, [$"Profile '{profile.Id}' is not valid: {problems[0]}" + (problems.Count > 1 ? $" (+{problems.Count - 1} more)" : "")]);
        if (profile.Components.Count == 0)
            return (null, [$"Profile '{profile.Id}' has no components to deploy."]);

        var (registry, ini) = _validator.Catalog.EntriesFor(profile);
        bool hasConfig = registry.Any(r => profile.IsFeatureEnabled(r.Feature)) || ini.Any(e => profile.IsFeatureEnabled(e.Feature));

        var errors = new List<string>();
        var steps = new List<DeploymentPlan>();
        var newServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pc in InDependencyOrder(profile.Components))
        {
            var (release, releaseErrors) = _components.Load(pc.Id, pc.Version);
            if (release is null)
            {
                errors.AddRange(releaseErrors);
                continue;
            }
            if (release.ServiceName.Length > 0)
                newServices.Add(release.ServiceName);

            // Service yang belum terpasang (instalasi pertama) tidak bisa dinyalakan; selebihnya service dinyalakan setelah deploy.
            bool start = release.ServiceName.Length > 0 && _services.GetState(release.ServiceName) != ServiceState.NotFound;
            // Config milik Profile, bukan component: diterapkan sekali, pada component pertama, sebelum component berikutnya dinyalakan.
            bool applyConfig = hasConfig && steps.Count == 0;

            var (plan, planErrors) = _planner.Build(new PlanRequest(profile.Id, pc.Id, pc.Version,
                DeployFiles: true, ApplyConfig: applyConfig, RestartService: start));
            if (plan is null)
                errors.AddRange(planErrors.Select(e => $"{pc.Id}: {e}"));
            else
                steps.Add(plan);
        }

        string? previous = _detector.Detect(_machine).ProfileId;
        var toStop = errors.Count == 0 ? ServicesOfPreviousProfile(previous, profile, newServices, errors) : [];

        return errors.Count > 0 ? (null, errors) : (new SwitchPlan(profile.Id, previous, steps, toStop), []);
    }

    // Hanya component Profile lama yang id-nya tidak ada di Profile baru, dan service-nya tidak dipakai component Profile baru.
    private List<ServiceToStop> ServicesOfPreviousProfile(string? previousId, Profile next, HashSet<string> keep, List<string> errors)
    {
        var result = new List<ServiceToStop>();
        if (previousId is null || previousId.Equals(next.Id, StringComparison.OrdinalIgnoreCase))
            return result;

        var (previous, _) = _profiles.Load(previousId);
        foreach (var pc in previous?.Components.Where(c => next.Components.All(n => n.Id != c.Id)) ?? [])
        {
            var (release, _) = _components.Load(pc.Id, pc.Version);
            if (release is null || release.ServiceName.Length == 0 || keep.Contains(release.ServiceName))
                continue;
            if (_services.GetState(release.ServiceName) != ServiceState.Running)
                continue;

            if (release.Validate(_allowList) is { Count: > 0 } invalid)
                errors.Add($"{pc.Id}: cannot stop service '{release.ServiceName}': {invalid[0]}");
            else
                result.Add(new ServiceToStop(pc.Id, release.ServiceName));
        }
        return result;
    }

    /// <summary>
    /// Menjalankan langkah satu per satu, lalu menghentikan service Profile lama dan menandai Profile baru aktif.
    /// Berhenti di langkah pertama yang gagal (yang gagal sudah di-restore Deployer; yang sudah berhasil dibiarkan).
    /// Bila deploy berhasil tetapi penghentian service atau penulisan machine.ini gagal, hasilnya tetap Succeeded
    /// dengan <see cref="DeploymentResult.Warnings"/> yang menyebut Profile belum aktif.
    /// </summary>
    public DeploymentResult Run(SwitchPlan plan, IProgress<DeployStep>? progress = null)
    {
        if (!_gate.TryEnter())
            return new DeploymentResult(HistoryOutcome.Rejected, "", ["Another execution is already running. Wait until it finishes."], [], "", []);
        try
        {
            return RunLocked(plan, progress);
        }
        finally
        {
            _gate.Exit();
        }
    }

    private DeploymentResult RunLocked(SwitchPlan plan, IProgress<DeployStep>? progress)
    {
        DeploymentResult? last = null;
        for (int i = 0; i < plan.Steps.Count; i++)
        {
            last = _deployer.Run(plan.Steps[i], progress);
            if (last.Succeeded)
                continue;

            var errors = last.Errors.Select(e => $"{plan.Steps[i].ComponentId}: {e}").ToList();
            var notRun = plan.Steps.Skip(i + 1).Select(s => s.ComponentId).ToList();
            if (notRun.Count > 0)
                errors.Add($"Not run: {string.Join(", ", notRun)}. Profile '{plan.ProfileId}' was not activated.");
            return last with { Errors = errors };
        }
        if (last is null)
            return new DeploymentResult(HistoryOutcome.Rejected, "", ["Nothing to switch."], [], "", []);

        var warnings = new List<string>();
        foreach (var (componentId, serviceName) in plan.ServicesToStop)
        {
            try
            {
                _services.Stop(serviceName, _serviceTimeout);
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                warnings.Add($"Service '{serviceName}' of {componentId} could not be stopped: {ex.Message}");
            }
        }
        if (warnings.Count > 0)
        {
            warnings.Add($"Profile '{plan.ProfileId}' was NOT activated. Stop the service yourself, then switch again.");
            return last with { Warnings = warnings };
        }

        try
        {
            AppIniEditor.Write(_machine.SourcePath, "Machine", "Profile", plan.ProfileId);
            _machine.Profile = plan.ProfileId;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return last with { Warnings = [$"The components were deployed, but Profile '{plan.ProfileId}' was NOT marked active in machine.ini: {ex.Message}"] };
        }
        return last;
    }

    // Stabil: urutan di profile dipertahankan sejauh dependency terpenuhi. Dependency di luar profile diabaikan (validator yang menolaknya).
    private static List<ProfileComponent> InDependencyOrder(IReadOnlyList<ProfileComponent> components)
    {
        var ordered = new List<ProfileComponent>();
        var pending = components.ToList();
        var ids = components.Select(c => c.Id).ToHashSet();
        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(c => c.DependsOn.All(d => !ids.Contains(d) || ordered.Any(o => o.Id == d))) ?? pending[0];
            ordered.Add(next);
            pending.Remove(next);
        }
        return ordered;
    }
}
