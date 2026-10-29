using Xunit;

namespace IonPackMan.Tests;

public class StartupOptionsTests
{
    [Fact]
    public void NoArguments_UsesTheDefaultLocations()
    {
        var (options, error) = StartupOptions.Parse([]);

        Assert.Null(error);
        Assert.Equal(Paths.ReleaseDirectory, options!.ReleaseDirectory);
        Assert.Equal(Paths.MachineIni, options.MachineIni);
    }

    [Fact]
    public void ReleaseAndMachine_OverrideTheDefaults_InAnyOrder()
    {
        var (options, error) = StartupOptions.Parse(["--machine", @"C:\PackManTest\machine.ini", "--release", @"C:\PackManTest\release"]);

        Assert.Null(error);
        Assert.Equal(@"C:\PackManTest\release", options!.ReleaseDirectory);
        Assert.Equal(@"C:\PackManTest\machine.ini", options.MachineIni);
    }

    [Fact]
    public void OnlyOneOverride_KeepsTheOtherDefault()
    {
        var (options, _) = StartupOptions.Parse(["--machine", @"C:\x\machine.ini"]);

        Assert.Equal(Paths.ReleaseDirectory, options!.ReleaseDirectory);
        Assert.Equal(@"C:\x\machine.ini", options.MachineIni);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("release")]
    [InlineData("--release")]                       // tanpa nilai
    [InlineData("--release", "--machine")]          // nilai berupa opsi lain
    [InlineData("--release", @"relative\folder")]   // bukan path penuh
    [InlineData("--machine", "")]
    public void BadArguments_AreRejected_WithAMessage(params string[] args)
    {
        var (options, error) = StartupOptions.Parse(args);

        Assert.Null(options);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Window_SetsTheWindowSize_AndDefaultsToNone()
    {
        Assert.Null(StartupOptions.Parse([]).Options!.WindowSize);

        var (options, error) = StartupOptions.Parse(["--window", "1024x768"]);

        Assert.Null(error);
        Assert.Equal((1024d, 768d), options!.WindowSize);
    }

    [Theory]
    [InlineData("1024")]
    [InlineData("axb")]
    [InlineData("100x100")]
    [InlineData("1024x768x2")]
    public void BadWindowSize_IsRejected(string size)
    {
        var (options, error) = StartupOptions.Parse(["--window", size]);

        Assert.Null(options);
        Assert.Contains("--window", error);
    }
}
