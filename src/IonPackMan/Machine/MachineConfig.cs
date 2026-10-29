using System.IO;
using IonPackMan.Deployment;
using IonPackMan.Ini;

namespace IonPackMan.Machine;

/// <summary>
/// Isi machine.ini: identitas mesin, environment, lokasi instalasi, dan local cache (PRD §7.2, D9).
/// File ini ditulis manual oleh developer. Satu-satunya yang ditulis PackMan: key [Machine] Profile, saat pindah Profile
/// (<see cref="Deployment.ProfileSwitcher"/>), yang menandai Profile aktif.
/// </summary>
public sealed class MachineConfig
{
    private static readonly string[] Environments = ["Development", "Test", "Production"];

    public required string MachineName { get; init; }
    public required string Environment { get; init; }
    /// <summary>Profile aktif: dipilih manual atau lewat pindah Profile; kosong = deteksi otomatis.</summary>
    public string Profile { get; set; } = "";
    /// <summary>Client (bank) yang sedang dikerjakan di mesin dev, diisi manual (ADR-006); kosong = default.</summary>
    public string Client { get; set; } = "";
    /// <summary>Path machine.ini ini dibaca; kosong bila config dibuat tanpa file (tes).</summary>
    public string SourcePath { get; init; } = "";
    public required IReadOnlyDictionary<string, string> InstallLocations { get; init; }
    public required string LocalCacheDirectory { get; init; }

    /// <summary>[SERVICE] nama tampilan = nama Windows Service. Hanya untuk ditampilkan di Dashboard (dibaca, tidak diubah).</summary>
    public IReadOnlyDictionary<string, string> Services { get; init; } = new Dictionary<string, string>();

    /// <summary>[PROGRAM] nama tampilan = folder program. Proses yang berjalan dari folder itu tampil di Dashboard.</summary>
    public IReadOnlyDictionary<string, string> Programs { get; init; } = new Dictionary<string, string>();

    /// <summary>Gating Developer Console (D6/D7). Selain "Development" — termasuk file yang tidak terbaca — berarti false.</summary>
    public bool IsDevelopment => Environment.Equals("Development", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Menyimpan Client ke machine.ini lalu memakainya (ADR-006); "" = Default. Satu-satunya jalan mengubah Client dari UI.
    /// Mengembalikan pesan error, atau null bila tersimpan.
    /// </summary>
    public string? TrySetClient(string client)
    {
        try
        {
            AppIniEditor.Write(SourcePath, "Machine", "Client", client);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return $"Client was not saved to machine.ini: {ex.Message}";
        }
        Client = client;
        return null;
    }

    /// <summary>Config valid, atau null beserta daftar masalah (file hilang, rusak, atau melanggar skema).</summary>
    public static (MachineConfig? Config, List<string> Errors) Load(string path)
    {
        var (ini, errors) = IniSchema.TryLoad(path);
        if (ini is null)
            return (null, errors);

        errors.AddRange(IniSchema.CheckMeta(ini));
        errors.AddRange(ini.CheckSections("Meta", "Machine", "InstallLocations", "PackMan", "SERVICE", "PROGRAM"));
        errors.AddRange(ini.CheckKeys("Machine", required: ["MachineName", "Environment"], optional: ["Profile", "Client"]));
        errors.AddRange(ini.CheckKeys("PackMan", required: ["LocalCacheDirectory"], optional: []));
        if (!ini.HasSection("InstallLocations"))
            errors.Add("missing section [InstallLocations].");

        string environment = ini.Get("Machine", "Environment") ?? "";
        if (environment.Length > 0 && !Environments.Contains(environment, StringComparer.OrdinalIgnoreCase))
            errors.Add($"[Machine] Environment '{environment}' must be one of: {string.Join(", ", Environments)}.");

        string client = ini.Get("Machine", "Client") ?? "";
        if (client.Length > 0 && !ClientConfig.IsValidId(client))
            errors.Add($"[Machine] Client '{client}' is not a valid Client Id (letters and digits, optionally separated by '-').");

        foreach (var (label, serviceName) in ini.Section("SERVICE"))
            if (serviceName.Length == 0)
                errors.Add($"[SERVICE] '{label}' has no service name.");
        foreach (var (label, folder) in ini.Section("PROGRAM"))
            if (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal))
                errors.Add($"[PROGRAM] '{label}' must be a full local folder path (found '{folder}').");

        if (errors.Count > 0)
            return (null, errors.Select(e => $"{path}: {e}").ToList());

        return (new MachineConfig
        {
            MachineName = ini.Get("Machine", "MachineName")!,
            Environment = environment,
            Profile = ini.Get("Machine", "Profile") ?? "",
            Client = client,
            SourcePath = Path.GetFullPath(path),
            InstallLocations = new Dictionary<string, string>(ini.Section("InstallLocations"), StringComparer.OrdinalIgnoreCase),
            LocalCacheDirectory = ini.Get("PackMan", "LocalCacheDirectory")!,
            Services = new Dictionary<string, string>(ini.Section("SERVICE"), StringComparer.OrdinalIgnoreCase),
            Programs = new Dictionary<string, string>(ini.Section("PROGRAM"), StringComparer.OrdinalIgnoreCase),
        }, []);
    }

    /// <summary>
    /// Local cache harus di disk lokal tetap (bukan USB/removable/jaringan) dan bisa ditulis (D10).
    /// Mengembalikan pesan perbaikan, atau null bila aman dipakai. Folder dibuat bila belum ada.
    /// </summary>
    public string? CheckCacheDirectory()
    {
        string dir = LocalCacheDirectory;
        string fix = "Set LocalCacheDirectory in machine.ini to a folder on a local fixed disk, e.g. D:\\ION-PackMan\\Cache.";

        if (!Path.IsPathFullyQualified(dir) || dir.StartsWith(@"\\", StringComparison.Ordinal))
            return $"LocalCacheDirectory '{dir}' is not a local drive path. {fix}";

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(dir)!);
            if (!drive.IsReady)
                return $"Drive {drive.Name} for LocalCacheDirectory is not available. {fix}";
            if (drive.DriveType != DriveType.Fixed)
                return $"Drive {drive.Name} is {drive.DriveType}, not a fixed local disk, so it cannot hold the cache. {fix}";

            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"LocalCacheDirectory '{dir}' cannot be written ({ex.Message}). {fix}";
        }
    }
}
