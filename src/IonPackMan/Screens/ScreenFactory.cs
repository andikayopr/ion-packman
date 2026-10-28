using System.Windows;
using IonPackMan.Components;

namespace IonPackMan.Screens;

/// <summary>Membuat layar tiap menu dari <see cref="AppServices"/>. Konfigurasi rusak = layar penjelasan, bukan crash.</summary>
public static class ScreenFactory
{
    /// <param name="toast">Menampilkan pesan singkat (dipakai alur yang berjalan di luar layar, mis. dialog import).</param>
    public static FrameworkElement Create(string key, AppServices s, Action<string> toast) => key switch
    {
        MainWindow.Dashboard => new DashboardScreen(s.Dashboard),
        MainWindow.Components => CreateComponents(s, toast),
        MainWindow.Profiles => CreateProfiles(s),
        MainWindow.History => CreateHistory(s),
        MainWindow.DevConsole => Placeholder(key, "The Developer Console is built in iteration 1c."),
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown menu."),
    };

    private static FrameworkElement CreateComponents(AppServices s, Action<string> toast)
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
        return screen;
    }

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
