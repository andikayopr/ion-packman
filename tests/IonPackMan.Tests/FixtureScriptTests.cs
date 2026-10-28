using System.Diagnostics;
using IonPackMan.Components;
using IonPackMan.Machine;
using IonPackMan.Profiles;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>
/// Skrip uji manual (tools\PackManTestFixture.ps1) menulis konfigurasi PackMan sendiri. Tes ini menjalankan HANYA aksi ConfigOnly
/// (menulis file di folder sementara; tanpa Registry, service, atau admin) lalu memuatnya dengan kode aplikasi, supaya skema
/// skrip tidak diam-diam usang setiap skema INI berubah.
/// </summary>
public class FixtureScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ionpackman-fixture-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static string? FindScript()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tools", "PackManTestFixture.ps1");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    [Fact]
    public void ConfigOnly_WritesFilesThatThePackManLoadersAccept()
    {
        string? script = FindScript();
        if (script is null) return;   // folder tools tidak ada di lingkungan ini (mis. hanya binari yang disalin)

        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Action ConfigOnly -Root \"{_root}\"")
        { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(psi)!;
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, "fixture failed: " + stderr);

        // Tidak ada yang ditulis di luar folder uji.
        Assert.True(File.Exists(Path.Combine(_root, "machine.ini")));

        var (machine, machineErrors) = MachineConfig.Load(Path.Combine(_root, "machine.ini"));
        Assert.Empty(machineErrors);
        Assert.True(machine!.IsDevelopment);

        string release = Path.Combine(_root, "release");
        var (allow, allowErrors) = AllowList.Load(Paths.AllowListIni(release));
        Assert.Empty(allowErrors);

        var components = new ComponentStore(release);
        var (wind, windErrors) = components.Load("wind", "2.1.0");
        Assert.Empty(windErrors);
        Assert.Empty(wind!.Validate(allow));   // target di sandbox, service terdaftar

        var validator = new ProfileValidator(components, allow!);
        var profiles = new ProfileStore(release, validator);
        var (profile, profileErrors) = profiles.Load("WINDTEST");
        Assert.Empty(profileErrors);
        Assert.Empty(validator.Validate(profile!));
        Assert.Equal(machine.LocalCacheDirectory, Path.Combine(_root, "Cache"));
    }
}
