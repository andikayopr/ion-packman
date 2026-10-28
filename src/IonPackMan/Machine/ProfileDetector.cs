using System.IO;
using IonPackMan.Components;
using IonPackMan.Profiles;

namespace IonPackMan.Machine;

/// <param name="ProfileId">Id Profile yang terdeteksi, atau null (= "Unknown") bila tidak pasti.</param>
public sealed record DetectionResult(string? ProfileId, string Reason);

/// <summary>
/// Menentukan Profile mesin ini (R1, D3): pilihan manual di machine.ini, atau mencocokkan folder/service
/// setiap component Profile. Bila tidak pasti hasilnya Unknown + alasan; tidak pernah menebak atau memakai nilai contoh.
/// </summary>
public sealed class ProfileDetector
{
    private readonly ComponentStore _components;
    private readonly ProfileStore _profiles;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, bool> _serviceExists;

    public ProfileDetector(ComponentStore components, ProfileStore profiles,
        Func<string, bool>? directoryExists = null, Func<string, bool>? serviceExists = null)
    {
        _components = components;
        _profiles = profiles;
        _directoryExists = directoryExists ?? Directory.Exists;
        var inspector = new MachineInspector();
        _serviceExists = serviceExists ?? (name => inspector.GetService(name) is not null);
    }

    public DetectionResult Detect(MachineConfig machine)
    {
        var ids = _profiles.ListIds();

        if (machine.Profile.Length > 0)
        {
            string? chosen = ids.FirstOrDefault(i => i.Equals(machine.Profile, StringComparison.OrdinalIgnoreCase));
            return chosen is null
                ? Unknown($"Profile '{machine.Profile}' set in machine.ini does not exist in the release folder.")
                : new DetectionResult(chosen, "Selected in machine.ini");
        }

        if (ids.Count == 0)
            return Unknown("No profiles were found in the release folder.");

        var matches = new List<string>();
        var unreadable = new List<string>();
        (string Id, int Matched, int Total, List<string> Missing)? nearest = null;

        foreach (string id in ids)
        {
            var (profile, errors) = _profiles.Load(id);
            if (profile is null)
            {
                unreadable.Add($"{id} ({string.Join(" ", errors)})");
                continue;
            }

            var missing = new List<string>();
            int matched = 0;
            foreach (var pc in profile.Components)
            {
                var reasons = CheckComponent(pc);
                if (reasons.Count == 0) matched++;
                missing.AddRange(reasons);
            }

            if (missing.Count == 0 && profile.Components.Count > 0)
                matches.Add(id);
            else if (nearest is null || (double)matched / profile.Components.Count > (double)nearest.Value.Matched / nearest.Value.Total)
                nearest = (id, matched, profile.Components.Count, missing);
        }

        // Profile yang rusak bisa saja yang sebenarnya cocok, jadi jangan menebak.
        if (unreadable.Count > 0)
            return Unknown($"Cannot detect the profile because some profiles could not be read: {string.Join("; ", unreadable)}. Fix them or set Profile in machine.ini.");

        if (matches.Count == 1)
            return new DetectionResult(matches[0], "Detected from installed folders and services");

        if (matches.Count > 1)
            return Unknown($"More than one profile matches this machine ({string.Join(", ", matches)}). Set Profile in machine.ini to choose one.");

        var n = nearest!.Value;
        return Unknown($"No profile matches this machine. Closest is {n.Id} ({n.Matched} of {n.Total} components found); missing: {string.Join("; ", n.Missing)}.");
    }

    /// <summary>Alasan component belum cocok di mesin ini; kosong = cocok.</summary>
    private List<string> CheckComponent(ProfileComponent pc)
    {
        var (release, _) = _components.Load(pc.Id, pc.Version);
        if (release is null)
            return [$"{pc.Id}@{pc.Version} cannot be loaded from the release folder"];

        var missing = new List<string>();
        if (!_directoryExists(release.TargetDirectory))
            missing.Add($"{pc.Id}: folder {release.TargetDirectory} not found");
        if (release.ServiceName.Length > 0 && !_serviceExists(release.ServiceName))
            missing.Add($"{pc.Id}: service {release.ServiceName} not found");
        return missing;
    }

    private static DetectionResult Unknown(string reason) => new(null, reason);
}
