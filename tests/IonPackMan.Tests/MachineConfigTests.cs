using IonPackMan.Machine;
using Xunit;

namespace IonPackMan.Tests;

public class MachineConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));

    public MachineConfigTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // Contoh dari PRD §7.2 (termasuk komentar di ujung baris).
    private const string ValidIni = """
        [Meta]
        SchemaVersion=1.0

        [Machine]
        MachineName=ATM-DEV-001
        Environment=Development        ; Development | Test | Production
        Profile=                       ; opsional

        [InstallLocations]
        ION=D:\ION
        APCONTAINER=D:\APCONTAINER

        [PackMan]
        LocalCacheDirectory=D:\ION-PackMan\Cache
        """;

    private string Write(string text)
    {
        string path = Path.Combine(_dir, "machine.ini");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Load_ValidFile_ReadsAllFields()
    {
        var (config, errors) = MachineConfig.Load(Write(ValidIni));

        Assert.Empty(errors);
        Assert.NotNull(config);
        Assert.Equal("ATM-DEV-001", config.MachineName);
        Assert.Equal("Development", config.Environment);
        Assert.Equal("", config.Profile);
        Assert.Equal(@"D:\ION", config.InstallLocations["ION"]);
        Assert.Equal(@"D:\APCONTAINER", config.InstallLocations["apcontainer"]);
        Assert.Equal(@"D:\ION-PackMan\Cache", config.LocalCacheDirectory);
        Assert.True(config.IsDevelopment);
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("Production")]
    public void IsDevelopment_FalseForOtherEnvironments(string environment)
    {
        var (config, errors) = MachineConfig.Load(Write(ValidIni.Replace("Environment=Development", "Environment=" + environment)));

        Assert.Empty(errors);
        Assert.False(config!.IsDevelopment);
    }

    [Fact]
    public void Load_MissingFile_ReturnsErrorNotException()
    {
        var (config, errors) = MachineConfig.Load(Path.Combine(_dir, "nope.ini"));

        Assert.Null(config);
        Assert.Contains("file not found", Assert.Single(errors));
    }

    [Fact]
    public void Load_BrokenSyntax_ReportsLineNumber()
    {
        var (config, errors) = MachineConfig.Load(Write("[Meta]\nSchemaVersion=1.0\nthis is wrong\n"));

        Assert.Null(config);
        Assert.Contains("line 3", Assert.Single(errors));
    }

    [Fact]
    public void Load_ReportsAllProblemsAtOnce()
    {
        var (config, errors) = MachineConfig.Load(Write("""
            [Meta]
            SchemaVersion=2.0
            [Machine]
            MachineName=
            Environment=Staging
            Typo=1
            [Surprise]
            X=1
            """));

        Assert.Null(config);
        Assert.Contains(errors, e => e.Contains("SchemaVersion '2.0' is not supported"));
        Assert.Contains(errors, e => e.Contains("required key 'MachineName' is empty"));
        Assert.Contains(errors, e => e.Contains("Environment 'Staging' must be one of"));
        Assert.Contains(errors, e => e.Contains("unknown key 'Typo'"));
        Assert.Contains(errors, e => e.Contains("unknown section [Surprise]"));
        Assert.Contains(errors, e => e.Contains("missing section [InstallLocations]"));
        Assert.Contains(errors, e => e.Contains("missing section [PackMan]"));
    }

    private static MachineConfig WithCache(string dir) => new()
    {
        MachineName = "M", Environment = "Development", InstallLocations = new Dictionary<string, string>(),
        LocalCacheDirectory = dir,
    };

    [Fact]
    public void CheckCacheDirectory_FixedDisk_IsOk_AndCreatesFolder()
    {
        string cache = Path.Combine(_dir, "Cache");

        Assert.Null(WithCache(cache).CheckCacheDirectory());
        Assert.True(Directory.Exists(cache));
        Assert.Empty(Directory.GetFiles(cache)); // file uji tulis sudah dihapus
    }

    [Theory]
    [InlineData(@"Cache")]                     // relatif
    [InlineData(@"\\server\share\Cache")]      // jaringan/UNC
    [InlineData(@"\\?\D:\Cache")]              // awalan device
    public void CheckCacheDirectory_NotALocalDrivePath_IsRejected_WithFixHint(string dir)
    {
        string? problem = WithCache(dir).CheckCacheDirectory();

        Assert.NotNull(problem);
        Assert.Contains("machine.ini", problem);
    }

    [Fact]
    public void CheckCacheDirectory_UnavailableDrive_IsRejected()
    {
        // Cari huruf drive yang tidak terpakai agar tes tidak bergantung pada mesin.
        char free = Enumerable.Range('D', 'Z' - 'D' + 1).Select(c => (char)c)
            .First(c => !DriveInfo.GetDrives().Any(d => char.ToUpperInvariant(d.Name[0]) == c));

        string? problem = WithCache($@"{free}:\Cache").CheckCacheDirectory();

        Assert.NotNull(problem);
        Assert.Contains("machine.ini", problem);
    }
}
