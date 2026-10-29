using System.IO;

namespace IonPackMan;

/// <summary>
/// Semua lokasi file/folder PackMan ada di sini (System Design: "path di satu tempat").
/// Class lain tidak boleh menyusun path sendiri.
/// </summary>
public static class Paths
{
    public const string SingleInstanceMutexName = @"Global\IonPackMan.SingleInstance";

    /// <summary>Folder rilis PackMan (tempat IonPackMan.exe berada; bisa di USB).</summary>
    public static string ReleaseDirectory => AppContext.BaseDirectory;

    /// <summary>File konfigurasi mesin yang dikelola developer (D9).</summary>
    public const string MachineIni = @"D:\ION-PackMan\machine.ini";

    public static string AllowListIni(string releaseDir) => Path.Combine(releaseDir, "Config", "allowlist.ini");

    /// <summary>Katalog global entri Registry/INI mesin ATM (opsional; tidak ada = kosong).</summary>
    public static string ConfigCatalogIni(string releaseDir) => Path.Combine(releaseDir, "Config", "config-entries.ini");

    /// <summary>Daftar Client (bank) dan component yang memakai branch Client (opsional; tidak ada = semua default). ADR-006.</summary>
    public static string ClientsIni(string releaseDir) => Path.Combine(releaseDir, "Config", "clients.ini");

    public static string ComponentsDir(string releaseDir) => Path.Combine(releaseDir, "Components");

    public static string CatalogIni(string releaseDir) => Path.Combine(ComponentsDir(releaseDir), "catalog.ini");

    public static string ComponentIni(string releaseDir, string componentId, string releaseVersion) =>
        Path.Combine(ComponentsDir(releaseDir), componentId, $"{componentId}-{releaseVersion}.ini");

    public static string ProfilesDir(string releaseDir) => Path.Combine(releaseDir, "Profiles");

    public static string ProfileIni(string releaseDir, string profileId) =>
        Path.Combine(ProfilesDir(releaseDir), $"{profileId}.ini");

    // Data operasional di local cache (D10).
    public static string BuildsDir(string cacheDir) => Path.Combine(cacheDir, "Builds");

    /// <summary>Folder satu build: id@versi untuk default, id@versi@CLIENT untuk build branch Client (ADR-006).</summary>
    public static string BuildDir(string cacheDir, string componentId, string releaseVersion, string client = "") =>
        Path.Combine(BuildsDir(cacheDir), client.Length == 0 ? $"{componentId}@{releaseVersion}" : $"{componentId}@{releaseVersion}@{client}");

    /// <summary>Salinan file build yang diimpor (isi folder build).</summary>
    public static string BuildFilesDir(string cacheDir, string componentId, string releaseVersion, string client = "") =>
        Path.Combine(BuildDir(cacheDir, componentId, releaseVersion, client), "files");

    /// <summary>Metadata build (nama folder sumber, Client, branch dan commit git, jumlah file, waktu import).</summary>
    public static string BuildIni(string cacheDir, string componentId, string releaseVersion, string client = "") =>
        Path.Combine(BuildDir(cacheDir, componentId, releaseVersion, client), "build.ini");

    /// <summary>Daftar hash file build: satu baris per file, "&lt;sha256&gt;  &lt;path relatif&gt;".</summary>
    public static string BuildChecksums(string cacheDir, string componentId, string releaseVersion, string client = "") =>
        Path.Combine(BuildDir(cacheDir, componentId, releaseVersion, client), "files.sha256");

    public static string BackupsDir(string cacheDir) => Path.Combine(cacheDir, "Backups");

    public static string BackupDir(string cacheDir, string execId) => Path.Combine(BackupsDir(cacheDir), execId);

    /// <summary>Salinan seluruh folder target sebelum patch (opsional); bagian dari folder backup eksekusi itu.</summary>
    public static string SnapshotDir(string backupDir) => Path.Combine(backupDir, "snapshot");

    public static string HistoryDir(string cacheDir) => Path.Combine(cacheDir, "History");

    public static string HistoryIni(string cacheDir, string execId) => Path.Combine(HistoryDir(cacheDir), $"{execId}.ini");
}
