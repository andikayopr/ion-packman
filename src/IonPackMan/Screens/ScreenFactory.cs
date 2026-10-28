using System.Windows;
using IonPackMan.Components;

namespace IonPackMan.Screens;

/// <summary>Membuat layar tiap menu dari <see cref="AppServices"/>. Konfigurasi rusak = layar penjelasan, bukan crash.</summary>
public static class ScreenFactory
{
    /// <param name="toast">Menampilkan pesan singkat (dipakai alur yang berjalan di luar layar, mis. dialog import).</param>
    /// <param name="open">Membuka layar menu lain dan mengembalikannya (MainWindow.Open); dipakai "Use in Developer Console".</param>
    public static FrameworkElement Create(string key, AppServices s, Action<string> toast, Func<string, FrameworkElement>? open = null) => key switch
    {
        MainWindow.Dashboard => new DashboardScreen(s.Dashboard, s.Recovery),
        MainWindow.Components => CreateComponents(s, toast, open),
        MainWindow.Profiles => CreateProfiles(s),
        MainWindow.History => CreateHistory(s),
        MainWindow.DevConsole => CreateDevConsole(s),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown menu."),
    };

    private static FrameworkElement CreateComponents(AppServices s, Action<string> toast, Func<string, FrameworkElement>? open)
    {
        if (s.AllowList is null)
            return Broken(MainWindow.Components, "allowlist.ini", s.AllowListErrors);

        string? LocalBuildLabel(string id, string version) =>
            s.Builds is not null && s.Builds.Exists(id, version) ? s.Builds.ListBuilds()
                .FirstOrDefault(b => b.ComponentId == id && b.ReleaseVersion == version)?.Display : null;

        var screen = new ComponentsScreen(s.Components, s.AllowList, LocalBuildLabel);
        screen.ImportBuildRequested += release =>
        {
            if (s.Builds is null)
            {
                MessageBox.Show("Builds cannot be imported because machine.ini is not valid.", "Import local build",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string? cacheProblem = s.Machine?.CheckCacheDirectory();
            if (cacheProblem is not null)
            {
                MessageBox.Show(cacheProblem, "Import local build", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (BuildImportFlow.Run(s.Builds, release, toast))
                screen.Refresh();   // kolom Local build ikut diperbarui
        };
        screen.UseInConsoleRequested += release =>
        {
            if (open?.Invoke(MainWindow.DevConsole) is DevConsoleScreen console)
                console.Preselect(release.Id, release.ReleaseVersion);
        };
        return screen;
    }

    // Konsol hanya bisa dibuat bila semua yang dibutuhkan eksekusi tersedia (machine.ini dan allowlist.ini valid).
    private static FrameworkElement CreateDevConsole(AppServices s) =>
        s.Machine is null || s.Profiles is null || s.Builds is null || s.Planner is null || s.Deployer is null || s.Backups is null
            ? Broken(MainWindow.DevConsole, s.Machine is null ? "machine.ini" : "allowlist.ini", [.. s.MachineErrors, .. s.AllowListErrors])
            : new DevConsoleScreen(s.Machine, s.Profiles, s.Components, s.Builds, s.Planner, s.Deployer, s.Backups, s.Detector);

    private static FrameworkElement CreateProfiles(AppServices s) =>
        s.Profiles is null || s.Validator is null || s.AllowList is null
            ? Broken(MainWindow.Profiles, "allowlist.ini", s.AllowListErrors)
            : new ProfilesScreen(s.Profiles, s.Components, s.Validator, s.AllowList);

    private static FrameworkElement CreateHistory(AppServices s) =>
        s.History is null
            ? Broken(MainWindow.History, "machine.ini", s.MachineErrors)
            : new HistoryScreen(s.History);

    private static FrameworkElement Placeholder(string key, string message)
    {
        var (_, title, icon) = MainWindow.Menus.Single(m => m.Key == key);
        return new PlaceholderScreen(title, message, icon);
    }

    private static FrameworkElement Broken(string key, string file, IReadOnlyList<string> errors) =>
        Placeholder(key, $"{file} could not be loaded, so this screen is unavailable.\n{string.Join("\n", errors)}");
}
