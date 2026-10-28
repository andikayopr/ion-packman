using System.IO;
using System.Text;

namespace IonPackMan.Ini;

/// <summary>
/// Pembaca/penulis INI kecil untuk file milik PackMan (machine, allowlist, component, profile, backup, history).
/// Bukan untuk INI milik aplikasi ATM — itu lewat AppIniEditor (ADR-003).
///
/// Aturan baca (ketat; pelanggaran = IniException dengan nomor baris):
///   - [Section] dan Key=Value; nama section/key tidak peka huruf besar-kecil.
///   - Baris kosong, baris yang diawali ; atau # adalah komentar.
///   - Komentar di ujung baris: spasi lalu ; ("Key=Value   ; catatan").
///   - Section ganda, key ganda dalam satu section, key di luar section, dan baris tanpa "=" adalah error.
/// Aturan tulis: file dibuat ulang dari data (komentar tidak dipertahankan), ditulis ke .tmp lalu di-rename.
/// </summary>
public sealed class IniFile
{
    // Dictionary mempertahankan urutan penambahan selama tidak ada Remove (API ini memang tidak punya Remove).
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> SectionNames => _sections.Keys;

    public bool HasSection(string section) => _sections.ContainsKey(section);

    public IReadOnlyDictionary<string, string> Section(string section) =>
        _sections.TryGetValue(section, out var keys) ? keys : EmptySection;

    private static readonly IReadOnlyDictionary<string, string> EmptySection = new Dictionary<string, string>();

    public string? Get(string section, string key) =>
        _sections.TryGetValue(section, out var keys) && keys.TryGetValue(key, out var value) ? value : null;

    /// <summary>Membaca file dari disk. Melempar IniException bila rusak, FileNotFoundException bila tidak ada.</summary>
    public static IniFile Load(string path) => Parse(File.ReadAllText(path, Encoding.UTF8), path);

    public static IniFile Parse(string text, string sourceName)
    {
        var ini = new IniFile();
        Dictionary<string, string>? current = null;
        var lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = i + 1;
            string line = StripInlineComment(lines[i].TrimEnd('\r')).Trim();

            if (line.Length == 0 || line[0] == ';' || line[0] == '#')
                continue;

            if (line[0] == '[')
            {
                if (line[^1] != ']')
                    throw new IniException(sourceName, lineNo, "section header is missing ']'.");
                string name = line[1..^1].Trim();
                if (name.Length == 0)
                    throw new IniException(sourceName, lineNo, "section name is empty.");
                if (ini._sections.ContainsKey(name))
                    throw new IniException(sourceName, lineNo, $"duplicate section [{name}].");
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                ini._sections[name] = current;
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0)
                throw new IniException(sourceName, lineNo, $"expected 'Key=Value' but found '{line}'.");
            if (current is null)
                throw new IniException(sourceName, lineNo, "key found before any [Section].");

            string key = line[..eq].Trim();
            if (key.Length == 0)
                throw new IniException(sourceName, lineNo, "key name is empty.");
            if (current.ContainsKey(key))
                throw new IniException(sourceName, lineNo, $"duplicate key '{key}'.");
            current[key] = line[(eq + 1)..].Trim();
        }

        return ini;
    }

    /// <summary>
    /// Memeriksa satu section: key wajib harus ada, key di luar daftar (wajib + opsional) adalah error.
    /// Mengembalikan daftar masalah (kosong = valid) supaya semua masalah tampil sekaligus.
    /// </summary>
    public List<string> CheckKeys(string section, string[] required, string[] optional)
    {
        var problems = new List<string>();
        if (!_sections.TryGetValue(section, out var keys))
        {
            problems.Add($"missing section [{section}].");
            return problems;
        }

        foreach (var key in required)
        {
            if (!keys.TryGetValue(key, out var value))
                problems.Add($"[{section}] is missing required key '{key}'.");
            else if (value.Length == 0)
                problems.Add($"[{section}] required key '{key}' is empty.");
        }

        var known = new HashSet<string>(required.Concat(optional), StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys.Keys)
            if (!known.Contains(key))
                problems.Add($"[{section}] has unknown key '{key}'.");

        return problems;
    }

    /// <summary>
    /// Section di luar daftar adalah error (PRD §7.2: tidak ada yang diabaikan diam-diam).
    /// Nama yang diakhiri ':' (mis. "Component:") berarti semua section dengan awalan itu diizinkan.
    /// </summary>
    public List<string> CheckSections(params string[] known)
    {
        var exact = new HashSet<string>(known.Where(k => !k.EndsWith(':')), StringComparer.OrdinalIgnoreCase);
        var prefixes = known.Where(k => k.EndsWith(':')).ToArray();
        return _sections.Keys.Where(name => !exact.Contains(name)
                                            && !prefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                             .Select(name => $"unknown section [{name}].")
                             .ToList();
    }

    /// <summary>Menetapkan nilai (membuat section bila belum ada). Menolak nilai yang tidak bisa dibaca kembali apa adanya.</summary>
    public void Set(string section, string key, string value)
    {
        RequireValidName(section, nameof(section), forbidden: "[]\r\n");
        RequireValidName(key, nameof(key), forbidden: "=\r\n");
        if (key[0] == ';' || key[0] == '#' || key[0] == '[')
            throw new ArgumentException($"Key '{key}' cannot start with ';', '#' or '['.", nameof(key));
        if (value.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException($"Value of '{key}' cannot contain line breaks.", nameof(value));
        if (value != value.Trim())
            throw new ArgumentException($"Value of '{key}' cannot start or end with whitespace.", nameof(value));
        if (StripInlineComment(value) != value)
            throw new ArgumentException($"Value of '{key}' cannot contain ' ;' (it would be read as a comment).", nameof(value));

        if (!_sections.TryGetValue(section, out var keys))
        {
            keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections[section] = keys;
        }
        keys[key] = value;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var (name, keys) in _sections)
        {
            sb.Append('[').Append(name).Append("]\r\n");
            foreach (var (key, value) in keys)
                sb.Append(key).Append('=').Append(value).Append("\r\n");
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Tulis atomik: .tmp dulu, lalu rename. File lama tidak pernah setengah tertulis.</summary>
    public void Save(string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, ToText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            IoRetry.Run(() => File.Move(tmp, path, overwrite: true));
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    // "Key=Value   ; catatan" -> "Key=Value". Hanya ';' yang didahului spasi/tab dihitung komentar.
    private static string StripInlineComment(string line)
    {
        for (int i = 1; i < line.Length; i++)
            if (line[i] == ';' && (line[i - 1] == ' ' || line[i - 1] == '\t'))
                return line[..i];
        return line;
    }

    private static void RequireValidName(string name, string paramName, string forbidden)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.IndexOfAny(forbidden.ToCharArray()) >= 0)
            throw new ArgumentException($"'{name}' is not a valid INI name.", paramName);
    }
}
