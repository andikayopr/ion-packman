using System.Windows;
using System.Windows.Controls;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

public class SwitchProfileDialogTests
{
    private static List<string> RowLabels(SwitchProfileDialog dialog) =>
        ((StackPanel)dialog.FindName("PlanRows")).Children.OfType<HeaderedContentControl>().Select(h => (string)h.Header).ToList();

    [Fact]
    public void ShowsThePlanPerComponent_ThenSwitchesAndMarksTheProfileActive()
    {
        using var env = new TestDeployEnvironment();
        ProfileSwitcherTests.ImportMiddlewareBuild(env);
        var switcher = ProfileSwitcherTests.SwitcherFor(env);

        var dialog = WpfHost.Run(() => new SwitchProfileDialog(switcher, "TESTP", "TEST-PC"));

        WpfHost.Run(() =>
        {
            Assert.True(dialog.CanSwitch);
            Assert.Equal(["Switch", "ion-middleware", "wind", "Stop", "Activate"], RowLabels(dialog));
        });
        Assert.Equal("", env.Machine.Profile);   // preview tidak mengubah apa pun

        var task = WpfHost.Run(() => dialog.ExecuteAsync());
        Assert.True(task.Wait(TimeSpan.FromSeconds(30)), "switch did not finish");

        WpfHost.Run(() =>
        {
            Assert.True(dialog.Switched);
            Assert.Equal("TESTP", env.Machine.Profile);
            Assert.False(dialog.CanSwitch);
            Assert.Equal(Visibility.Visible, ((Button)dialog.FindName("CloseButton")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((Button)dialog.FindName("SwitchButton")).Visibility);
        });
    }

    [Fact]
    public void RejectedPlan_ShowsTheReasons_AndCannotBeExecuted()
    {
        using var env = new TestDeployEnvironment(environment: "Production");
        ProfileSwitcherTests.ImportMiddlewareBuild(env);
        var switcher = ProfileSwitcherTests.SwitcherFor(env);

        var dialog = WpfHost.Run(() => new SwitchProfileDialog(switcher, "TESTP", "TEST-PC"));

        WpfHost.Run(() =>
        {
            Assert.False(dialog.CanSwitch);
            Assert.NotEmpty(dialog.Errors);
            Assert.Equal(Visibility.Visible, ((UIElement)dialog.FindName("ErrorNotice")).Visibility);
            Assert.False(((Button)dialog.FindName("SwitchButton")).IsEnabled);
        });
        Assert.Null(WpfHost.Run(() => dialog.ExecuteAsync()).Result);
        Assert.Equal("", env.Machine.Profile);
    }

    [Fact]
    public void DevConsole_OffersOnlyTheActiveProfile_WhenItIsKnown()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create());
        rig.AddSecondProfile();           // TESTQ ada; kedua profile cocok dengan mesin, jadi hanya penanda aktif yang menentukan
        rig.Env.Machine.Profile = "TESTP";
        WpfHost.Run(() =>
        {
            rig.Screen.Refresh();
            Assert.Equal(new[] { "TESTP" }, rig.Profile.Items.Cast<string>());
        });
    }

    [Fact]
    public void DevConsole_OffersEveryProfile_WhenTheActiveOneIsUnknown()
    {
        using var rig = WpfHost.Run(() => DevConsoleTestRig.Create(detected: false));
        rig.AddSecondProfile();
        WpfHost.Run(() =>
        {
            rig.Screen.Refresh();
            Assert.Equal(new[] { "TESTP", "TESTQ" }, rig.Profile.Items.Cast<string>().Order());
        });
    }
}
