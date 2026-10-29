using System.IO;
using System.Text.RegularExpressions;
using IonPackMan.Ini;

namespace IonPackMan.Machine;

/// <summary>
/// Daftar Client (bank) dan component yang dikerjakan di branch Client, dibaca dari Config\clients.ini (ADR-006).
/// File tidak ada = tidak ada Client (semua build Default). String Client kosong selalu berarti Default.
/// Layanan memegang satu instance bersama; layar Clients menyimpan perubahan lalu memanggil <see cref="Replace"/>,
/// sehingga semua layar langsung memakai data baru. Isi disimpan sebagai satu snapshot yang diganti sekaligus
/// (aman dibaca dari thread latar saat Dashboard membaca).
/// </summary>
public sealed partial class ClientConfig
{
    private sealed record State(Dictionary<string, string> Clients, Dictionary<string, HashSet<string>> Overrides);

    private static State Empty() => new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Nilai bawaan parameter "tanpa Client" di tes dan layanan. Hanya baca: tidak boleh di-<see cref="Replace"/>.</summary>
    public static readonly ClientConfig None = new();

    private volatile State _state;

    /// <summary>Daftar kosong yang bisa diperbarui (milik satu aplikasi).</summary>
    public ClientConfig() => _state = Empty();

    private ClientConfig(State state) => _state = state;

    /// <summary>Huruf/angka dipisah '-': aman dipakai di nama folder cache, dan '@' (pemisah id/versi/Client) tidak mungkin ada.</summary>
    [GeneratedRegex("^[A-Za-z0-9]+(-[A-Za-z0-9]+)*$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string id) => IdPattern().IsMatch(id);

    /// <summary>Id Client dan nama tampilannya.</summary>
    public IReadOnlyDictionary<string, string> Clients => _state.Clients;

    public bool IsKnown(string client) => _state.Clients.ContainsKey(client);

    /// <summary>Menukar isi dengan <paramref name="other"/> (hasil Load atau With/Without yang sudah tersimpan).</summary>
    public void Replace(ClientConfig other)
    {
        if (ReferenceEquals(this, None))
            throw new InvalidOperationException("ClientConfig.None is read-only.");
        _state = other._state;
    }

    /// <summary>Apakah Client memakai branch sendiri untuk component ini.</summary>
    public bool Overrides(string client, string componentId) =>
        _state.Overrides.TryGetValue(client, out var set) && set.Contains(componentId);

    /// <summary>Component yang memakai branch Client ini, urut abjad.</summary>
    public List<string> OverriddenBy(string client) =>
        _state.Overrides.TryGetValue(client, out var set) ? set.OrderBy(c => c, StringComparer.Ordinal).ToList() : [];

    /// <summary>Client yang memakai branch sendiri untuk component ini (pilihan selain Default saat import).</summary>
    public List<string> ClientsOverriding(string componentId) =>
        _state.Clients.Keys.Where(c => Overrides(c, componentId)).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Tag Client build yang boleh di-deploy untuk component ini di mesin ber-Client <paramref name="machineClient"/>
    /// (ADR-006 §4): Client mesin bila ia meng-override component, selain itu Default (""). Tidak ada fallback.
    /// </summary>
    public string BuildClientFor(string machineClient, string componentId) =>
        machineClient.Length > 0 && Overrides(machineClient, componentId) ? ExactId(machineClient) : "";

    /// <summary>Penulisan id sesuai clients.ini (key tidak peka huruf besar/kecil).</summary>
    public string ExactId(string client) =>
        _state.Clients.Keys.FirstOrDefault(c => c.Equals(client, StringComparison.OrdinalIgnoreCase)) ?? client;

    /// <summary>
    /// Saran Client dari nama branch git: Client yang meng-override component dan namanya muncul sebagai kata utuh di branch
    /// ("feature/bca-qristap" -> BCA). Null bila tidak ada atau ambigu; developer tetap mengonfirmasi.
    /// </summary>
    public string? Suggest(string branch, string componentId)
    {
        var words = Regex.Split(branch, "[^A-Za-z0-9]+").Where(w => w.Length > 0).ToList();
        var hits = ClientsOverriding(componentId).Where(c => words.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        return hits.Count == 1 ? hits[0] : null;
    }

    // ---------- Baca ----------

    /// <summary>
    /// Membaca Config\clients.ini. File tidak ada = daftar kosong. Component di [Overrides] harus ada di
    /// <paramref name="knownComponentIds"/> (catalog.ini), Client di [Overrides] harus ada di [Clients].
    /// </summary>
    public static (ClientConfig? Config, List<string> Errors) Load(string path, IReadOnlyCollection<string> knownComponentIds)
    {
        if (!File.Exists(path))
            return (new ClientConfig(), []);

        var (ini, errors) = IniSchema.TryLoad(path);
        return ini is null ? (null, errors) : FromIni(ini, path, knownComponentIds);
    }

    // Satu-satunya tempat aturan validasi: dipakai Load (file) dan Save (sebelum menulis).
    private static (ClientConfig? Config, List<string> Errors) FromIni(IniFile ini, string path, IReadOnlyCollection<string> knownComponentIds)
    {
        var errors = new List<string>();
        errors.AddRange(IniSchema.CheckMeta(ini));
        errors.AddRange(ini.CheckSections("Meta", "Clients", "Overrides"));

        var state = Empty();
        foreach (var (id, name) in ini.Section("Clients"))
        {
            if (!IsValidId(id))
                errors.Add($"[Clients] '{id}' is not a valid Client Id (letters and digits, optionally separated by '-').");
            state.Clients[id] = name;
        }

        foreach (var (client, list) in ini.Section("Overrides"))
        {
            if (!state.Clients.ContainsKey(client))
                errors.Add($"[Overrides] '{client}' is not listed in [Clients].");
            var components = list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            foreach (var id in components.Where(id => !knownComponentIds.Contains(id)))
                errors.Add($"[Overrides] {client}: '{id}' is not a component in catalog.ini.");
            state.Overrides[client] = components;
        }

        if (errors.Count > 0)
            return (null, errors.Select(e => $"{path}: {e}").ToList());
        return (new ClientConfig(state), []);
    }

    // ---------- Ubah (menghasilkan salinan; tidak menyentuh instance ini) ----------

    /// <summary>Salinan dengan Client <paramref name="id"/> ditambahkan atau diperbarui (nama dan daftar component yang di-override).</summary>
    public ClientConfig With(string id, string name, IEnumerable<string> overriddenComponents)
    {
        var copy = Copy();
        string key = ExactId(id);
        copy.Clients[key] = name;
        copy.Overrides[key] = overriddenComponents.ToHashSet();
        return new ClientConfig(copy);
    }

    /// <summary>Salinan tanpa Client <paramref name="id"/> beserta override-nya.</summary>
    public ClientConfig Without(string id)
    {
        var copy = Copy();
        copy.Clients.Remove(id);
        copy.Overrides.Remove(id);
        return new ClientConfig(copy);
    }

    private State Copy()
    {
        var copy = Empty();
        foreach (var (id, name) in _state.Clients)
            copy.Clients[id] = name;
        foreach (var (id, set) in _state.Overrides)
            copy.Overrides[id] = [.. set];
        return copy;
    }

    // ---------- Tulis ----------

    private IniFile ToIni()
    {
        var ini = new IniFile();
        ini.Set("Meta", "SchemaVersion", IniSchema.CurrentVersion);
        var ids = _state.Clients.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var id in ids)
            ini.Set("Clients", id, _state.Clients[id]);
        foreach (var id in ids)
            if (OverriddenBy(id) is { Count: > 0 } components)
                ini.Set("Overrides", id, string.Join(",", components));
        return ini;
    }

    /// <summary>
    /// Menulis isi ini ke <paramref name="path"/> bila lolos aturan yang sama dengan <see cref="Load"/>.
    /// Mengembalikan daftar masalah (kosong = tersimpan); bila tidak kosong, file tidak disentuh.
    /// Hanya menulis file: pemanggil menukar isi instance bersama dengan <see cref="Replace"/>.
    /// </summary>
    public List<string> Save(string path, IReadOnlyCollection<string> knownComponentIds)
    {
        IniFile ini;
        try { ini = ToIni(); }
        catch (ArgumentException ex) { return [ex.Message]; }   // mis. nama berisi " ;" atau spasi di ujung

        var (_, errors) = FromIni(ini, path, knownComponentIds);
        if (errors.Count > 0)
            return errors;

        try { ini.Save(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"{path}: cannot be written ({ex.Message})."];
        }
        return [];
    }
}
