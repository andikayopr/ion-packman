using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using Microsoft.Win32;

namespace IonPackMan.Machine;

/// <param name="Processor">Nama CPU dan jumlah logical processor; "Unknown" bila tidak terbaca.</param>
/// <param name="Memory">RAM total dan yang masih kosong; "Unknown" bila tidak terbaca.</param>
public sealed record MachineSnapshot(string MachineName, string WindowsVersion, string Processor = "Unknown", string Memory = "Unknown");

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
    public MachineSnapshot Inspect() => new(Environment.MachineName, ReadWindowsVersion(), ReadProcessor(), ReadMemory());

    /// <summary>"Intel(R) Core(TM) i7-1185G7 @ 3.00GHz · 8 logical processors".</summary>
    private static string ReadProcessor()
    {
        string name = "";
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            name = (key?.GetValue("ProcessorNameString") as string ?? "").Trim();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return FormatProcessor(name, Environment.ProcessorCount);
    }

    public static string FormatProcessor(string name, int logicalProcessors)
    {
        string cores = $"{logicalProcessors} logical processors";
        return name.Length == 0 ? (logicalProcessors > 0 ? cores : "Unknown") : $"{CollapseSpaces(name)} · {cores}";
    }

    private static string CollapseSpaces(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private static string ReadMemory()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref status) ? FormatMemory(status.TotalPhys, status.AvailPhys) : "Unknown";
    }

    /// <summary>"16.0 GB total · 7.2 GB free".</summary>
    public static string FormatMemory(ulong totalBytes, ulong freeBytes)
    {
        const double Gb = 1024.0 * 1024 * 1024;
        return totalBytes == 0 ? "Unknown" : $"{totalBytes / Gb:0.0} GB total · {freeBytes / Gb:0.0} GB free";
    }

    /// <summary>Versi Windows seperti yang ditampilkan winver: "Windows 11 Enterprise · Version 24H2 (OS Build 26100.2605)".</summary>
    private static string ReadWindowsVersion()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("ProductName") is string product && product.Length > 0)
                return FormatWindowsVersion(
                    product,
                    key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string,
                    key.GetValue("CurrentBuildNumber") as string ?? Environment.OSVersion.Version.Build.ToString(),
                    key.GetValue("UBR") is int ubr ? ubr : null);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return "Unknown";
    }

    /// <summary>
    /// Windows 11 masih menulis ProductName "Windows 10 ..." di Registry; winver memperbaikinya dari nomor build (>= 22000).
    /// UBR = nomor revisi build (bagian setelah titik di "OS Build 26100.2605").
    /// </summary>
    public static string FormatWindowsVersion(string productName, string? displayVersion, string? buildNumber, int? ubr)
    {
        string product = productName;
        if (int.TryParse(buildNumber, out int build) && build >= 22000 && product.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
            product = "Windows 11" + product["Windows 10".Length..];

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(displayVersion))
            details.Add($"Version {displayVersion}");
        if (!string.IsNullOrWhiteSpace(buildNumber))
            details.Add($"(OS Build {buildNumber}{(ubr is { } revision ? "." + revision : "")})");
        return details.Count == 0 ? product : $"{product} · {string.Join(" ", details)}";
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
                // Path (Registry, murah) dicek dulu; status/start type/nama tampilan (panggilan SCM, mahal) hanya untuk yang cocok.
                string path = ReadImagePath(sc.ServiceName);
                if (path.Length > 0 && dirs.Any(d => IsUnder(path, d)))
                    result.Add(Describe(sc, path));
            }
        }
        return result.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Proses yang berjalan dari direktori itu (termasuk subfoldernya). Proses yang path-nya tidak bisa dibaca dilewati.</summary>
    public List<ProcessInfo> GetProcessesUnder(string directory) => ProcessesUnder(ListProcesses(), directory);

    /// <summary>
    /// Semua proses yang path exe-nya terbaca. Mahal (sekitar satu detik di mesin dengan ratusan proses: tiap proses dibuka),
    /// jadi pemanggil yang butuh banyak folder membacanya SEKALI lalu memakai <see cref="ProcessesUnder"/> per folder.
    /// </summary>
    public List<ProcessInfo> ListProcesses()
    {
        var result = new List<ProcessInfo>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    string? path = p.MainModule?.FileName;
                    if (path is not null)
                        result.Add(new ProcessInfo(p.ProcessName, p.Id, p.WorkingSet64, path));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Akses ditolak / proses sudah selesai / proses sistem: abaikan.
                }
            }
        }
        return result;
    }

    /// <summary>Yang berjalan dari <paramref name="directory"/> (termasuk subfoldernya), urut nama lalu Pid.</summary>
    public static List<ProcessInfo> ProcessesUnder(IEnumerable<ProcessInfo> processes, string directory) =>
        string.IsNullOrWhiteSpace(directory)
            ? []
            : processes.Where(p => IsUnder(p.ExecutablePath, directory))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Pid).ToList();

    private static ServiceInfo Describe(ServiceController sc) => Describe(sc, ReadImagePath(sc.ServiceName));

    private static ServiceInfo Describe(ServiceController sc, string imagePath)
    {
        string status = Safe(() => sc.Status.ToString());
        string startType = Safe(() => sc.StartType.ToString());
        return new ServiceInfo(sc.ServiceName, Safe(() => sc.DisplayName), status, startType, imagePath);
    }

    private static string Safe(Func<string> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { return "Unknown"; }
    }

    /// <summary>Path exe service dari ImagePath di Registry; kosong bila service tidak ada atau tidak terbaca.</summary>
    public static string ReadImagePath(string serviceName)
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
