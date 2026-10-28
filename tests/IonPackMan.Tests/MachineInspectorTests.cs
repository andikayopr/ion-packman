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
        Assert.Contains(Environment.OSVersion.Version.ToString(), snap.WindowsVersion);
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
}
