using System.IO;
using IonPackMan.Ini;

namespace IonPackMan.Machine;

/// <summary>
/// Batas operasi yang diizinkan (PRD R7, D5/D12), dibaca dari Config\allowlist.ini.
/// Deny-by-default: apa pun yang tidak tercantum ditolak. File ini dikelola developer ION dan tidak bisa diubah dari UI.
/// </summary>
public sealed class AllowList
{
    public required IReadOnlyDictionary<string, string> BaseDirectories { get; init; }
    public required IReadOnlyDictionary<string, string> RegistryKeys { get; init; }
    public required IReadOnlyDictionary<string, string> IniFiles { get; init; }
    public required IReadOnlyDictionary<string, string> Services { get; init; }

    public static (AllowList? List, List<string> Errors) Load(string path)
    {
        var (ini, errors) = IniSchema.TryLoad(path);
        if (ini is null)
            return (null, errors);

        errors.AddRange(IniSchema.CheckMeta(ini));
        errors.AddRange(ini.CheckSections("Meta", "BaseDirectories", "RegistryKeys", "IniFiles", "Services"));
        if (errors.Count > 0)
            return (null, errors.Select(e => $"{path}: {e}").ToList());

        // Section yang tidak ditulis = tidak ada yang diizinkan (aman).
        return (new AllowList
        {
            BaseDirectories = Copy(ini, "BaseDirectories"),
            RegistryKeys = Copy(ini, "RegistryKeys"),
            IniFiles = Copy(ini, "IniFiles"),
            Services = Copy(ini, "Services"),
        }, []);
    }

    /// <summary>Target harus berada <b>di dalam</b> salah satu base directory (bukan sama dengan base directory itu).</summary>
    public bool IsUnderBaseDirectory(string path)
    {
        string? full = Normalize(path);
        if (full is null)
            return false;

        foreach (string baseDir in BaseDirectories.Values)
        {
            string? root = Normalize(baseDir);
            if (root is null)
                continue;
            // Tambah pemisah supaya "D:\IONX" tidak dianggap berada di "D:\ION".
            if (full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                && full.TrimEnd('\\').Length > root.TrimEnd('\\').Length)
                return true;
        }
        return false;
    }

    /// <summary>Key harus persis sama dengan salah satu yang terdaftar (tanpa sub-key, tanpa wildcard).</summary>
    public bool IsRegistryKeyAllowed(string key)
    {
        string wanted = key.Trim().TrimEnd('\\');
        return RegistryKeys.Values.Any(k => string.Equals(k.TrimEnd('\\'), wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>File INI aplikasi harus persis sama dengan salah satu path yang terdaftar.</summary>
    public bool IsIniFileAllowed(string path)
    {
        string? full = Normalize(path);
        return full is not null && IniFiles.Values.Any(f => string.Equals(Normalize(f), full, StringComparison.OrdinalIgnoreCase));
    }

    public bool IsServiceAllowed(string serviceName) =>
        Services.Values.Any(s => string.Equals(s, serviceName, StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, string> Copy(IniFile ini, string section) =>
        new(ini.Section(section), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Path absolut yang sudah dirapikan ("..", "." dan "/" diselesaikan), atau null bila tidak aman/tidak valid:
    /// relatif, awalan \\?\ atau \\.\, nama stream (":" setelah drive), atau karakter terlarang.
    /// </summary>
    private static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            return null;
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return null; // UNC dan awalan device (\\?\, \\.\) tidak diizinkan
        if (path.IndexOf(':', 2) >= 0)
            return null; // alternate data stream
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.IndexOfAny(['*', '?']) >= 0)
            return null; // karakter terlarang dan wildcard
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
