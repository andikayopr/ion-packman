using IonPackMan;
using Xunit;

namespace IonPackMan.Tests;

public class PathsTests
{
    [Fact]
    public void ComponentIni_UsesIdAndVersion()
    {
        var path = Paths.ComponentIni(@"E:\Rel", "wind", "2.1.0");
        Assert.Equal(@"E:\Rel\Components\wind\wind-2.1.0.ini", path);
    }

    [Fact]
    public void ProfileIni_UsesProfileId()
    {
        Assert.Equal(@"E:\Rel\Profiles\QRISTAP.ini", Paths.ProfileIni(@"E:\Rel", "QRISTAP"));
    }

    [Fact]
    public void OperationalData_LivesUnderCache()
    {
        const string cache = @"D:\ION-PackMan\Cache";
        Assert.Equal(@"D:\ION-PackMan\Cache\Builds\wind@2.1.0", Paths.BuildDir(cache, "wind", "2.1.0"));
        Assert.Equal(@"D:\ION-PackMan\Cache\Backups\e1", Paths.BackupDir(cache, "e1"));
        Assert.Equal(@"D:\ION-PackMan\Cache\History\e1.ini", Paths.HistoryIni(cache, "e1"));
    }
}
