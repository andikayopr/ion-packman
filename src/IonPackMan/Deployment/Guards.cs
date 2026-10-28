using System.IO;
using IonPackMan.Builds;
using IonPackMan.Machine;

namespace IonPackMan.Deployment;

/// <summary>
/// Pemeriksaan keselamatan (System Design §3.6). Dipanggil saat Preview <b>dan dipanggil ulang tepat sebelum eksekusi</b>
/// di <see cref="Deployer"/>, karena aplikasi berjalan sebagai admin: keamanan tidak boleh bergantung pada layar.
/// Mengembalikan semua masalah sekaligus; daftar kosong = boleh lanjut.
/// </summary>
public sealed class Guards
{
    private const long SpaceMargin = 1024 * 1024;

    private readonly MachineConfig _machine;
    private readonly AllowList _allowList;
    private readonly BuildImporter _builds;
    private readonly Func<List<string>> _unfinishedBackups;
    private readonly Func<string, long> _freeSpace;

    /// <param name="unfinishedBackups">Baris deskripsi backup yang belum selesai/bermasalah; kosong = tidak ada.</param>
    /// <param name="freeSpace">Ruang kosong (byte) pada drive sebuah path; default memakai DriveInfo (parameter ini agar bisa dites).</param>
    public Guards(MachineConfig machine, AllowList allowList, BuildImporter builds,
        Func<List<string>> unfinishedBackups, Func<string, long>? freeSpace = null)
    {
        _machine = machine;
        _allowList = allowList;
        _builds = builds;
        _unfinishedBackups = unfinishedBackups;
        _freeSpace = freeSpace ?? (path => new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace);
    }

    public List<string> Check(DeploymentPlan plan)
    {
        var problems = new List<string>();

        CheckEnvironment(problems);
        CheckPaths(plan, problems);
        CheckAllowList(plan, problems);
        CheckBuild(plan, problems);
        CheckDisk(plan, problems);
        problems.AddRange(_unfinishedBackups().Select(b => $"An unfinished backup must be restored first: {b}"));
        return problems;
    }

    private void CheckEnvironment(List<string> problems)
    {
        if (!_machine.IsDevelopment)
            problems.Add($"The Developer Console only runs on Development machines (this machine is '{_machine.Environment}').");
    }

    private void CheckPaths(DeploymentPlan plan, List<string> problems)
    {
        if (!_allowList.IsUnderBaseDirectory(plan.TargetDirectory))
        {
            problems.Add($"Target directory '{plan.TargetDirectory}' is not inside an allowed base directory.");
            return;   // pemeriksaan file di bawahnya tidak berarti bila direktorinya sendiri ditolak
        }

        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.TargetDirectory));
        foreach (var file in plan.Files)
        {
            string full = Path.GetFullPath(file.TargetPath);
            if (!full.StartsWith(target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                problems.Add($"File '{file.RelativePath}' would be written outside the target directory.");
            else if (File.Exists(full) && IsReparsePoint(full))
                problems.Add($"Target file '{file.RelativePath}' is a link; refusing to write through it.");

            string source = Path.GetFullPath(file.SourcePath);
            string build = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.BuildFilesDirectory));
            if (!source.StartsWith(build + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                problems.Add($"Source of '{file.RelativePath}' is outside the imported build.");
        }

        // Junction/symlink di target atau salah satu induknya (sampai base directory) dapat mengalihkan tulisan ke luar.
        for (var dir = new DirectoryInfo(target); dir is not null && _allowList.IsUnderBaseDirectory(dir.FullName); dir = dir.Parent)
            if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                problems.Add($"'{dir.FullName}' is a link (junction/symlink); refusing to deploy through it.");
    }

    private void CheckAllowList(DeploymentPlan plan, List<string> problems)
    {
        foreach (var r in plan.RegistryChanges)
            if (!_allowList.IsRegistryKeyAllowed(r.Key))
                problems.Add($"Registry key '{r.Key}' is not in the allow-list.");

        foreach (var i in plan.IniChanges)
            if (!_allowList.IsIniFileAllowed(i.File))
                problems.Add($"INI file '{i.File}' is not in the allow-list.");

        if (plan.ServiceName.Length > 0 && !_allowList.IsServiceAllowed(plan.ServiceName))
            problems.Add($"Service '{plan.ServiceName}' is not in the allow-list.");
    }

    private void CheckBuild(DeploymentPlan plan, List<string> problems)
    {
        if (!plan.DeployFiles)
            return;
        problems.AddRange(_builds.Verify(plan.ComponentId, plan.ReleaseVersion)
            .Select(p => $"Imported build {plan.ComponentId}@{plan.ReleaseVersion} is not valid: {p}"));
    }

    private void CheckDisk(DeploymentPlan plan, List<string> problems)
    {
        if (!plan.DeployFiles)
            return;

        long backupBytes = plan.Files.Where(f => f.Change == FileChange.Replace).Sum(f => f.Size);
        long copyBytes = plan.Files.Where(f => f.Change != FileChange.Same).Sum(f => f.Size);

        TryCheck(_machine.LocalCacheDirectory, backupBytes, "backup", problems);
        TryCheck(plan.TargetDirectory, copyBytes, "copy", problems);
    }

    private void TryCheck(string path, long needed, string purpose, List<string> problems)
    {
        try
        {
            long free = _freeSpace(path);
            if (free < needed + SpaceMargin)
                problems.Add($"Not enough free disk space for the {purpose}: need {(needed + SpaceMargin) / (1024 * 1024)} MB, have {free / (1024 * 1024)} MB on {Path.GetPathRoot(path)}.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            problems.Add($"Cannot read free disk space for the {purpose} ({path}): {ex.Message}");
        }
    }

    private static bool IsReparsePoint(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
