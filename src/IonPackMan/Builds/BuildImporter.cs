using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using IonPackMan.Components;
using IonPackMan.Ini;

namespace IonPackMan.Builds;

/// <summary>
/// Mengimpor folder build ke local cache (PRD R5, D8, D10): salin ke Builds\&lt;id&gt;@&lt;versi&gt;\files\,
/// hitung SHA-256 tiap file, tulis build.ini dan files.sha256, serta memeriksa ulang (Verify) kapan saja.
/// Tidak pernah menghapus build lain; semua penulisan terbatas pada folder build id@versi yang diimpor
/// (plus folder sementara bersaudara "id@versi~importing" yang selalu dibersihkan).
/// </summary>
public sealed class BuildImporter
{
    private const string StagingSuffix = "~importing"; // '~' tidak lolos IsValidVersion, jadi tidak bisa bentrok dengan versi asli
    private const string OldSuffix = "~old";
    private const long SpaceMarginBytes = 1024 * 1024;

    private readonly string _cacheDir;
    private readonly Func<string, long> _freeSpace;

    public BuildImporter(string cacheDir, Func<string, long>? freeSpace = null)
    {
        _cacheDir = cacheDir;
        _freeSpace = freeSpace ?? DefaultFreeSpace;
    }

    private static long DefaultFreeSpace(string path) =>
        new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace;

    /// <summary>Apakah build id@versi sudah ada (layar bisa meminta konfirmasi sebelum import ulang menggantikannya).</summary>
    public bool Exists(string componentId, string releaseVersion) =>
        ComponentRelease.IsValidId(componentId) && ComponentRelease.IsValidVersion(releaseVersion)
        && Directory.Exists(Paths.BuildDir(_cacheDir, componentId, releaseVersion));

    // ---------- Import ----------

    public (BuildInfo? Build, List<string> Errors) Import(
        string componentId, string releaseVersion, string sourceFolder, Action<int, int>? progress = null)
    {
        if (!ComponentRelease.IsValidId(componentId) || !ComponentRelease.IsValidVersion(releaseVersion))
            return (null, [$"'{componentId}@{releaseVersion}' is not a valid component release."]);

        string source;
        string cache;
        try
        {
            source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceFolder));
            cache = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_cacheDir));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return (null, [$"'{sourceFolder}' is not a valid folder path."]);
        }

        if (!Directory.Exists(source))
            return (null, [$"Source folder '{source}' does not exist."]);
        if (IsInside(source, cache))
            return (null, [$"Source folder '{source}' is inside the local cache; choose the original build folder."]);
        if (IsInside(cache, source))
            return (null, [$"Source folder '{source}' contains the local cache; choose a folder that does not contain it."]);

        var (files, scanErrors) = ScanSource(source);
        if (scanErrors.Count > 0)
            return (null, scanErrors);
        if (files.Count == 0)
            return (null, [$"Source folder '{source}' contains no files."]);

        long total = files.Sum(f => f.Length);
        long needed = total + SpaceMarginBytes;
        long free;
        try
        {
            free = _freeSpace(cache);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (null, [$"Cannot determine free disk space for '{cache}': {ex.Message}"]);
        }
        if (free < needed)
            return (null, [$"Not enough disk space in '{cache}': build needs {FormatSize(needed)} (including a small margin) but only {FormatSize(free)} is free."]);

        // Salin ke folder sementara lebih dulu; baru ditukar setelah semuanya selesai.
        string stagingVersion = releaseVersion + StagingSuffix;
        string stagingDir = Paths.BuildDir(_cacheDir, componentId, stagingVersion);
        string finalDir = Paths.BuildDir(_cacheDir, componentId, releaseVersion);
        string oldDir = Paths.BuildDir(_cacheDir, componentId, releaseVersion + OldSuffix);

        try
        {
            DeleteIfExists(stagingDir); // sisa import yang terputus (milik kita sendiri)
            string stagingFiles = Paths.BuildFilesDir(_cacheDir, componentId, stagingVersion);
            Directory.CreateDirectory(stagingFiles);

            var lines = new StringBuilder();
            for (int i = 0; i < files.Count; i++)
            {
                string relative = files[i].Relative;
                string hash = CopyAndHash(files[i].FullPath, Path.Combine(stagingFiles, relative));
                lines.Append(hash).Append("  ").Append(relative.Replace('\\', '/')).Append("\r\n");
                progress?.Invoke(i + 1, files.Count);
            }

            var info = new BuildInfo(componentId, releaseVersion, FolderName(source), files.Count, total, DateTimeOffset.Now);
            ToIni(info).Save(Paths.BuildIni(_cacheDir, componentId, stagingVersion));
            File.WriteAllText(Paths.BuildChecksums(_cacheDir, componentId, stagingVersion), lines.ToString(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Swap(stagingDir, finalDir, oldDir);
            return (info, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, [$"Cannot import {componentId}@{releaseVersion}: {ex.Message}"]);
        }
        finally
        {
            TryDelete(stagingDir);
        }
    }

    // Versi lama dipindah ke "~old", yang baru dipindah masuk; bila gagal, versi lama dikembalikan.
    private static void Swap(string stagingDir, string finalDir, string oldDir)
    {
        bool hadOld = Directory.Exists(finalDir);
        if (hadOld)
        {
            DeleteIfExists(oldDir);
            Directory.Move(finalDir, oldDir);
        }
        try
        {
            Directory.Move(stagingDir, finalDir);
        }
        catch
        {
            if (hadOld)
                Directory.Move(oldDir, finalDir);
            throw;
        }
        if (hadOld)
            TryDelete(oldDir);
    }

    // ---------- Load / Verify / List ----------

    public (BuildInfo? Build, List<string> Errors) Load(string componentId, string releaseVersion)
    {
        if (!ComponentRelease.IsValidId(componentId) || !ComponentRelease.IsValidVersion(releaseVersion))
            return (null, [$"'{componentId}@{releaseVersion}' is not a valid component release."]);

        string path = Paths.BuildIni(_cacheDir, componentId, releaseVersion);
        var (ini, errors) = IniSchema.TryLoad(path);
        if (ini is null)
            return (null, errors);

        errors.AddRange(IniSchema.CheckMeta(ini));
        errors.AddRange(ini.CheckSections("Meta", "Build"));
        errors.AddRange(ini.CheckKeys("Build",
            required: ["ComponentId", "ReleaseVersion", "SourceFolderName", "FileCount", "TotalBytes", "ImportedAt"], optional: []));
        if (errors.Count > 0)
            return (null, errors.Select(e => $"{path}: {e}").ToList());

        string id = ini.Get("Build", "ComponentId")!, version = ini.Get("Build", "ReleaseVersion")!;
        if (id != componentId || version != releaseVersion)
            errors.Add($"file declares {id}@{version} but its folder says {componentId}@{releaseVersion}.");
        if (!int.TryParse(ini.Get("Build", "FileCount"), NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            errors.Add("[Build] FileCount is not a valid number.");
        if (!long.TryParse(ini.Get("Build", "TotalBytes"), NumberStyles.None, CultureInfo.InvariantCulture, out long bytes))
            errors.Add("[Build] TotalBytes is not a valid number.");
        if (!DateTimeOffset.TryParse(ini.Get("Build", "ImportedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
            errors.Add("[Build] ImportedAt is not a valid ISO-8601 time.");
        if (errors.Count > 0)
            return (null, errors.Select(e => $"{path}: {e}").ToList());

        return (new BuildInfo(id, version, ini.Get("Build", "SourceFolderName")!, count, bytes, at), []);
    }

    /// <summary>Membandingkan isi folder files dengan files.sha256. Daftar kosong = build valid.</summary>
    public List<string> Verify(string componentId, string releaseVersion, Action<int, int>? progress = null)
    {
        var (info, loadErrors) = Load(componentId, releaseVersion);
        if (info is null)
            return loadErrors;

        string filesDir = Paths.BuildFilesDir(_cacheDir, componentId, releaseVersion);
        string sumsPath = Paths.BuildChecksums(_cacheDir, componentId, releaseVersion);
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadAllLines(sumsPath, Encoding.UTF8))
            {
                if (line.Length == 0) continue;
                if (line.Length <= 66 || line.IndexOf("  ", StringComparison.Ordinal) != 64)
                    return [$"{sumsPath}: malformed line '{line}'."];
                if (!expected.TryAdd(line[66..], line[..64].ToLowerInvariant()))
                    return [$"{sumsPath}: duplicate entry '{line[66..]}'."];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"{sumsPath}: cannot be read ({ex.Message})."];
        }

        var problems = new List<string>();
        if (expected.Count != info.FileCount)
            problems.Add($"build.ini says {info.FileCount} files but files.sha256 lists {expected.Count}.");

        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(filesDir))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories))
                    actual.Add(Path.GetRelativePath(filesDir, f).Replace('\\', '/'));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Cannot list '{filesDir}': {ex.Message}");
            }
        }
        else
        {
            problems.Add($"Folder '{filesDir}' is missing.");
        }

        int done = 0;
        foreach (var (relative, hash) in expected.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            progress?.Invoke(++done, expected.Count);
            if (!actual.Contains(relative))
            {
                problems.Add($"Missing file: {relative}");
                continue;
            }
            try
            {
                using var stream = new FileStream(Path.Combine(filesDir, relative.Replace('/', '\\')), FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"Changed file (hash differs): {relative}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Cannot read file {relative}: {ex.Message}");
            }
        }

        foreach (var extra in actual.Where(a => !expected.ContainsKey(a)).OrderBy(a => a, StringComparer.Ordinal))
            problems.Add($"Unexpected file: {extra}");

        return problems;
    }

    /// <summary>Semua build di cache, diurutkan menurut id lalu versi. Folder sementara/bukan build dilewati.</summary>
    public List<BuildSummary> ListBuilds()
    {
        string root = Paths.BuildsDir(_cacheDir);
        var result = new List<BuildSummary>();
        if (!Directory.Exists(root))
            return result;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                string name = Path.GetFileName(dir);
                int at = name.IndexOf('@');
                if (at <= 0) continue;
                string id = name[..at], version = name[(at + 1)..];
                if (!ComponentRelease.IsValidId(id) || !ComponentRelease.IsValidVersion(version)) continue;

                var (info, _) = Load(id, version);
                result.Add(new BuildSummary(id, version, info?.SourceFolderName, info?.FileCount ?? 0));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Folder Builds tidak terbaca: tampilkan apa yang sudah terkumpul.
        }

        return result.OrderBy(b => b.ComponentId, StringComparer.Ordinal).ThenBy(b => b.ReleaseVersion, StringComparer.Ordinal).ToList();
    }

    // ---------- helper ----------

    private sealed record SourceFile(string FullPath, string Relative, long Length);

    // Mengumpulkan semua file sumber; reparse point (symlink/junction) dan folder/file yang tidak terbaca = error.
    private static (List<SourceFile> Files, List<string> Errors) ScanSource(string source)
    {
        var files = new List<SourceFile>();
        var errors = new List<string>();
        if (IsReparsePoint(source))
            errors.Add($"Source folder '{source}' is a symbolic link or junction, which is not allowed.");
        else
            Walk(source, source, files, errors);
        files.Sort((a, b) => string.CompareOrdinal(a.Relative, b.Relative));
        return (files, errors);
    }

    private static void Walk(string root, string dir, List<SourceFile> files, List<string> errors)
    {
        try
        {
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    errors.Add($"'{entry.FullName}' is a symbolic link or junction, which is not allowed.");
                else if (entry is DirectoryInfo sub)
                    Walk(root, sub.FullName, files, errors);
                else if (entry is FileInfo file)
                    files.Add(new SourceFile(file.FullName, Path.GetRelativePath(root, file.FullName), file.Length));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Cannot read folder '{dir}': {ex.Message}");
        }
    }

    private static bool IsReparsePoint(string path) =>
        new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);

    // Satu kali baca: menyalin sekaligus menghitung hash.
    private static string CopyAndHash(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static IniFile ToIni(BuildInfo info)
    {
        var ini = new IniFile();
        ini.Set("Meta", "SchemaVersion", IniSchema.CurrentVersion);
        ini.Set("Build", "ComponentId", info.ComponentId);
        ini.Set("Build", "ReleaseVersion", info.ReleaseVersion);
        ini.Set("Build", "SourceFolderName", info.SourceFolderName);
        ini.Set("Build", "FileCount", info.FileCount.ToString(CultureInfo.InvariantCulture));
        ini.Set("Build", "TotalBytes", info.TotalBytes.ToString(CultureInfo.InvariantCulture));
        ini.Set("Build", "ImportedAt", info.ImportedAt.ToString("o", CultureInfo.InvariantCulture));
        return ini;
    }

    // Nama folder harus aman ditulis ke INI (tanpa spasi di ujung dan tanpa " ;").
    private static string FolderName(string source)
    {
        string name = Path.GetFileName(source).Replace(" ;", " ,").Trim();
        return name.Length == 0 ? "(root)" : name;
    }

    private static bool IsInside(string path, string parent)
    {
        string p = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
        string q = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        return p.StartsWith(q, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteIfExists(string dir)
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static void TryDelete(string dir)
    {
        try { DeleteIfExists(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string FormatSize(long bytes) => $"{bytes / (1024.0 * 1024.0):0.##} MB";
}
