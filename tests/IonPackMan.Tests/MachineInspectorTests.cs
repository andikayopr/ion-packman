using System.Diagnostics;
using IonPackMan.Machine;
using Xunit;

namespace IonPackMan.Tests;

public class MachineInspectorTests
{
    private readonly MachineInspector _inspector = new();

    [Fact]
    public void Inspect_ReturnsMachineNameAndWindowsVersion()
    {
        var snap = _inspector.Inspect();

        Assert.Equal(Environment.MachineName, snap.MachineName);
        Assert.StartsWith("Windows", snap.WindowsVersion);
        // Seperti winver: nomor build ikut tampil ("OS Build 26100.xxxx").
        Assert.Contains($"OS Build {Environment.OSVersion.Version.Build}", snap.WindowsVersion);
    }

    [Theory]
    [InlineData("Windows 10 Enterprise", "22H2", "19045", 5440, "Windows 10 Enterprise · Version 22H2 (OS Build 19045.5440)")]
    [InlineData("Windows 10 Enterprise", "24H2", "26100", 2605, "Windows 11 Enterprise · Version 24H2 (OS Build 26100.2605)")]   // Windows 11 masih ditulis "Windows 10" di Registry
    [InlineData("Windows 10 IoT Enterprise LTSC 2021", "21H2", "19044", 3086, "Windows 10 IoT Enterprise LTSC 2021 · Version 21H2 (OS Build 19044.3086)")]
    [InlineData("Windows 10 Pro", null, "19045", null, "Windows 10 Pro · (OS Build 19045)")]
    [InlineData("Windows 10 Pro", null, null, null, "Windows 10 Pro")]
    public void FormatWindowsVersion_FollowsWinver(string product, string? displayVersion, string? build, int? ubr, string expected)
    {
        Assert.Equal(expected, MachineInspector.FormatWindowsVersion(product, displayVersion, build, ubr));
    }

    [Theory]
    [InlineData("EventLog")]
    [InlineData("Winmgmt")]
    public void GetService_ExistingWindowsService_IsRunningWithPath(string name)
    {
        var s = _inspector.GetService(name);

        Assert.NotNull(s);
        Assert.Equal("Running", s.Status);
        Assert.Equal(name, s.Name, ignoreCase: true);
        Assert.NotEmpty(s.DisplayName);
        Assert.NotEmpty(s.StartType);
        Assert.NotEmpty(s.ExecutablePath);
        Assert.DoesNotContain("\"", s.ExecutablePath);
    }

    [Fact]
    public void GetService_Missing_ReturnsNull()
    {
        Assert.Null(_inspector.GetService("NoSuchService-" + Guid.NewGuid().ToString("N")));
        Assert.Null(_inspector.GetService(""));
    }

    [Fact]
    public void FindServicesUnder_SystemDirectory_FindsWindowsServices_AndUnrelatedDirectoryFindsNone()
    {
        string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

        Assert.Contains(_inspector.FindServicesUnder([system32]), s => s.Name == "EventLog" || s.Name == "Winmgmt");
        Assert.Empty(_inspector.FindServicesUnder([Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"))]));
    }

    [Fact]
    public void GetProcessesUnder_OwnTestHostDirectory_FindsThisProcess()
    {
        string exe = Process.GetCurrentProcess().MainModule!.FileName;
        string dir = Path.GetDirectoryName(exe)!;

        var found = _inspector.GetProcessesUnder(dir);

        var me = Assert.Single(found, p => p.Pid == Environment.ProcessId);
        Assert.Equal(exe, me.ExecutablePath, ignoreCase: true);
        Assert.True(me.MemoryBytes > 0);
    }

    [Fact]
    public void GetProcessesUnder_DirectoryWithoutProcesses_IsEmpty()
    {
        Assert.Empty(_inspector.GetProcessesUnder(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void ProcessesUnder_FiltersOneSnapshotPerFolder_SortedByNameThenPid()
    {
        // Satu hasil ListProcesses dipakai untuk banyak folder: tidak ada enumerasi proses per folder.
        var all = new[]
        {
            new ProcessInfo("wind", 20, 1, @"D:\ION\wind\wind.exe"),
            new ProcessInfo("agent", 30, 1, @"D:\ION\wind\sub\agent.exe"),
            new ProcessInfo("wind", 10, 1, @"D:\ION\wind\wind.exe"),
            new ProcessInfo("windx", 40, 1, @"D:\ION\windx\windx.exe"),
            new ProcessInfo("other", 50, 1, @"C:\Other\other.exe"),
        };

        Assert.Equal([30, 10, 20], MachineInspector.ProcessesUnder(all, @"D:\ION\wind").Select(p => p.Pid));   // windx bukan di wind
        Assert.Equal([40], MachineInspector.ProcessesUnder(all, @"D:\ION\windx").Select(p => p.Pid));
        Assert.Empty(MachineInspector.ProcessesUnder(all, ""));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\ION\\svc.exe\" -k run", "C:\\Program Files\\ION\\svc.exe")]
    [InlineData("C:\\Windows\\system32\\svchost.exe -k netsvcs -p", "C:\\Windows\\system32\\svchost.exe")]
    [InlineData("C:\\Program Files\\ION\\svc.exe", "C:\\Program Files\\ION\\svc.exe")]
    [InlineData("  ", "")]
    public void ExtractExecutable_StripsQuotesAndArguments(string raw, string expected)
    {
        Assert.Equal(expected, MachineInspector.ExtractExecutable(raw));
    }

    [Fact]
    public void IsUnder_DoesNotMatchSiblingWithSamePrefix()
    {
        Assert.True(MachineInspector.IsUnder(@"D:\ION\wind\w.exe", @"D:\ION"));
        Assert.False(MachineInspector.IsUnder(@"D:\IONX\w.exe", @"D:\ION"));
    }

    [Fact]
    public void Inspect_ReturnsProcessorAndMemory()
    {
        var snap = new MachineInspector().Inspect();

        Assert.Contains("logical processors", snap.Processor);
        Assert.Contains("GB total", snap.Memory);
    }

    [Fact]
    public void FormatProcessor_CollapsesSpaces_AndFallsBackWhenNameIsMissing()
    {
        Assert.Equal("Intel(R) Core(TM) i7 @ 3.00GHz · 8 logical processors",
            MachineInspector.FormatProcessor("Intel(R)  Core(TM) i7   @ 3.00GHz", 8));
        Assert.Equal("4 logical processors", MachineInspector.FormatProcessor("", 4));
    }

    [Fact]
    public void FormatMemory_ShowsTotalAndFreeInGb()
    {
        Assert.Equal("16.0 GB total · 4.0 GB free", MachineInspector.FormatMemory(16UL << 30, 4UL << 30));
        Assert.Equal("Unknown", MachineInspector.FormatMemory(0, 0));
    }
}
