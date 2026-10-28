using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace IonPackMan.Deployment;

/// <summary>
/// Implementasi asli <see cref="IRegistryAccess"/>. Selalu view Registry64; path dari Profile sudah memuat
/// WOW6432Node dan dipakai apa adanya. Hive didukung: HKLM/HKEY_LOCAL_MACHINE (HKCU hanya agar bisa diuji pada kunci uji).
/// Tidak pernah menghapus key.
/// </summary>
public sealed class RegistryAccess : IRegistryAccess
{
    public RegistryValue? Read(string key, string valueName)
    {
        var (hive, sub) = ParsePath(key);
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = baseKey.OpenSubKey(sub, writable: false);
            if (k is null)
                return null;

            var raw = k.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw is null)
                return null;
            return k.GetValueKind(valueName) switch
            {
                RegistryValueKind.DWord => new RegistryValue(RegistryValueType.DWord,
                    unchecked((uint)(int)raw).ToString(CultureInfo.InvariantCulture)),
                RegistryValueKind.String or RegistryValueKind.ExpandString =>
                    new RegistryValue(RegistryValueType.String, (string)raw),
                var other => throw new IOException($"Registry '{key}' value '{valueName}' bertipe {other}, tidak didukung."),
            };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw Wrap(ex, "membaca", key, valueName);
        }
    }

    public void Write(string key, string valueName, RegistryValueType type, string value)
    {
        var (hive, sub) = ParsePath(key);
        object data;
        RegistryValueKind kind;
        switch (type)
        {
            case RegistryValueType.String:
                data = value ?? throw new ArgumentNullException(nameof(value));
                kind = RegistryValueKind.String;
                break;
            case RegistryValueType.DWord:
                if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var u))
                    throw new ArgumentException($"Nilai DWORD '{value}' tidak valid untuk '{key}' value '{valueName}' (harus 0..4294967295).");
                data = unchecked((int)u);
                kind = RegistryValueKind.DWord;
                break;
            default:
                throw new ArgumentException($"Tipe Registry tidak valid: {type}.", nameof(type));
        }

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = baseKey.CreateSubKey(sub, writable: true)
                ?? throw new IOException("Key tidak bisa dibuat.");
            k.SetValue(valueName, data, kind);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw Wrap(ex, "menulis", key, valueName);
        }
    }

    public void Delete(string key, string valueName)
    {
        var (hive, sub) = ParsePath(key);
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = baseKey.OpenSubKey(sub, writable: true);
            k?.DeleteValue(valueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw Wrap(ex, "menghapus", key, valueName);
        }
    }

    /// <summary>Memecah "HKLM\a\b" menjadi hive + subkey. Menolak hive lain dan subkey kosong.</summary>
    public static (RegistryHive Hive, string SubKey) ParsePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Path Registry kosong.", nameof(key));
        var i = key.IndexOf('\\');
        var head = i < 0 ? key : key[..i];
        var sub = i < 0 ? "" : key[(i + 1)..].Trim('\\');
        var hive = head.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            _ => throw new ArgumentException($"Hive Registry '{head}' tidak didukung (path '{key}'); hanya HKLM."),
        };
        if (sub.Length == 0)
            throw new ArgumentException($"Path Registry '{key}' tidak memuat subkey.");
        return (hive, sub);
    }

    private static Exception Wrap(Exception ex, string action, string key, string valueName)
    {
        var msg = $"Gagal {action} Registry '{key}' value '{valueName}': {ex.Message}";
        return ex is UnauthorizedAccessException or System.Security.SecurityException
            ? new UnauthorizedAccessException(msg, ex)
            : new IOException(msg, ex);
    }
}
