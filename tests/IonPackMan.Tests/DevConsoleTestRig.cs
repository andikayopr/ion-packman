using System.Windows;
using System.Windows.Controls;
using IonPackMan.Deployment;
using IonPackMan.History;
using IonPackMan.Machine;
using IonPackMan.Profiles;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>
/// Menyusun layar Developer Console di atas mesin palsu (TestDeployEnvironment): BackupStore, HistoryStore, Deployer, dan
/// ProfileDetector dibuat di sini seperti DeployerTests.Prepare. Tidak menyentuh Registry/service/file nyata.
/// Harus dipakai dari thread WPF (WpfHost.Run).
/// </summary>
internal sealed class DevConsoleTestRig : IDisposable
{
    public required TestDeployEnvironment Env { get; init; }
    public required BackupStore Backups { get; init; }
    public required HistoryStore History { get; init; }
    public required Deployer Deployer { get; init; }
    public required ProfileDetector Detector { get; init; }
    public required DevConsoleScreen Screen { get; init; }
    public required Window Host { get; init; }
    public List<string> Messages { get; } = [];

    /// <param name="detected">false = detektor tidak menemukan folder/service, jadi hasilnya Unknown.</param>
    public static DevConsoleTestRig Create(TestDeployEnvironment? env = null, bool detected = true, ExecutionLock? gate = null)
    {
        env ??= new TestDeployEnvironment();
        var backups = new BackupStore(env.Cache, env.Registry, env.Services, env.Allow);
        var history = new HistoryStore(env.Cache);
        var deployer = new Deployer(env.Machine, env.Guards, backups, env.Services, env.Processes, env.Registry, history, TimeSpan.FromSeconds(1));
        var detector = new ProfileDetector(env.Components, env.Profiles, _ => detected, _ => detected);
        var screen = new DevConsoleScreen(env.Machine, env.Profiles, env.Components, env.Builds, env.Planner, deployer, backups, detector, gate);
        var host = new Window { Content = screen };   // style tema hanya berlaku bila elemen berada di Window
        screen.Measure(new Size(808, 3000));

        var rig = new DevConsoleTestRig { Env = env, Backups = backups, History = history, Deployer = deployer, Detector = detector, Screen = screen, Host = host };
        screen.Message += rig.Messages.Add;
        return rig;
    }

    public T Part<T>(string name) where T : class => (T)Screen.FindName(name);

    public ComboBox Profile => Part<ComboBox>("ProfileBox");
    public ComboBox Component => Part<ComboBox>("ComponentBox");
    public ComboBox Build => Part<ComboBox>("BuildBox");
    public CheckBox Deploy => Part<CheckBox>("DeployBox");
    public CheckBox Config => Part<CheckBox>("ConfigBox");
    public CheckBox Restart => Part<CheckBox>("RestartBox");
    public Button PreviewButton => Part<Button>("PreviewButton");
    public Button ExecuteButton => Part<Button>("ExecuteButton");
    public string Status => Part<TextBlock>("StatusText").Text;

    /// <summary>Pilih component wind (build wind@2.1.0 sudah diimpor oleh TestDeployEnvironment).</summary>
    public void SelectWind() => Component.SelectedItem = "wind";

    /// <summary>Baris ringkasan Execution Preview: label -> teks.</summary>
    public Dictionary<string, string> PlanRows() =>
        Part<StackPanel>("PlanRows").Children.OfType<HeaderedContentControl>()
            .ToDictionary(h => (string)h.Header, h => ((TextBlock)h.Content).Text);

    public List<string> ErrorLines() =>
        Part<StackPanel>("ErrorLines").Children.OfType<TextBlock>().Select(t => t.Text).ToList();

    public string ResultText() =>
        string.Join("\n", Part<StackPanel>("ResultLines").Children.OfType<TextBlock>().Select(t => t.Text));

    public void Dispose() => Env.Dispose();

    /// <summary>Profile kedua yang hanya berisi wind, supaya tes bisa mengganti Profile.</summary>
    public void AddSecondProfile()
    {
        Assert.Empty(Env.Profiles.Save(new IonPackMan.Profiles.Profile
        {
            Id = "TESTQ", Version = "1.0.0", WindWebUIEnabled = true,
            Components = [new("wind", "2.1.0", [])],
            RegistryEntries = [new("WebUISupport", TestDeployEnvironment.RegKey, "Support", "DWORD", "1", IonPackMan.Profiles.Profile.WindWebUI)],
            IniEntries = [new("WebUISupport", Env.AppIni, "WebUI", "Support", "1", IonPackMan.Profiles.Profile.WindWebUI)],
        }));
    }

    /// <summary>Menjalankan ExecutePlanAsync dari thread WPF dan menunggu hasilnya (continuation kembali ke thread UI).</summary>
    public (DeploymentResult? Result, List<DeployStep> Steps, List<bool> BusyDuring) Execute()
    {
        var steps = new List<DeployStep>();
        var busy = new List<bool>();
        var progress = new SyncProgress(s => { steps.Add(s); busy.Add(Screen.IsBusy); });
        var task = WpfHost.Run(() => Screen.ExecutePlanAsync(progress));
        Assert.True(task.Wait(TimeSpan.FromSeconds(30)), "execution did not finish");
        return (task.Result, steps, busy);
    }

    private sealed class SyncProgress(Action<DeployStep> onReport) : IProgress<DeployStep>
    {
        public void Report(DeployStep value) => onReport(value);   // sinkron di thread pool agar urutan pasti
    }
}
