using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using Microsoft.Win32;

namespace IonPackMan.Machine;

public sealed record MachineSnapshot(string MachineName, string WindowsVersion);

/// <param name="Status">Running, Stopped, Paused, dst.; "Unknown" bila tidak terbaca.</param>
/// <param name="StartType">Automatic, Manual, Disabled; "Unknown" bila tidak terbaca.</param>
/// <param name="ExecutablePath">Path exe dari ImagePath Registry (tanda kutip/argumen dibuang); kosong bila tidak ada.</param>
public sealed record ServiceInfo(string Name, string DisplayName, string Status, string StartType, string ExecutablePath);

public sealed record ProcessInfo(string Name, int Pid, long MemoryBytes, string ExecutablePath);

/// <summary>
/// Membaca kondisi mesin (R2). HANYA membaca: tidak pernah start/stop service atau menulis Registry/file.
/// Status service dan status proses dipisah; keduanya dilaporkan apa adanya.
/// </summary>
public sealed class MachineInspector
{
    public MachineSnapshot Inspect() => new(Environment.MachineName, ReadWindowsVersion());

    private static string ReadWindowsVersion()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("ProductName") is string product && product.Length > 0)
                return $"{product} {Environment.OSVersion.Version}";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return "Unknown";
    }

    /// <summary>Info satu service, atau null bila service tidak terpasang.</summary>
    public ServiceInfo? GetService(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        try
        {
            using var sc = new ServiceController(name);
            // Memicu pembacaan; melempar InvalidOperationException bila service tidak ada.
            _ = sc.Status;
            return Describe(sc);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Semua service yang exe-nya berada di bawah salah satu base directory (R2).</summary>
    public List<ServiceInfo> FindServicesUnder(IEnumerable<string> baseDirectories)
    {
        var dirs = baseDirectories.ToList();
        var result = new List<ServiceInfo>();
        ServiceController[] all;
        try
        {
            all = ServiceController.GetServices();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return result;
        }

        foreach (var sc in all)
        {
            using (sc)
            {
                var info = Describe(sc);
                if (info.ExecutablePath.Length > 0 && dirs.Any(d => IsUnder(info.ExecutablePath, d)))
                    result.Add(info);
            }
        }
        return result.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Proses yang berjalan dari direktori itu (termasuk subfoldernya). Proses yang path-nya tidak bisa dibaca dilewati.</summary>
    public List<ProcessInfo> GetProcessesUnder(string directory)
    {
        var result = new List<ProcessInfo>();
        if (string.IsNullOrWhiteSpace(directory))
            return result;

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    string? path = p.MainModule?.FileName;
                    if (path is not null && IsUnder(path, directory))
                        result.Add(new ProcessInfo(p.ProcessName, p.Id, p.WorkingSet64, path));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Akses ditolak / proses sudah selesai / proses sistem: abaikan.
                }
            }
        }
        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToList();
    }

    private static ServiceInfo Describe(ServiceController sc)
    {
        string status = Safe(() => sc.Status.ToString());
        string startType = Safe(() => sc.StartType.ToString());
        return new ServiceInfo(sc.ServiceName, Safe(() => sc.DisplayName), status, startType, ReadImagePath(sc.ServiceName));
    }

    private static string Safe(Func<string> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return "Unknown"; }
    }

    private static string ReadImagePath(string serviceName)
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return key?.GetValue("ImagePath") is string raw ? ExtractExecutable(raw) : "";
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return "";
        }
    }

    /// <summary>Ambil path exe dari ImagePath: buang tanda kutip dan argumen.</summary>
    public static string ExtractExecutable(string imagePath)
    {
        string s = imagePath.Trim();
        if (s.Length == 0)
            return "";
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            return (end < 0 ? s[1..] : s[1..end]).Trim();
        }
        // Tanpa kutip: path bisa memuat spasi, jadi potong setelah ".exe" pertama bila ada.
        int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exe >= 0)
            return s[..(exe + 4)];
        int space = s.IndexOf(' ');
        return space < 0 ? s : s[..space];
    }

    /// <summary>True bila path berada di dalam directory (tidak peduli huruf besar/kecil; "D:\IONX" bukan di "D:\ION").</summary>
    public static bool IsUnder(string path, string directory)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(directory).TrimEnd('\\') + "\\";
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
