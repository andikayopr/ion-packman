using IonPackMan.Components;
using IonPackMan.Ini;

namespace IonPackMan.Profiles;

public sealed record ProfileComponent(string Id, string Version, IReadOnlyList<string> DependsOn);

/// <param name="Feature">Kosong = selalu berlaku; "WindWebUI" / "JnlLog" = hanya bila fitur itu aktif.</param>
public sealed record RegistryEntry(string Name, string Key, string ValueName, string Type, string Value, string Feature);

public sealed record IniEntry(string Name, string File, string Section, string Key, string Value, string Feature);

/// <summary>
/// Komposisi sebuah Profile (PRD R6–R7): versi component yang exact, dependency, fitur, dan entri Registry/INI.
/// Disimpan sebagai Profiles\&lt;Id&gt;.ini. Satu mesin memakai satu Profile.
/// <see cref="RegistryEntries"/>/<see cref="IniEntries"/> adalah entri KHUSUS profile ini (opsional). Config yang melekat di
/// mesin ATM didefinisikan sekali di <see cref="ConfigCatalog"/>; gabungan keduanya: <see cref="ConfigCatalog.EntriesFor"/>.
/// Aturan kebenaran (R8) ada di <see cref="ProfileValidator"/>; di sini hanya membaca dan menulis.
/// </summary>
public sealed record Profile
{
    public const string WindWebUI = "WindWebUI";
    public const string JnlLog = "JnlLog";

    public required string Id { get; init; }
    public string DisplayName { get; init; } = "";
    public required string Version { get; init; }
    public string DeploymentMode { get; init; } = ComponentRelease.InPlaceMode;
    public bool WindWebUIEnabled { get; init; }
    public bool JnlLogEnabled { get; init; }
    public IReadOnlyList<ProfileComponent> Components { get; init; } = [];
    public IReadOnlyList<RegistryEntry> RegistryEntries { get; init; } = [];
    public IReadOnlyList<IniEntry> IniEntries { get; init; } = [];

    /// <summary>Apakah entri dengan fitur ini berlaku. JnlLog hanya berlaku bila WindWebUI juga aktif (R7).</summary>
    public bool IsFeatureEnabled(string feature) => feature switch
    {
        "" => true,
        WindWebUI => WindWebUIEnabled,
        JnlLog => JnlLogEnabled && WindWebUIEnabled,
        _ => false,
    };

    /// <summary>Mengubah WindWebUI; mematikannya otomatis mematikan JnlLog juga (R7).</summary>
    public Profile WithWindWebUI(bool enabled) =>
        this with { WindWebUIEnabled = enabled, JnlLogEnabled = enabled && JnlLogEnabled };

    /// <summary>
    /// Membaca file INI Profile: hanya soal bentuk file (section/key/tipe). Apakah isinya benar
    /// (dependency, fitur, versi tersimpan) diperiksa <see cref="ProfileValidator"/>.
    /// </summary>
    public static (Profile? Profile, List<string> Errors) LoadFile(string path)
    {
        var (ini, errors) = IniSchema.TryLoad(path);
        if (ini is null)
            return (null, errors);

        errors.AddRange(IniSchema.CheckMeta(ini));
        errors.AddRange(ini.CheckSections("Meta", "Profile", "Features", "Component:", "Registry:", "Ini:"));
        errors.AddRange(ini.CheckKeys("Profile", required: ["Id", "Version"], optional: ["DisplayName", "DeploymentMode"]));
        if (ini.HasSection("Features"))
            errors.AddRange(ini.CheckKeys("Features", required: [], optional: [WindWebUI, JnlLog]));

        bool webUi = ReadBool(ini, WindWebUI, errors);
        bool jnlLog = ReadBool(ini, JnlLog, errors);

        var components = new List<ProfileComponent>();
        var registry = new List<RegistryEntry>();
        var iniEntries = new List<IniEntry>();

        foreach (string section in ini.SectionNames)
        {
            if (TrySplit(section, "Component:", out string id))
            {
                errors.AddRange(ini.CheckKeys(section, required: ["Version"], optional: ["DependsOn"]));
                components.Add(new ProfileComponent(id, ini.Get(section, "Version") ?? "", SplitList(ini.Get(section, "DependsOn"))));
            }
            else if (TrySplit(section, "Registry:", out string regName))
                registry.Add(ReadRegistryEntry(ini, section, regName, errors));
            else if (TrySplit(section, "Ini:", out string iniName))
                iniEntries.Add(ReadIniEntry(ini, section, iniName, errors));
        }

        if (errors.Count > 0)
            return (null, errors.Select(e => $"{path}: {e}").ToList());

        return (new Profile
        {
            Id = ini.Get("Profile", "Id")!,
            DisplayName = ini.Get("Profile", "DisplayName") ?? "",
            Version = ini.Get("Profile", "Version")!,
            DeploymentMode = ini.Get("Profile", "DeploymentMode") ?? ComponentRelease.InPlaceMode,
            WindWebUIEnabled = webUi,
            JnlLogEnabled = jnlLog,
            Components = components,
            RegistryEntries = registry,
            IniEntries = iniEntries,
        }, []);
    }

    /// <summary>Isi file INI (juga untuk pratinjau). Melempar ArgumentException bila ada nilai yang tidak bisa disimpan.</summary>
    public IniFile ToIni()
    {
        var ini = new IniFile();
        ini.Set("Meta", "SchemaVersion", IniSchema.CurrentVersion);
        ini.Set("Profile", "Id", Id);
        ini.Set("Profile", "DisplayName", DisplayName);
        ini.Set("Profile", "Version", Version);
        ini.Set("Profile", "DeploymentMode", DeploymentMode);
        ini.Set("Features", WindWebUI, WindWebUIEnabled ? "true" : "false");
        ini.Set("Features", JnlLog, JnlLogEnabled ? "true" : "false");

        foreach (var c in Components)
        {
            ini.Set($"Component:{c.Id}", "Version", c.Version);
            ini.Set($"Component:{c.Id}", "DependsOn", string.Join(",", c.DependsOn));
        }
        foreach (var r in RegistryEntries)
        {
            string s = $"Registry:{r.Name}";
            ini.Set(s, "Key", r.Key);
            ini.Set(s, "ValueName", r.ValueName);
            ini.Set(s, "Type", r.Type);
            ini.Set(s, "Value", r.Value);
            ini.Set(s, "Feature", r.Feature);
        }
        foreach (var e in IniEntries)
        {
            string s = $"Ini:{e.Name}";
            ini.Set(s, "File", e.File);
            ini.Set(s, "Section", e.Section);
            ini.Set(s, "Key", e.Key);
            ini.Set(s, "Value", e.Value);
            ini.Set(s, "Feature", e.Feature);
        }
        return ini;
    }

    // Dipakai juga oleh ConfigCatalog: entri di katalog global punya bentuk yang sama dengan entri di file Profile.
    internal static RegistryEntry ReadRegistryEntry(IniFile ini, string section, string name, List<string> errors)
    {
        errors.AddRange(ini.CheckKeys(section, required: ["Key", "ValueName", "Type"], optional: ["Value", "Feature"]));
        return new RegistryEntry(name, ini.Get(section, "Key") ?? "", ini.Get(section, "ValueName") ?? "",
            ini.Get(section, "Type") ?? "", ini.Get(section, "Value") ?? "", ini.Get(section, "Feature") ?? "");
    }

    internal static IniEntry ReadIniEntry(IniFile ini, string section, string name, List<string> errors)
    {
        errors.AddRange(ini.CheckKeys(section, required: ["File", "Section", "Key"], optional: ["Value", "Feature"]));
        return new IniEntry(name, ini.Get(section, "File") ?? "", ini.Get(section, "Section") ?? "",
            ini.Get(section, "Key") ?? "", ini.Get(section, "Value") ?? "", ini.Get(section, "Feature") ?? "");
    }

    internal static bool TrySplit(string section, string prefix, out string name)
    {
        bool match = section.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        name = match ? section[prefix.Length..] : "";
        return match;
    }

    private static List<string> SplitList(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool ReadBool(IniFile ini, string key, List<string> errors)
    {
        string? text = ini.Get("Features", key);
        if (text is null || text.Length == 0)
            return false;
        if (bool.TryParse(text, out bool value))
            return value;
        errors.Add($"[Features] {key} must be true or false (found '{text}').");
        return false;
    }
}
