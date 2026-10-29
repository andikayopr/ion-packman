using System.IO;
using IonPackMan.Ini;
using IonPackMan.Machine;

namespace IonPackMan.Components;

public sealed record CatalogEntry(string Id, string DisplayName);

/// <summary>
/// Membaca dan menyimpan definisi component di folder rilis (PRD R3–R4).
/// Satu component = satu release: menyimpan versi baru menggantikan release lama component itu.
/// </summary>
public sealed class ComponentStore
{
    private readonly string _releaseDir;

    public ComponentStore(string releaseDir) => _releaseDir = releaseDir;

    /// <summary>Daftar component yang dikenal (Components\catalog.ini: "id=Display Name").</summary>
    public (List<CatalogEntry> Entries, List<string> Errors) LoadCatalog()
    {
        string path = Paths.CatalogIni(_releaseDir);
        var (ini, errors) = IniSchema.TryLoad(path);
        if (ini is null)
            return ([], errors);

        errors.AddRange(IniSchema.CheckMeta(ini));
        errors.AddRange(ini.CheckSections("Meta", "Components"));
        if (!ini.HasSection("Components"))
            errors.Add("missing section [Components].");
        foreach (var (id, name) in ini.Section("Components"))
            if (!ComponentRelease.IsValidId(id))
                errors.Add($"[Components] '{id}' is not a valid component Id.");
        if (errors.Count > 0)
            return ([], errors.Select(e => $"{path}: {e}").ToList());

        var entries = ini.Section("Components").Select(kv => new CatalogEntry(kv.Key, kv.Value.Length > 0 ? kv.Value : kv.Key)).ToList();
        return (entries, []);
    }

    /// <summary>
    /// Satu component = satu release: versi yang berlaku (yang terbaru) atau kosong. Bila folder rilis berisi file versi lain
    /// (diubah manual atau sisa dari sebelum aturan ini), file itu diabaikan di mana pun (lihat <see cref="IgnoredVersions"/>)
    /// dan dihapus saat component disimpan.
    /// </summary>
    public List<string> ListVersions(string componentId) => AllVersions(componentId).Take(1).ToList();

    /// <summary>File release lain milik component ini yang diabaikan karena sudah ada versi yang lebih baru.</summary>
    public List<string> IgnoredVersions(string componentId) => AllVersions(componentId).Skip(1).ToList();

    private List<string> AllVersions(string componentId)
    {
        string dir = Path.Combine(Paths.ComponentsDir(_releaseDir), componentId);
        if (!ComponentRelease.IsValidId(componentId) || !Directory.Exists(dir))
            return [];

        string prefix = componentId + "-";
        return Directory.GetFiles(dir, prefix + "*.ini")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => name![prefix.Length..])
            .Where(ComponentRelease.IsValidVersion)
            .OrderDescending(VersionComparer.Instance)
            .ToList();
    }

    /// <summary>Membaca satu release; file harus benar-benar berisi Id dan versi yang diminta.</summary>
    public (ComponentRelease? Release, List<string> Errors) Load(string componentId, string releaseVersion)
    {
        if (!ComponentRelease.IsValidId(componentId) || !ComponentRelease.IsValidVersion(releaseVersion))
            return (null, [$"'{componentId}@{releaseVersion}' is not a valid component release."]);

        if (IgnoredVersions(componentId).Contains(releaseVersion))
            return (null, [$"{componentId}@{releaseVersion} is not the release in use: {componentId}@{ListVersions(componentId)[0]} replaces it."]);

        string path = Paths.ComponentIni(_releaseDir, componentId, releaseVersion);
        var (release, errors) = ComponentRelease.LoadFile(path);
        if (release is null)
            return (null, errors);

        if (release.Id != componentId || release.ReleaseVersion != releaseVersion)
            return (null, [$"{path}: file declares {release.Id}@{release.ReleaseVersion} but its name says {componentId}@{releaseVersion}."]);

        return (release, []);
    }

    /// <summary>
    /// Validasi lalu simpan. Satu component = satu release: file release versi lain milik component yang sama dihapus
    /// SETELAH file baru tertulis (gagal menulis tidak menghilangkan release lama). Mengembalikan masalah; kosong = tersimpan.
    /// </summary>
    public List<string> Save(ComponentRelease release, AllowList? allowList = null)
    {
        var problems = release.Validate(allowList);
        if (problems.Count > 0)
            return problems;

        try
        {
            release.ToIni().Save(Paths.ComponentIni(_releaseDir, release.Id, release.ReleaseVersion));
        }
        catch (ArgumentException ex)
        {
            return [ex.Message];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"Cannot save {release.Id}@{release.ReleaseVersion}: {ex.Message}"];
        }

        var removeProblems = new List<string>();
        foreach (string other in AllVersions(release.Id).Where(v => v != release.ReleaseVersion))
        {
            try
            {
                File.Delete(Paths.ComponentIni(_releaseDir, release.Id, other));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                removeProblems.Add($"{release.Id}@{release.ReleaseVersion} was saved, but the older release {other} could not be removed: {ex.Message}");
            }
        }
        return removeProblems;
    }

    /// <summary>Import file INI dari luar: ditolak bila Id bukan component yang sedang dipilih (R4), lalu disimpan.</summary>
    public (ComponentRelease? Release, List<string> Errors) Import(string path, string expectedComponentId, AllowList? allowList = null)
    {
        var (release, errors) = ComponentRelease.LoadFile(path);
        if (release is null)
            return (null, errors);
        if (release.Id != expectedComponentId)
            return (null, [$"{path}: this file is for '{release.Id}', not '{expectedComponentId}'."]);

        var saveErrors = Save(release, allowList);
        return saveErrors.Count > 0 ? (null, saveErrors) : (release, []);
    }

    /// <summary>Urutan versi: bagian angka dibandingkan sebagai angka (2.10.0 &gt; 2.9.0); sisanya sebagai teks.</summary>
    private sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();

        public int Compare(string? a, string? b)
        {
            var (na, ta) = Split(a ?? "");
            var (nb, tb) = Split(b ?? "");
            int c = na.CompareTo(nb);
            // "2.1.0" (rilis) dianggap lebih baru dari "2.1.0-dev.1" (pra-rilis).
            if (c == 0) c = ta.Length == 0 ? (tb.Length == 0 ? 0 : 1) : (tb.Length == 0 ? -1 : string.Compare(ta, tb, StringComparison.OrdinalIgnoreCase));
            return c;
        }

        private static (Version Numbers, string Tail) Split(string v)
        {
            int i = v.IndexOfAny(['-', '+', '_']);
            string head = i < 0 ? v : v[..i];
            string tail = i < 0 ? "" : v[i..];
            // Version.TryParse butuh minimal "major.minor", jadi "2" dibaca sebagai "2.0".
            string numeric = head.Contains('.') ? head : head + ".0";
            return (Version.TryParse(numeric, out var parsed) ? parsed : new Version(0, 0), tail);
        }
    }
}
