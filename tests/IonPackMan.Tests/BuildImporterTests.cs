using System.Diagnostics;
using System.Security.Cryptography;
using IonPackMan.Builds;
using Xunit;

namespace IonPackMan.Tests;

public class BuildImporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ionpackman-build-" + Guid.NewGuid().ToString("N"));
    private readonly string _cache;
    private readonly string _src;
    private readonly BuildImporter _importer;

    public BuildImporterTests()
    {
        _cache = Path.Combine(_root, "cache");
        _src = Path.Combine(_root, "WIND 2.1.0");
        Directory.CreateDirectory(_src);
        Directory.CreateDirectory(Path.Combine(_src, "bin", "plugins"));
        File.WriteAllText(Path.Combine(_src, "wind.exe"), "exe-bytes");
        File.WriteAllText(Path.Combine(_src, "bin", "core.dll"), "core");
        File.WriteAllText(Path.Combine(_src, "bin", "plugins", "p1.dll"), "plugin-one");
        _importer = new BuildImporter(_cache, _ => long.MaxValue / 2);
    }

    public void Dispose()
    {
        // Junction dilepas dulu supaya Delete(recursive) tidak mengikutinya.
        // Hanya dua level yang mungkin berisi junction (tes ini); tidak ada enumerasi yang mengikutinya.
        foreach (var d in Directory.GetDirectories(_root).Concat(Directory.GetDirectories(_src)))
            if (new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint))
                Directory.Delete(d);
        Directory.Delete(_root, recursive: true);
    }

    private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    // ---------- Import berhasil ----------

    [Fact]
    public void Import_CopiesFilesWithSubfoldersAndHashesMatch()
    {
        var (build, errors) = _importer.Import("wind", "2.1.0", _src);

        Assert.Empty(errors);
        Assert.NotNull(build);
        Assert.Equal(3, build!.FileCount);
        Assert.Equal("WIND 2.1.0", build.SourceFolderName);

        string files = Paths.BuildFilesDir(_cache, "wind", "2.1.0");
        Assert.Equal("exe-bytes", File.ReadAllText(Path.Combine(files, "wind.exe")));
        Assert.Equal("plugin-one", File.ReadAllText(Path.Combine(files, "bin", "plugins", "p1.dll")));
        Assert.Equal(new FileInfo(Path.Combine(_src, "wind.exe")).Length
                     + new FileInfo(Path.Combine(_src, "bin", "core.dll")).Length
                     + new FileInfo(Path.Combine(_src, "bin", "plugins", "p1.dll")).Length, build.TotalBytes);

        var lines = File.ReadAllLines(Paths.BuildChecksums(_cache, "wind", "2.1.0"));
        Assert.Equal(3, lines.Length);
        Assert.Contains($"{Sha(Path.Combine(_src, "bin", "plugins", "p1.dll"))}  bin/plugins/p1.dll", lines);
        Assert.Contains($"{Sha(Path.Combine(_src, "wind.exe"))}  wind.exe", lines);
    }

    [Fact]
    public void Import_WritesBuildIni_AndLoadReadsItBack()
    {
        var (imported, _) = _importer.Import("wind", "2.1.0", _src);

        Assert.True(File.Exists(Paths.BuildIni(_cache, "wind", "2.1.0")));
        var (loaded, errors) = _importer.Load("wind", "2.1.0");

        Assert.Empty(errors);
        Assert.Equal(imported, loaded);
    }

    [Fact]
    public void Import_ReportsProgress_AndLeavesNoStagingFolder()
    {
        var seen = new List<(int, int)>();

        _importer.Import("wind", "2.1.0", _src, (i, n) => seen.Add((i, n)));

        Assert.Equal([(1, 3), (2, 3), (3, 3)], seen);
        Assert.Equal("wind@2.1.0", Path.GetFileName(Assert.Single(Directory.GetDirectories(Paths.BuildsDir(_cache)))));
    }

    // ---------- Verify ----------

    [Fact]
    public void Verify_FreshImport_IsValid()
    {
        _importer.Import("wind", "2.1.0", _src);

        Assert.Empty(_importer.Verify("wind", "2.1.0"));
    }

    [Fact]
    public void Verify_DetectsChangedMissingAndExtraFiles()
    {
        _importer.Import("wind", "2.1.0", _src);
        string files = Paths.BuildFilesDir(_cache, "wind", "2.1.0");
        File.WriteAllText(Path.Combine(files, "wind.exe"), "tampered");
        File.Delete(Path.Combine(files, "bin", "core.dll"));
        File.WriteAllText(Path.Combine(files, "bin", "extra.txt"), "x");

        var problems = _importer.Verify("wind", "2.1.0");

        Assert.Contains(problems, p => p.Contains("Changed") && p.Contains("wind.exe"));
        Assert.Contains(problems, p => p.Contains("Missing") && p.Contains("bin/core.dll"));
        Assert.Contains(problems, p => p.Contains("Unexpected") && p.Contains("bin/extra.txt"));
        Assert.Equal(3, problems.Count);
    }

    [Fact]
    public void Verify_UnknownBuild_ReturnsError()
    {
        Assert.NotEmpty(_importer.Verify("wind", "9.9.9"));
    }

    // ---------- Import ulang ----------

    [Fact]
    public void Import_Again_ReplacesOldBuildCompletely()
    {
        _importer.Import("wind", "2.1.0", _src);
        File.Delete(Path.Combine(_src, "bin", "core.dll"));
        File.WriteAllText(Path.Combine(_src, "wind.exe"), "new-exe");

        var (build, errors) = _importer.Import("wind", "2.1.0", _src);

        Assert.Empty(errors);
        Assert.Equal(2, build!.FileCount);
        string files = Paths.BuildFilesDir(_cache, "wind", "2.1.0");
        Assert.Equal("new-exe", File.ReadAllText(Path.Combine(files, "wind.exe")));
        Assert.False(File.Exists(Path.Combine(files, "bin", "core.dll")));
        Assert.Empty(_importer.Verify("wind", "2.1.0"));
        Assert.Single(Directory.GetDirectories(Paths.BuildsDir(_cache)));
    }

    [Fact]
    public void Import_FailureDuringReimport_KeepsOldBuild()
    {
        _importer.Import("wind", "2.1.0", _src);
        using var locked = new FileStream(Path.Combine(_src, "bin", "core.dll"), FileMode.Open, FileAccess.Read, FileShare.None);

        var (build, errors) = _importer.Import("wind", "2.1.0", _src);

        Assert.Null(build);
        Assert.NotEmpty(errors);
        Assert.Empty(_importer.Verify("wind", "2.1.0"));
        Assert.Single(Directory.GetDirectories(Paths.BuildsDir(_cache)));
    }

    [Fact]
    public void Import_OtherBuilds_AreNeverTouched()
    {
        _importer.Import("wind", "2.0.0", _src);
        string marker = Path.Combine(Paths.BuildDir(_cache, "wind", "2.0.0"), "keep.txt");
        File.WriteAllText(marker, "keep");

        _importer.Import("wind", "2.1.0", _src);

        Assert.True(File.Exists(marker));
        Assert.Equal(2, _importer.ListBuilds().Count);
    }

    // ---------- Penolakan ----------

    [Fact]
    public void Import_NotEnoughDiskSpace_IsRejected_AndWritesNothing()
    {
        var tight = new BuildImporter(_cache, _ => 5);

        var (build, errors) = tight.Import("wind", "2.1.0", _src);

        Assert.Null(build);
        Assert.Contains(errors, e => e.Contains("Not enough disk space"));
        Assert.False(Directory.Exists(_cache));
    }

    [Fact]
    public void Import_FreeSpaceEqualToSizeWithoutMargin_IsRejected()
    {
        long size = 9 + 4 + 10; // wind.exe + core.dll + p1.dll
        var tight = new BuildImporter(_cache, _ => size);

        Assert.NotEmpty(tight.Import("wind", "2.1.0", _src).Errors);
    }

    [Fact]
    public void Import_MissingSourceFolder_IsRejected()
    {
        var (build, errors) = _importer.Import("wind", "2.1.0", Path.Combine(_root, "nope"));

        Assert.Null(build);
        Assert.Contains(errors, e => e.Contains("does not exist"));
    }

    [Fact]
    public void Import_EmptySourceFolder_IsRejected()
    {
        string empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(Path.Combine(empty, "subfolder"));

        var (build, errors) = _importer.Import("wind", "2.1.0", empty);

        Assert.Null(build);
        Assert.Contains(errors, e => e.Contains("no files"));
        Assert.False(Directory.Exists(_cache));
    }

    [Fact]
    public void Import_SourceInsideCache_IsRejected()
    {
        _importer.Import("wind", "2.1.0", _src);

        var (build, errors) = _importer.Import("wind", "2.2.0", Paths.BuildFilesDir(_cache, "wind", "2.1.0"));

        Assert.Null(build);
        Assert.Contains(errors, e => e.Contains("inside the local cache"));
    }

    [Fact]
    public void Import_SourceContainingCache_IsRejected()
    {
        var (build, errors) = _importer.Import("wind", "2.1.0", _root);

        Assert.Null(build);
        Assert.Contains(errors, e => e.Contains("contains the local cache"));
    }

    [Theory]
    [InlineData("..", "1.0")]
    [InlineData("wind", "..")]
    [InlineData("a@b", "1.0")]
    [InlineData("wind", "1@0")]
    [InlineData("wind", @"..\x")]
    [InlineData("", "1.0")]
    [InlineData("Wind", "1.0")]
    public void Import_DangerousIdOrVersion_IsRejected(string id, string version)
    {
        var (build, errors) = _importer.Import(id, version, _src);

        Assert.Null(build);
        Assert.Contains(errors, e => e.Contains("not a valid component release"));
        Assert.False(Directory.Exists(_cache));
    }

    [Fact]
    public void Import_SourceContainingJunction_IsRejected()
    {
        string target = Path.Combine(_root, "outside");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "secret.txt"), "s");
        MakeJunction(Path.Combine(_src, "link"), target);

        var (build, errors) = _importer.Import("wind", "2.1.0", _src);

        Assert.Null(build);
        Assert.Contains(errors, e => e.Contains("symbolic link or junction"));
        Assert.False(Directory.Exists(_cache));
    }

    [Fact]
    public void Import_SourceFolderItselfIsJunction_IsRejected()
    {
        string link = Path.Combine(_root, "link-to-src");
        MakeJunction(link, _src);

        var (build, errors) = _importer.Import("wind", "2.1.0", link);

        Assert.Null(build);
        Assert.Contains(errors, e => e.Contains("symbolic link or junction"));
    }

    // ---------- List ----------

    [Fact]
    public void ListBuilds_ShowsFolderNameAndFileCount()
    {
        Assert.Empty(_importer.ListBuilds());
        _importer.Import("wind", "2.1.0", _src);

        var list = _importer.ListBuilds();

        var only = Assert.Single(list);
        Assert.Equal("WIND 2.1.0 · 3 files", only.Display);
        Assert.Equal(("wind", "2.1.0"), (only.ComponentId, only.ReleaseVersion));
    }

    private static void MakeJunction(string link, string target)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }
}
