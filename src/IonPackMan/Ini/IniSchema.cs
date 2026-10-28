using System.IO;

namespace IonPackMan.Ini;

/// <summary>Aturan yang sama untuk semua file INI PackMan (PRD §7.2) dan cara memuatnya menjadi daftar error.</summary>
public static class IniSchema
{
    public const string CurrentVersion = "1.0";

    /// <summary>[Meta] SchemaVersion wajib ada dan harus versi yang dikenal.</summary>
    public static List<string> CheckMeta(IniFile ini)
    {
        var problems = ini.CheckKeys("Meta", required: ["SchemaVersion"], optional: []);
        if (problems.Count == 0 && ini.Get("Meta", "SchemaVersion") != CurrentVersion)
            problems.Add($"[Meta] SchemaVersion '{ini.Get("Meta", "SchemaVersion")}' is not supported (expected {CurrentVersion}).");
        return problems;
    }

    /// <summary>
    /// Membaca file dan mengubah semua kegagalan baca (tidak ada, rusak, tidak bisa dibuka) menjadi satu pesan error.
    /// Dipakai semua store agar layar cukup menampilkan daftar string.
    /// </summary>
    public static (IniFile? Ini, List<string> Errors) TryLoad(string path)
    {
        try
        {
            return (IniFile.Load(path), []);
        }
        catch (IniException ex)
        {
            return (null, [ex.Message]);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return (null, [$"{path}: file not found."]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, [$"{path}: cannot be read ({ex.Message})."]);
        }
    }
}
