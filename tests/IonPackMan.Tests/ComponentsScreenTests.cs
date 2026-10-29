using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using IonPackMan.Components;
using IonPackMan.Controls;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

public class ComponentsScreenTests : IDisposable
{
    // Area konten 1024×768: 808 + padding 2×16 (UI/UX Spec §1).
    private const double HostWidth = 808 + 32;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-cs-" + Guid.NewGuid().ToString("N"));
    private readonly ComponentStore _store;

    public ComponentsScreenTests()
    {
        Directory.CreateDirectory(Paths.ComponentsDir(_dir));
        File.WriteAllText(Paths.CatalogIni(_dir), """
            [Meta]
            SchemaVersion=1.0
            [Components]
            ion-middleware=ION Middleware
            wind=WIND
            qraptor=QRAPTOR
            non-ndc-parser=Non-NDC Parser
            mdd-service=MDD Service
            """);
        _store = new ComponentStore(_dir);
        Assert.Empty(_store.Save(Rel("wind", "WIND", "2.1.0", @"D:\APCONTAINER\WIND", "WIND")));
        Assert.Empty(_store.Save(Rel("qraptor", "QRAPTOR", "1.3.1", @"D:\APCONTAINER\QRAPTOR", "")));
        Assert.Empty(_store.Save(Rel("ion-middleware", "ION Middleware", "1.8.2", @"D:\ION\Middleware", "")));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ComponentRelease Rel(string id, string name, string version, string target, string service) => new()
    {
        Id = id, DisplayName = name, ReleaseVersion = version, TargetDirectory = target, ServiceName = service,
    };

    private static string? BuildLabel(string id, string version) => id == "wind" && version == "2.1.0" ? "wind-build · 12 files" : null;

    private ComponentsScreen NewScreen() => new(_store, TestAllowList.Create(), BuildLabel);

    // Style implisit hanya berlaku di dalam Window; tampilkan di luar layar supaya DataGrid membuat baris.
    private static Window Host(ComponentsScreen screen)
    {
        var window = new Window
        {
            Content = new Border { Padding = new Thickness(16), Child = screen }, Width = HostWidth, Height = 900,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000,
        };
        window.Show();
        window.UpdateLayout();
        return window;
    }

    private static T Find<T>(ComponentsScreen s, string name) where T : class => (T)s.FindName(name);
    private static void Click(ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Type(TextBox box, string text) => box.Text = text;

    private static string[] NoticeLines(Notice n) =>
        n.Visibility != Visibility.Visible ? [] : ((StackPanel)n.Content).Children.Cast<TextBlock>().Select(t => t.Text).ToArray();

    private static void Choose(ComponentsScreen s, string id) => s.OpenComponent(id);

    private static string Row(ComponentsScreen s, int i)
    {
        var grid = Find<DataGrid>(s, "CatalogGrid");
        var item = grid.Items[i]!;
        return string.Join(" | ", new[] { "Display", "Versions", "LocalBuild" }
            .Select(p => (string)item.GetType().GetProperty(p)!.GetValue(item)!));
    }

    // ---------- Katalog ----------

    [Fact]
    public void Catalog_ShowsFourRowsPerPage_WithVersionsAndLocalBuild()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            var grid = Find<DataGrid>(s, "CatalogGrid");

            Assert.Equal(4, grid.Items.Count);
            Assert.Equal("Page 1 of 2 · 5 components", Find<TextBlock>(s, "PageText").Text);
            Assert.Equal("ION Middleware (ion-middleware) | 1.8.2 | Not imported", Row(s, 0));
            Assert.Equal("WIND (wind) | 2.1.0 | wind-build · 12 files", Row(s, 1));
            Assert.Equal("Non-NDC Parser (non-ndc-parser) | None | Not imported", Row(s, 3));
            Assert.False(Find<Button>(s, "PrevButton").IsEnabled);

            Click(Find<Button>(s, "NextButton"));
            Assert.Equal(1, grid.Items.Count);
            Assert.Equal("Page 2 of 2 · 5 components", Find<TextBlock>(s, "PageText").Text);
            Assert.Contains("MDD Service", Row(s, 0));
            Assert.False(Find<Button>(s, "NextButton").IsEnabled);
        });
    }

    [Fact]
    public void Catalog_SearchAndFilter_NarrowTheRows()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            var grid = Find<DataGrid>(s, "CatalogGrid");
            var search = Find<TextBox>(s, "SearchBox");
            var filter = Find<ComboBox>(s, "FilterBox");

            Type(search, " WIND ");                       // id/nama, spasi dan huruf besar tidak berpengaruh
            Assert.Equal(1, grid.Items.Count);
            Assert.Equal("Page 1 of 1 · 1 component", Find<TextBlock>(s, "PageText").Text);

            Type(search, "1.3.1");                        // cari lewat versi
            Assert.Contains("QRAPTOR", Row(s, 0));

            Type(search, "");
            filter.SelectedIndex = 1;                     // Saved releases
            Assert.Equal("Page 1 of 1 · 3 components", Find<TextBlock>(s, "PageText").Text);
            filter.SelectedIndex = 2;                     // Has local build
            Assert.Equal(1, grid.Items.Count);
            Assert.Contains("WIND", Row(s, 0));
            filter.SelectedIndex = 3;                     // Not saved
            Assert.Equal("Page 1 of 1 · 2 components", Find<TextBlock>(s, "PageText").Text);

            Type(search, "zzz");
            Assert.Equal("Page 1 of 1 · 0 components", Find<TextBlock>(s, "PageText").Text);
        });
    }

    // ---------- Open / form / preview ----------

    [Fact]
    public void OpenButton_LoadsLatestSavedVersionIntoTheForm()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            var window = Host(s);
            var open = WpfHost.Descendants<Button>(Find<DataGrid>(s, "CatalogGrid")).Where(b => (string)b.Content == "Open").ToList();
            Assert.Equal(4, open.Count);

            Click(open[1]);                               // baris WIND

            Assert.Equal("2.1.0", Find<TextBox>(s, "VersionBox").Text);
            Assert.Equal(@"D:\APCONTAINER\WIND", Find<TextBox>(s, "TargetBox").Text);
            Assert.Equal("WIND", Find<TextBox>(s, "ServiceBox").Text);
            Assert.Equal("wind", (string)((ComboBoxItem)Find<ComboBox>(s, "ComponentBox").SelectedItem).Tag);
            Assert.Equal("Imported: wind-build · 12 files", Find<TextBlock>(s, "BuildText").Text);
            window.Close();
        });
    }

    [Fact]
    public void ChoosingAComponent_LoadsItsOnlyRelease_AndSavingAnotherVersionReplacesIt()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            var box = Find<ComboBox>(s, "ComponentBox");
            box.SelectedItem = box.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == "wind");   // dari dropdown, bukan OpenComponent
            Assert.Equal("2.1.0", Find<TextBox>(s, "VersionBox").Text);
            Assert.Equal("Saved version: 2.1.0.", Find<TextBlock>(s, "SavedVersionsText").Text);

            Type(Find<TextBox>(s, "VersionBox"), "2.2.0");
            Assert.Contains("Saving 2.2.0 replaces it", Find<TextBlock>(s, "SavedVersionsText").Text);
            Click(Find<Button>(s, "SaveButton"));

            Assert.Equal(["2.2.0"], _store.ListVersions("wind"));
            Assert.Equal("WIND (wind) | 2.2.0 | Not imported", Row(s, 1));
        });
    }

    [Fact]
    public void IniPreview_FollowsTheForm()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            Choose(s, "wind");
            var ini = Find<TextBox>(s, "IniBox");
            Assert.Contains("ReleaseVersion=2.1.0", ini.Text);

            Type(Find<TextBox>(s, "VersionBox"), "3.0.0");
            Type(Find<TextBox>(s, "TargetBox"), @"  D:\APCONTAINER\WIND3  ");
            Type(Find<TextBox>(s, "BuildRefBox"), "ci-77");

            Assert.Contains("ReleaseVersion=3.0.0", ini.Text);
            Assert.Contains(@"TargetDirectory=D:\APCONTAINER\WIND3", ini.Text);
            Assert.Contains("BuildReference=ci-77", ini.Text);
            Assert.True(ini.IsReadOnly);

            Click(Find<Button>(s, "IniTab"));
            Assert.Equal(Visibility.Visible, ini.Visibility);
            Assert.Equal(Visibility.Collapsed, Find<StackPanel>(s, "FormPanel").Visibility);
            Click(Find<Button>(s, "FormTab"));
            Assert.Equal(Visibility.Collapsed, ini.Visibility);
        });
    }

    // ---------- Save / Validate ----------

    [Fact]
    public void Save_Valid_WritesFile_RaisesMessage_AndUpdatesCatalog()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            string? toast = null;
            s.Message += m => toast = m;
            Choose(s, "mdd-service");
            Type(Find<TextBox>(s, "VersionBox"), " 2.4.0 ");
            Type(Find<TextBox>(s, "TargetBox"), @" D:\ION\MDD ");

            Click(Find<Button>(s, "SaveButton"));

            Assert.True(File.Exists(Paths.ComponentIni(_dir, "mdd-service", "2.4.0")));
            var notice = Find<Notice>(s, "ReleaseNotice");
            Assert.Equal(StatusKind.Success, notice.Kind);
            Assert.Equal(["Saved mdd-service@2.4.0."], NoticeLines(notice));
            Assert.Equal("Component release saved.", toast);
            Assert.Equal(@"D:\ION\MDD", _store.Load("mdd-service", "2.4.0").Release!.TargetDirectory);
            Assert.True(Find<Button>(s, "ImportBuildButton").IsEnabled);   // sudah tersimpan
        });
    }

    [Fact]
    public void Save_TargetOutsideBaseDirectory_ShowsErrorAndWritesNothing()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            string? toast = null;
            s.Message += m => toast = m;
            Choose(s, "mdd-service");
            Type(Find<TextBox>(s, "VersionBox"), "2.4.0");
            Type(Find<TextBox>(s, "TargetBox"), @"C:\Windows\System32");
            Type(Find<TextBox>(s, "ServiceBox"), "NotAllowed");

            Click(Find<Button>(s, "SaveButton"));

            Assert.False(File.Exists(Paths.ComponentIni(_dir, "mdd-service", "2.4.0")));
            var notice = Find<Notice>(s, "ReleaseNotice");
            Assert.Equal(StatusKind.Error, notice.Kind);
            var lines = NoticeLines(notice);
            Assert.Equal(2, lines.Length);                // satu baris per masalah
            Assert.Contains(lines, l => l.Contains("not inside an allowed base directory"));
            Assert.Contains(lines, l => l.Contains("not in the allow-list"));
            Assert.Null(toast);
        });
    }

    [Fact]
    public void Validate_ReportsProblems_OrSuccess_WithoutWriting()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            Choose(s, "mdd-service");
            Type(Find<TextBox>(s, "VersionBox"), "2.4.0");
            Type(Find<TextBox>(s, "TargetBox"), "relative\\path");
            Click(Find<Button>(s, "ValidateButton"));
            Assert.Equal(StatusKind.Error, Find<Notice>(s, "ReleaseNotice").Kind);

            Type(Find<TextBox>(s, "TargetBox"), @"D:\ION\MDD");
            Click(Find<Button>(s, "ValidateButton"));
            Assert.Equal(StatusKind.Success, Find<Notice>(s, "ReleaseNotice").Kind);
            Assert.False(File.Exists(Paths.ComponentIni(_dir, "mdd-service", "2.4.0")));
        });
    }

    // ---------- Import local build, Use in Dev Console ----------

    [Fact]
    public void ImportBuild_IsEnabledOnlyForSavedRelease_AndRaisesEvent()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            ComponentRelease? requested = null;
            s.ImportBuildRequested += r => requested = r;
            var button = Find<Button>(s, "ImportBuildButton");

            Choose(s, "wind");
            Assert.True(button.IsEnabled);
            Click(button);
            Assert.Equal("wind", requested?.Id);
            Assert.Equal("2.1.0", requested?.ReleaseVersion);

            Type(Find<TextBox>(s, "VersionBox"), "4.0.0");  // belum tersimpan
            Assert.False(button.IsEnabled);

            // Release yang belum tersimpan tidak bisa dipakai di Developer Console.
            var console = Find<Button>(s, "UseInConsoleButton");
            Assert.False(console.IsEnabled);
            Assert.Equal("Save the release first", console.ToolTip);
        });
    }

    [Fact]
    public void UseInConsole_IsEnabledForSavedRelease_AndRaisesEvent()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            ComponentRelease? requested = null;
            s.UseInConsoleRequested += r => requested = r;
            var console = Find<Button>(s, "UseInConsoleButton");

            Choose(s, "wind");
            Assert.True(console.IsEnabled);
            Click(console);

            Assert.Equal(("wind", "2.1.0"), (requested?.Id, requested?.ReleaseVersion));
        });
    }

    // ---------- Import / Export INI ----------

    [Fact]
    public void ImportIni_RejectsOtherComponent_AndAcceptsMatchingOne()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            string other = Path.Combine(_dir, "ext-qraptor.ini");
            Rel("qraptor", "QRAPTOR", "1.4.0", @"D:\APCONTAINER\QRAPTOR", "").ToIni().Save(other);

            Choose(s, "wind");
            s.ImportIni(other);
            var notice = Find<Notice>(s, "ReleaseNotice");
            Assert.Equal(StatusKind.Error, notice.Kind);
            Assert.Contains("not 'wind'", NoticeLines(notice)[0]);
            Assert.False(File.Exists(Paths.ComponentIni(_dir, "qraptor", "1.4.0")));

            Choose(s, "qraptor");
            s.ImportIni(other);
            Assert.Equal(StatusKind.Success, notice.Kind);
            Assert.True(File.Exists(Paths.ComponentIni(_dir, "qraptor", "1.4.0")));
            Assert.Equal("1.4.0", Find<TextBox>(s, "VersionBox").Text);
        });
    }

    [Fact]
    public void ExportIni_WritesFormContent_AndRefusesInvalidForm()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            string path = Path.Combine(_dir, "exported.ini");
            Choose(s, "wind");

            s.ExportIni(path);
            Assert.Equal(_store.Load("wind", "2.1.0").Release, ComponentRelease.LoadFile(path).Release);

            Type(Find<TextBox>(s, "TargetBox"), "");
            string path2 = Path.Combine(_dir, "bad.ini");
            s.ExportIni(path2);
            Assert.False(File.Exists(path2));
            Assert.Equal(StatusKind.Error, Find<Notice>(s, "ReleaseNotice").Kind);
        });
    }

    // ---------- Katalog rusak ----------

    [Fact]
    public void MissingCatalog_ShowsErrorNotice_AndDoesNotCrash()
    {
        WpfHost.Run(() =>
        {
            File.Delete(Paths.CatalogIni(_dir));

            var s = NewScreen();
            var notice = Find<Notice>(s, "CatalogNotice");

            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Equal(StatusKind.Error, notice.Kind);
            Assert.Equal(0, Find<DataGrid>(s, "CatalogGrid").Items.Count);
            Assert.Equal("Page 1 of 1 · 0 components", Find<TextBlock>(s, "PageText").Text);

            // Katalog kembali tersedia: Refresh memulihkan tampilan.
            File.WriteAllText(Paths.CatalogIni(_dir), "[Meta]\nSchemaVersion=1.0\n[Components]\nwind=WIND\n");
            s.Refresh();
            Assert.Equal(Visibility.Collapsed, notice.Visibility);
            Assert.Equal(1, Find<DataGrid>(s, "CatalogGrid").Items.Count);
        });
    }

    // ---------- Render PNG ----------

    private static readonly string PngDir = Environment.GetEnvironmentVariable("IONPACKMAN_PNG_DIR") ?? Path.GetTempPath();

    private static void RenderAndCheck(ComponentsScreen screen, string name)
    {
        var host = new Border { Padding = new Thickness(16), Child = screen };
        double width, height, screenWidth;
        try
        {
            (width, height) = WpfHost.RenderToPng(host, HostWidth, Path.Combine(PngDir, $"ionpackman-components-{name}.png"));
            screenWidth = screen.ActualWidth;
        }
        finally
        {
            host.Child = null;   // lepas supaya layar bisa dirender lagi
        }
        Assert.True(width <= HostWidth + 0.5, $"{name}: {width}px wide");
        Assert.True(height > 300);
        Assert.True(screenWidth <= 808.5, $"{name}: screen is {screenWidth}px wide");
    }

    [Fact]
    public void Render_Initial_Error_AndIniPreview_FitContentWidth()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            RenderAndCheck(s, "1-initial");

            Choose(s, "wind");
            RenderAndCheck(s, "2-opened");

            Type(Find<TextBox>(s, "TargetBox"), @"C:\Windows\System32\Some\Very\Long\Path\That\Is\Outside\The\Allowed\Base\Directories");
            Type(Find<TextBox>(s, "ServiceBox"), "NotAllowed");
            Click(Find<Button>(s, "SaveButton"));
            RenderAndCheck(s, "3-error");

            Click(Find<Button>(s, "IniTab"));
            RenderAndCheck(s, "4-ini");
        });
    }
}
