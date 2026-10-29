using System.IO;
using IonPackMan.Ini;

namespace IonPackMan.Profiles;

/// <summary>
/// Katalog global entri Registry/INI mesin ATM (Config\config-entries.ini). Config ini melekat di mesin, bukan di Profile,
/// jadi didefinisikan sekali di sini; Profile hanya mengaktifkan fitur (WindWebUI/JnlLog) yang menentukan entri mana yang berlaku.
/// File yang tidak ada dianggap katalog kosong. Entri di katalog tetap harus lolos allow-list (diperiksa lewat ConfigResolver).
/// </summary>
public sealed class ConfigCatalog
{
    public static ConfigCatalog Empty { get; } = new([], []);

    public IReadOnlyList<RegistryEntry> RegistryEntries { get; }
    public IReadOnlyList<IniEntry> IniEntries { get; }

    public ConfigCatalog(IReadOnlyList<RegistryEntry> registryEntries, IReadOnlyList<IniEntry> iniEntries)
    {
        RegistryEntries = registryEntries;
        IniEntries = iniEntries;
    }

    /// <summary>Katalog, atau katalog kosong beserta daftar masalah bila file ada tapi rusak.</summary>
    public static (ConfigCatalog Catalog, List<string> Errors) Load(string path)
    {
        if (!File.Exists(path))
            return (Empty, []);

        var (ini, errors) = IniSchema.TryLoad(path);
        if (ini is null)
            return (Empty, errors);

        errors.AddRange(IniSchema.CheckMeta(ini));
        errors.AddRange(ini.CheckSections("Meta", "Registry:", "Ini:"));

        var registry = new List<RegistryEntry>();
        var iniEntries = new List<IniEntry>();
        foreach (string section in ini.SectionNames)
        {
            if (Profile.TrySplit(section, "Registry:", out string regName))
                registry.Add(Profile.ReadRegistryEntry(ini, section, regName, errors));
            else if (Profile.TrySplit(section, "Ini:", out string iniName))
                iniEntries.Add(Profile.ReadIniEntry(ini, section, iniName, errors));
        }

        if (errors.Count == 0)
            ProfileValidator.CheckEntries(registry, iniEntries, errors);

        return errors.Count > 0
            ? (Empty, errors.Select(e => $"{path}: {e}").ToList())
            : (new ConfigCatalog(registry, iniEntries), []);
    }

    /// <summary>Entri yang berlaku untuk profile: semua entri katalog, dengan entri profile bernama sama menggantikannya.</summary>
    public (IReadOnlyList<RegistryEntry> Registry, IReadOnlyList<IniEntry> Ini) EntriesFor(Profile profile) =>
        (Merge(RegistryEntries, profile.RegistryEntries, e => e.Name), Merge(IniEntries, profile.IniEntries, e => e.Name));

    private static List<T> Merge<T>(IReadOnlyList<T> global, IReadOnlyList<T> own, Func<T, string> name)
    {
        var merged = global.ToList();
        foreach (var entry in own)
        {
            int at = merged.FindIndex(e => string.Equals(name(e), name(entry), StringComparison.OrdinalIgnoreCase));
            if (at >= 0) merged[at] = entry; else merged.Add(entry);
        }
        return merged;
    }
}
