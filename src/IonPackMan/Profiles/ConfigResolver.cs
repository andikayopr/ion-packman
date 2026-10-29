using IonPackMan.Machine;

namespace IonPackMan.Profiles;

public enum EntryState { Enabled, NotApplied }

/// <summary>Satu baris tabel "Resolved Registry &amp; INI Changes" (PRD R7).</summary>
/// <param name="Scope">"Registry" atau "INI".</param>
/// <param name="Target">Key Registry, atau path file INI.</param>
/// <param name="Entry">Nama value Registry, atau "[Section] Key" untuk INI.</param>
/// <param name="Type">Tipe value Registry; kosong untuk INI.</param>
/// <param name="AllowListProblem">Alasan entri ini ditolak allow-list; null = diizinkan.</param>
public sealed record ResolvedEntry(
    string Scope, string Name, string Target, string Entry, string Type, string Value,
    string Feature, EntryState State, string? AllowListProblem);

/// <summary>
/// Mengubah entri Registry/INI sebuah Profile menjadi daftar yang siap ditampilkan dan dieksekusi:
/// mana yang berlaku (sesuai fitur aktif) dan mana yang ditolak allow-list.
/// Tidak melihat versi component, jadi hasilnya sama untuk versi WIND berapa pun (R7).
/// </summary>
public static class ConfigResolver
{
    /// <param name="catalog">Katalog global mesin; null = hanya entri khusus profile.</param>
    public static List<ResolvedEntry> Resolve(Profile profile, AllowList allowList, ConfigCatalog? catalog = null)
    {
        var entries = new List<ResolvedEntry>();
        var (registryEntries, iniEntries) = (catalog ?? ConfigCatalog.Empty).EntriesFor(profile);

        foreach (var r in registryEntries)
            entries.Add(new ResolvedEntry("Registry", r.Name, r.Key, r.ValueName, r.Type, r.Value, r.Feature,
                StateOf(profile, r.Feature),
                allowList.IsRegistryKeyAllowed(r.Key) ? null : $"Registry key '{r.Key}' is not in the allow-list."));

        foreach (var e in iniEntries)
            entries.Add(new ResolvedEntry("INI", e.Name, e.File, $"[{e.Section}] {e.Key}", "", e.Value, e.Feature,
                StateOf(profile, e.Feature),
                allowList.IsIniFileAllowed(e.File) ? null : $"INI file '{e.File}' is not in the allow-list."));

        return entries;
    }

    private static EntryState StateOf(Profile profile, string feature) =>
        profile.IsFeatureEnabled(feature) ? EntryState.Enabled : EntryState.NotApplied;
}
