using System.Text.RegularExpressions;
using IonPackMan.Components;
using IonPackMan.Machine;

namespace IonPackMan.Profiles;

/// <summary>
/// Aturan kebenaran Profile (PRD R8). Profile yang masih punya masalah tidak boleh disimpan sebagai valid
/// maupun dipakai di Developer Console. Mengembalikan semua masalah sekaligus; kosong = valid.
/// </summary>
public sealed class ProfileValidator
{
    private static readonly Regex IdPattern = new(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$", RegexOptions.Compiled);
    private static readonly Regex EntryNamePattern = new(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly string[] Features = ["", Profile.WindWebUI, Profile.JnlLog];
    private static readonly string[] RegistryTypes = ["String", "DWORD"];

    private readonly ComponentStore _components;
    private readonly AllowList _allowList;

    /// <summary>Entri Registry/INI global mesin ATM; berlaku untuk semua profile bersama entri khusus profile.</summary>
    public ConfigCatalog Catalog { get; }

    public ProfileValidator(ComponentStore components, AllowList allowList, ConfigCatalog? catalog = null)
    {
        _components = components;
        _allowList = allowList;
        Catalog = catalog ?? ConfigCatalog.Empty;
    }

    /// <summary>Entri Registry/INI yang berlaku untuk profile (katalog + entri profile), lengkap dengan hasil allow-list.</summary>
    public List<ResolvedEntry> Resolve(Profile p) => ConfigResolver.Resolve(p, _allowList, Catalog);

    public static bool IsValidProfileId(string id) => IdPattern.IsMatch(id);

    public List<string> Validate(Profile p)
    {
        var problems = new List<string>();

        if (!IsValidProfileId(p.Id))
            problems.Add($"Profile Id '{p.Id}' must be 1–32 letters, digits, '-' or '_' (e.g. QRISTAP).");
        if (!ComponentRelease.IsValidVersion(p.Version))
            problems.Add($"Profile version '{p.Version}' is not valid (e.g. 1.0.0).");
        if (p.DeploymentMode != ComponentRelease.InPlaceMode)
            problems.Add($"DeploymentMode '{p.DeploymentMode}' is not supported (only '{ComponentRelease.InPlaceMode}').");

        CheckComponents(p, problems);
        CheckFeatures(p, problems);
        CheckEntries(p.RegistryEntries, p.IniEntries, problems);

        // Semua entri yang dideklarasikan (katalog + profile) harus diizinkan, termasuk yang saat ini belum berlaku (fitur mati).
        problems.AddRange(Resolve(p)
            .Where(e => e.AllowListProblem is not null)
            .Select(e => $"{e.Scope}:{e.Name}: {e.AllowListProblem}"));
        return problems;
    }

    private void CheckComponents(Profile p, List<string> problems)
    {
        if (p.Components.Count == 0)
        {
            problems.Add("Profile has no components.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in p.Components)
        {
            if (!ids.Add(c.Id))
                problems.Add($"Component '{c.Id}' is listed more than once.");
            if (!ComponentRelease.IsValidId(c.Id))
            {
                problems.Add($"Component Id '{c.Id}' is not valid.");
                continue;
            }
            if (!ComponentRelease.IsValidVersion(c.Version))
                problems.Add($"{c.Id}: version '{c.Version}' is not valid.");
            else if (!_components.ListVersions(c.Id).Contains(c.Version))
                problems.Add($"{c.Id}@{c.Version} is not saved in Components.");
        }

        foreach (var c in p.Components)
            foreach (string dep in c.DependsOn)
                if (dep == c.Id)
                    problems.Add($"{c.Id} depends on itself.");
                else if (!ids.Contains(dep))
                    problems.Add($"{c.Id} depends on '{dep}', which is not in this profile.");

        string? cycle = FindCycle(p.Components);
        if (cycle is not null)
            problems.Add($"Circular dependency: {cycle}.");
    }

    // DFS; hanya memeriksa dependency ke component yang ada (yang tidak ada sudah dilaporkan di atas).
    private static string? FindCycle(IReadOnlyList<ProfileComponent> components)
    {
        var deps = components.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First().DependsOn);
        var done = new HashSet<string>();
        var path = new List<string>();

        string? Visit(string id)
        {
            int at = path.IndexOf(id);
            if (at >= 0)
                return string.Join(" → ", path.Skip(at).Append(id));
            if (!done.Add(id))
                return null;
            path.Add(id);
            foreach (string dep in deps[id].Where(d => d != id && deps.ContainsKey(d)))
            {
                string? found = Visit(dep);
                if (found is not null)
                    return found;
            }
            path.RemoveAt(path.Count - 1);
            return null;
        }

        foreach (string id in deps.Keys)
        {
            string? found = Visit(id);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static void CheckFeatures(Profile p, List<string> problems)
    {
        if (p.JnlLogEnabled && !p.WindWebUIEnabled)
            problems.Add("JnlLog requires WindWebUI to be enabled.");
        if (p.WindWebUIEnabled && !p.Components.Any(c => c.Id == "wind"))
            problems.Add("WindWebUI is enabled but the profile has no 'wind' component.");
    }

    // Bentuk entri saja; izin (allow-list) diperiksa di Validate lewat ConfigResolver.
    public static void CheckEntries(IEnumerable<RegistryEntry> registryEntries, IEnumerable<IniEntry> iniEntries, List<string> problems)
    {
        foreach (var r in registryEntries)
        {
            string who = $"Registry:{r.Name}";
            if (!EntryNamePattern.IsMatch(r.Name)) problems.Add($"{who}: name must use letters, digits, '-' or '_'.");
            if (r.Key.Length == 0) problems.Add($"{who}: Key is required.");
            if (r.ValueName.Length == 0) problems.Add($"{who}: ValueName is required.");
            if (!RegistryTypes.Contains(r.Type)) problems.Add($"{who}: Type '{r.Type}' must be one of {string.Join(", ", RegistryTypes)}.");
            else if (r.Type == "DWORD" && !uint.TryParse(r.Value, out _)) problems.Add($"{who}: DWORD Value '{r.Value}' must be a number between 0 and 4294967295.");
            if (!Features.Contains(r.Feature)) problems.Add($"{who}: Feature '{r.Feature}' is not known.");
        }

        foreach (var e in iniEntries)
        {
            string who = $"Ini:{e.Name}";
            if (!EntryNamePattern.IsMatch(e.Name)) problems.Add($"{who}: name must use letters, digits, '-' or '_'.");
            if (e.File.Length == 0) problems.Add($"{who}: File is required.");
            if (e.Section.Length == 0) problems.Add($"{who}: Section is required.");
            if (e.Key.Length == 0) problems.Add($"{who}: Key is required.");
            if (!Features.Contains(e.Feature)) problems.Add($"{who}: Feature '{e.Feature}' is not known.");
        }
    }
}
