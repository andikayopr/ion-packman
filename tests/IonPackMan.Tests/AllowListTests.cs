using IonPackMan.Machine;
using Xunit;

namespace IonPackMan.Tests;

public class AllowListTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-" + Guid.NewGuid().ToString("N"));

    public AllowListTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // Contoh dari PRD §7.2.
    private const string ValidIni = """
        [Meta]
        SchemaVersion=1.0

        [BaseDirectories]
        ION=D:\ION
        APCONTAINER=D:\APCONTAINER

        [RegistryKeys]
        WebUI=HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI

        [IniFiles]
        GPFCNFG=D:\APCONTAINER\Config\GPFCNFG.ini

        [Services]
        IonMiddleware=IonMiddleware
        WIND=WIND
        """;

    private AllowList Load(string text = ValidIni)
    {
        string path = Path.Combine(_dir, "allowlist.ini");
        File.WriteAllText(path, text);
        var (list, errors) = AllowList.Load(path);
        Assert.Empty(errors);
        return list!;
    }

    [Fact]
    public void Load_ReadsAllFourSections()
    {
        var list = Load();

        Assert.Equal(2, list.BaseDirectories.Count);
        Assert.Single(list.RegistryKeys);
        Assert.Single(list.IniFiles);
        Assert.Equal(2, list.Services.Count);
    }

    [Fact]
    public void Load_UnknownSection_IsAnError_AndMissingFileToo()
    {
        string path = Path.Combine(_dir, "bad.ini");
        File.WriteAllText(path, "[Meta]\nSchemaVersion=1.0\n[RegistryKey]\nX=y\n");

        var (list, errors) = AllowList.Load(path);
        Assert.Null(list);
        Assert.Contains(errors, e => e.Contains("unknown section [RegistryKey]"));

        var (list2, errors2) = AllowList.Load(Path.Combine(_dir, "nope.ini"));
        Assert.Null(list2);
        Assert.Contains("file not found", Assert.Single(errors2));
    }

    [Fact]
    public void Load_MissingSections_MeansNothingIsAllowed()
    {
        var list = Load("[Meta]\nSchemaVersion=1.0\n");

        Assert.False(list.IsUnderBaseDirectory(@"D:\ION\x"));
        Assert.False(list.IsServiceAllowed("WIND"));
    }

    [Theory]
    [InlineData(@"D:\ION\IonMiddleware", true)]
    [InlineData(@"d:\ion\ionmiddleware\bin", true)]           // tidak peka huruf besar-kecil
    [InlineData(@"D:\APCONTAINER\WIND", true)]
    [InlineData(@"D:\ION\IonMiddleware\", true)]              // garis miring akhir
    [InlineData(@"D:/ION/IonMiddleware", true)]               // garis miring depan
    [InlineData(@"D:\ION", false)]                            // base directory itu sendiri
    [InlineData(@"D:\ION\", false)]
    [InlineData(@"D:\IONX\foo", false)]                       // awalan mirip
    [InlineData(@"D:\ION-OTHER\foo", false)]
    [InlineData(@"D:\ION\..\Windows", false)]                 // keluar lewat ..
    [InlineData(@"D:\ION\a\..\..\Windows\System32", false)]
    [InlineData(@"D:\ION\..\ION\x", true)]                    // ".." yang tetap di dalam
    [InlineData(@"C:\Windows\System32", false)]
    [InlineData(@"ION\IonMiddleware", false)]                 // relatif
    [InlineData(@"\\server\share\ION\x", false)]              // UNC
    [InlineData(@"\\?\D:\ION\x", false)]                      // awalan device
    [InlineData(@"D:\ION\file.txt:stream", false)]            // alternate data stream
    [InlineData(@"D:\ION\a|b", false)]                        // karakter terlarang
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsUnderBaseDirectory(string path, bool expected)
    {
        Assert.Equal(expected, Load().IsUnderBaseDirectory(path));
    }

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI", true)]
    [InlineData(@"hklm\software\wow6432node\hitachiexternalparameter\user\webui", true)]
    [InlineData(@"HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI\", true)]
    [InlineData(@"HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User\WebUI\Sub", false)]  // sub-key
    [InlineData(@"HKLM\SOFTWARE\WOW6432Node\HitachiExternalParameter\User", false)]          // induk
    [InlineData(@"HKLM\SOFTWARE\Microsoft", false)]
    [InlineData("", false)]
    public void IsRegistryKeyAllowed(string key, bool expected)
    {
        Assert.Equal(expected, Load().IsRegistryKeyAllowed(key));
    }

    [Theory]
    [InlineData(@"D:\APCONTAINER\Config\GPFCNFG.ini", true)]
    [InlineData(@"d:\apcontainer\config\gpfcnfg.INI", true)]
    [InlineData(@"D:\APCONTAINER\Config\..\Config\GPFCNFG.ini", true)]
    [InlineData(@"D:\APCONTAINER\Config\Other.ini", false)]
    [InlineData(@"D:\APCONTAINER\Config\GPFCNFG.ini:stream", false)]
    [InlineData(@"Config\GPFCNFG.ini", false)]
    public void IsIniFileAllowed(string path, bool expected)
    {
        Assert.Equal(expected, Load().IsIniFileAllowed(path));
    }

    [Theory]
    [InlineData("WIND", true)]
    [InlineData("wind", true)]
    [InlineData("IonMiddleware", true)]
    [InlineData("Spooler", false)]
    [InlineData("", false)]
    public void IsServiceAllowed(string name, bool expected)
    {
        Assert.Equal(expected, Load().IsServiceAllowed(name));
    }
}
