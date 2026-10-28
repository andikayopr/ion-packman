using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using IonPackMan.Components;
using IonPackMan.Controls;
using IonPackMan.Machine;
using IonPackMan.Profiles;
using IonPackMan.Screens;
using Xunit;

namespace IonPackMan.Tests;

/// <summary>Test layar Profiles (T12). Data dari Data awal yang disalin ke folder sementara.</summary>
public class ProfilesScreenTests : IDisposable
{
    private const double ContentWidth = 808 + 32;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ionpackman-ps-" + Guid.NewGuid().ToString("N"));
    private readonly ComponentStore _components;
    private readonly ProfileValidator _validator;
    private readonly ProfileStore _store;
    private readonly AllowList _allow;

    public ProfilesScreenTests()
    {
        foreach (string sub in new[] { "Components", "Profiles", "Config" })
            CopyDir(Path.Combine(AppContext.BaseDirectory, sub), Path.Combine(_dir, sub));

        var (list, errors) = AllowList.Load(Paths.AllowListIni(_dir));
        Assert.Empty(errors);
        _allow = list!;
        _components = new ComponentStore(_dir);
        _validator = new ProfileValidator(_components, _allow);
        _store = new ProfileStore(_dir, _validator);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
        foreach (string d in Directory.GetDirectories(from)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }

    private string ProfileFile(string id) => Paths.ProfileIni(_dir, id);

    // Jendela jangkar: Application dimatikan WPF bila jendela terakhir ditutup, padahal Application dipakai bersama semua test.
    private static Window? _anchor;

    private ProfilesScreen NewScreen()
    {
        _anchor ??= new Window { Width = 10, Height = 10, WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000 };
        if (!_anchor.IsVisible) _anchor.Show();
        return new ProfilesScreen(_store, _components, _validator, _allow);
    }

    // ---------- alat bantu ----------

    private static T F<T>(ProfilesScreen s, string name) where T : class => (T)s.FindName(name);

    private static void Click(ButtonBase b) => b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static string P(object item, string property) => item.GetType().GetProperty(property)!.GetValue(item)!.ToString()!;

    private static List<object> Items(DataGrid grid) => grid.Items.Cast<object>().ToList();

    private static FrameworkElement Tagged(ProfilesScreen s, string tag) =>
        WpfHost.Descendants<FrameworkElement>(F<StackPanel>(s, "ComponentRows")).First(e => e.Tag as string == tag);

    private static string BannerText(ProfilesScreen s)
    {
        var banner = F<Notice>(s, "Banner");
        if (banner.Visibility != Visibility.Visible) return "";
        // Inline Run/LineBreak: gabungkan teks Run
        return string.Concat(((TextBlock)banner.Content).Inlines.Select(i => i is System.Windows.Documents.Run r ? r.Text : " "));
    }

    private static List<StatusPill> Chips(ProfilesScreen s) =>
        WpfHost.Descendants<StatusPill>(F<StackPanel>(s, "ComponentRows")).ToList();

    private static void Pick(ComboBox box, string idPart) =>
        box.SelectedItem = box.Items.Cast<object>().First(i => i.GetType().GetProperty("Id")!.GetValue(i)!.ToString() == idPart);

    /// <summary>Menampilkan layar di jendela di luar layar (style berlaku, sel DataGrid terbentuk).</summary>
    private static Window Show(ProfilesScreen s)
    {
        var w = new Window { Content = s, Width = ContentWidth, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = -20000 };
        w.Show();
        w.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        w.UpdateLayout();
        return w;
    }

    private void WriteProfile(string id, string body) =>
        File.WriteAllText(ProfileFile(id), "[Meta]\nSchemaVersion=1.0\n" + body);

    // ---------- tes ----------

    [Fact]
    public void Catalog_ListsSeedProfiles_WithValidStatus()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            var rows = Items(F<DataGrid>(s, "CatalogGrid"));

            Assert.Equal(new[] { "MDD", "QRISTAP" }, rows.Select(r => P(r, "Id")));
            Assert.All(rows, r => Assert.Equal("Valid", P(r, "StatusText")));
            Assert.Equal("4", P(rows[0], "ComponentsText"));
            Assert.Equal("3", P(rows[1], "ComponentsText"));
            Assert.Equal("1.0.0", P(rows[1], "Version"));
            Assert.Equal("Page 1 of 1 · 2 profiles", F<TextBlock>(s, "PageText").Text);
        });
    }

    [Fact]
    public void Catalog_FilterSearchAndPaging()
    {
        string seed = File.ReadAllText(ProfileFile("QRISTAP"));
        for (int i = 1; i <= 4; i++)
            File.WriteAllText(ProfileFile("EXTRA" + i), seed.Replace("Id=QRISTAP", "Id=EXTRA" + i).Replace("DisplayName=QRIS TAP", "DisplayName=Extra " + i));
        WriteProfile("EMPTY", "[Profile]\nId=EMPTY\nVersion=1.0.0\n");   // 7 profile; satu tanpa component

        WpfHost.Run(() =>
        {
            var s = NewScreen();
            var grid = F<DataGrid>(s, "CatalogGrid");
            var page = F<TextBlock>(s, "PageText");
            var next = F<Button>(s, "NextButton");
            var prev = F<Button>(s, "PrevButton");

            Assert.Equal("Page 1 of 2 · 7 profiles", page.Text);
            Assert.Equal(4, grid.Items.Count);
            Assert.False(prev.IsEnabled);
            Assert.True(next.IsEnabled);

            Click(next);
            Assert.Equal("Page 2 of 2 · 7 profiles", page.Text);
            Assert.Equal(3, grid.Items.Count);
            Assert.False(next.IsEnabled);

            F<TextBox>(s, "SearchBox").Text = "  mdd ";                    // di-trim, tidak peka huruf besar
            Assert.Equal(new[] { "MDD" }, Items(grid).Select(r => P(r, "Id")));
            Assert.Equal("Page 1 of 1 · 1 profile", page.Text);

            F<TextBox>(s, "SearchBox").Text = "";
            F<ComboBox>(s, "FilterBox").SelectedIndex = 2;               // Empty
            Assert.Equal(new[] { "EMPTY" }, Items(grid).Select(r => P(r, "Id")));
            F<ComboBox>(s, "FilterBox").SelectedIndex = 1;               // With components
            Assert.Equal("Page 1 of 2 · 6 profiles", page.Text);

            F<TextBox>(s, "SearchBox").Text = "nothing-like-this";
            Assert.Equal(Visibility.Visible, F<EmptyState>(s, "CatalogEmpty").Visibility);
        });
    }

    [Fact]
    public void OpenButton_LoadsTheProfileIntoTheForm()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            var w = Show(s);
            try
            {
                var open = WpfHost.Descendants<Button>(F<DataGrid>(s, "CatalogGrid")).First(b => b.Tag as string == "QRISTAP");
                Click(open);

                Assert.Equal("QRISTAP", F<TextBox>(s, "IdBox").Text);
                Assert.Equal("QRIS TAP", F<TextBox>(s, "NameBox").Text);
                Assert.Equal("1.0.0", F<TextBox>(s, "VersionBox").Text);
                Assert.Equal(3, F<StackPanel>(s, "ComponentRows").Children.Count);
                Assert.True(F<CheckBox>(s, "WebUiCheck").IsChecked);
                Assert.True(F<CheckBox>(s, "JnlLogCheck").IsChecked);
                Assert.True(F<Button>(s, "SaveButton").IsEnabled);
                Assert.Equal("Valid", F<StatusPill>(s, "ValidPill").Text);
                Assert.Equal("1.8.2", ((ComboBox)Tagged(s, "version:ion-middleware")).SelectedItem);
            }
            finally { w.Content = null; w.Close(); }
        });
    }

    [Fact]
    public void AddComponent_AddsOnce_AndRejectsDuplicates()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            Click(F<Button>(s, "NewButton"));
            var component = F<ComboBox>(s, "AddComponentBox");
            var version = F<ComboBox>(s, "AddVersionBox");
            var rows = F<StackPanel>(s, "ComponentRows");

            Assert.Equal(0, rows.Children.Count);
            Pick(component, "wind");
            Assert.Equal("2.1.0", version.SelectedItem);
            Click(F<Button>(s, "AddButton"));
            Assert.Equal(1, rows.Children.Count);
            Assert.Equal(Visibility.Collapsed, F<Notice>(s, "Banner").Visibility);

            Click(F<Button>(s, "AddButton"));                            // komponen yang sama lagi
            Assert.Equal(1, rows.Children.Count);
            Assert.Contains("already in this profile", BannerText(s));
            Assert.Equal(StatusKind.Warning, F<Notice>(s, "Banner").Kind);
        });
    }

    [Fact]
    public void JnlLog_IsDisabledWithHint_WhenWebUiIsOff()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            s.Open("QRISTAP");
            var webUi = F<CheckBox>(s, "WebUiCheck");
            var jnl = F<CheckBox>(s, "JnlLogCheck");
            var hint = F<TextBlock>(s, "JnlLogHint");
            Assert.True(jnl.IsEnabled);
            Assert.Equal(Visibility.Collapsed, hint.Visibility);

            webUi.IsChecked = false;

            Assert.False(jnl.IsEnabled);
            Assert.False(jnl.IsChecked);
            Assert.Equal(Visibility.Visible, hint.Visibility);
            Assert.Equal("Requires WIND WebUI", hint.Text);
            string preview = F<TextBox>(s, "PreviewBox").Text;
            Assert.Contains("WindWebUI=false", preview);
            Assert.Contains("JnlLog=false", preview);
            Assert.All(Items(F<DataGrid>(s, "ConfigGrid")), r => Assert.Equal("Not applied", P(r, "StateText")));

            webUi.IsChecked = true;                                       // dinyalakan lagi: JNLLog tetap mati
            Assert.True(jnl.IsEnabled);
            Assert.False(jnl.IsChecked);
        });
    }

    [Fact]
    public void ConfigTable_ShowsStateAndAllowList_ForQrisTap()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            s.Open("QRISTAP");
            var rows = Items(F<DataGrid>(s, "ConfigGrid"));

            Assert.Equal(6, rows.Count);
            Assert.All(rows, r => Assert.Equal("Enabled", P(r, "StateText")));
            Assert.All(rows, r => Assert.Equal("✔", P(r, "AllowSymbol")));
            Assert.Equal(2, rows.Count(r => P(r, "Scope") == "Registry"));
        });
    }

    [Fact]
    public void MissingDependency_IsMarkedRed_AndSaveIsDisabled()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            s.Open("QRISTAP");
            Assert.DoesNotContain(Chips(s), c => c.Kind == StatusKind.Error);

            Click((Button)Tagged(s, "remove:ion-middleware"));            // wind & qraptor masih bergantung padanya

            var missing = Chips(s).Where(c => c.Kind == StatusKind.Error).ToList();
            Assert.Equal(2, missing.Count);
            Assert.All(missing, c => Assert.Contains("ion-middleware (missing)", c.Text));
            Assert.False(F<Button>(s, "SaveButton").IsEnabled);
            Assert.Equal(StatusKind.Error, F<StatusPill>(s, "ValidPill").Kind);

            Click(F<Button>(s, "ValidateButton"));
            Assert.Equal(StatusKind.Error, F<Notice>(s, "Banner").Kind);
            Assert.Contains("depends on 'ion-middleware'", BannerText(s));
        });
    }

    [Fact]
    public void Validate_ReportsSuccess_ForValidProfile()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            s.Open("MDD");
            Click(F<Button>(s, "ValidateButton"));
            Assert.Equal(StatusKind.Success, F<Notice>(s, "Banner").Kind);
            Assert.Contains("valid", BannerText(s));
        });
    }

    [Fact]
    public void EditingDependsOn_IsValidated_AndRedrawsChips()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            s.Open("QRISTAP");
            var box = (TextBox)Tagged(s, "deps:wind");

            box.Text = "ion-middleware,  qraptor ,ion-middleware";
            box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Contains("qraptor", F<TextBox>(s, "PreviewBox").Text);
            Assert.Contains("DependsOn=ion-middleware,qraptor", F<TextBox>(s, "PreviewBox").Text);
            Assert.Equal("ion-middleware, qraptor", ((TextBox)Tagged(s, "deps:wind")).Text);   // baris dibuat ulang, rapi

            var again = (TextBox)Tagged(s, "deps:wind");
            again.Text = "Bad Id";
            again.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Contains("not a valid component ID", BannerText(s));
            Assert.Equal("ion-middleware, qraptor", again.Text);                                 // dikembalikan

            var ghost = (TextBox)Tagged(s, "deps:wind");
            ghost.Text = "ghost-service";
            ghost.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
            Assert.Contains(Chips(s), c => c.Kind == StatusKind.Error && c.Text.StartsWith("ghost-service"));
            Assert.False(F<Button>(s, "SaveButton").IsEnabled);
        });
    }

    [Fact]
    public void EntryOutsideAllowList_ShowsCross_AndMakesProfileInvalid()
    {
        string text = File.ReadAllText(ProfileFile("QRISTAP"));
        File.WriteAllText(ProfileFile("QRISTAP"), text.Replace(@"File=D:\APCONTAINER\Config\GPFCNFG.ini", @"File=C:\Windows\win.ini"));

        WpfHost.Run(() =>
        {
            var s = NewScreen();
            Assert.Equal("Invalid", P(Items(F<DataGrid>(s, "CatalogGrid")).Single(r => P(r, "Id") == "QRISTAP"), "StatusText"));

            s.Open("QRISTAP");
            var bad = Items(F<DataGrid>(s, "ConfigGrid")).Where(r => P(r, "AllowSymbol") == "✖").ToList();
            Assert.Single(bad);
            Assert.Contains("not in the allow-list", P(bad[0], "AllowTip"));
            Assert.False(F<Button>(s, "SaveButton").IsEnabled);

            Click(F<Button>(s, "ValidateButton"));
            Assert.Contains("allow-list", BannerText(s));
        });
    }

    [Fact]
    public void Save_ValidProfile_WritesFile_AndRaisesMessage()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            var messages = new List<string>();
            s.Message += messages.Add;

            Click(F<Button>(s, "NewButton"));
            F<TextBox>(s, "IdBox").Text = "  NEWPROFILE ";                // spasi di-trim
            F<TextBox>(s, "NameBox").Text = " New profile ";
            Assert.False(F<Button>(s, "SaveButton").IsEnabled);          // belum ada component

            Pick(F<ComboBox>(s, "AddComponentBox"), "ion-middleware");
            Click(F<Button>(s, "AddButton"));
            Assert.True(F<Button>(s, "SaveButton").IsEnabled);
            Click(F<Button>(s, "SaveButton"));

            Assert.True(File.Exists(ProfileFile("NEWPROFILE")));
            var (saved, errors) = _store.Load("NEWPROFILE");
            Assert.Empty(errors);
            Assert.Equal("New profile", saved!.DisplayName);
            Assert.Equal("ion-middleware", Assert.Single(saved.Components).Id);
            Assert.Single(messages);
            Assert.Contains(Items(F<DataGrid>(s, "CatalogGrid")).Concat(Items(F<DataGrid>(s, "CatalogGrid"))), r => P(r, "Id") == "NEWPROFILE");
        });
    }

    [Fact]
    public void Save_InvalidProfile_DoesNotWriteAnything()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            string before = File.ReadAllText(ProfileFile("QRISTAP"));
            s.Open("QRISTAP");
            Click((Button)Tagged(s, "remove:ion-middleware"));
            F<TextBox>(s, "VersionBox").Text = "9.9.9";
            Assert.False(F<Button>(s, "SaveButton").IsEnabled);

            Click(F<Button>(s, "SaveButton"));                            // meski dipaksa, store menolak

            Assert.Equal(before, File.ReadAllText(ProfileFile("QRISTAP")));
            Assert.Equal(StatusKind.Error, F<Notice>(s, "Banner").Kind);

            Click(F<Button>(s, "NewButton"));
            F<TextBox>(s, "IdBox").Text = "ONLYID";
            Click(F<Button>(s, "SaveButton"));
            Assert.False(File.Exists(ProfileFile("ONLYID")));
        });
    }

    [Fact]
    public void IniPreview_FollowsTheForm()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            s.Open("QRISTAP");
            var preview = F<TextBox>(s, "PreviewBox");
            Assert.True(preview.IsReadOnly);
            Assert.Contains("Id=QRISTAP", preview.Text);

            F<TextBox>(s, "IdBox").Text = "CHANGED";
            F<TextBox>(s, "VersionBox").Text = "2.5.0";
            ((ComboBox)Tagged(s, "version:qraptor")).SelectedIndex = 0;

            Assert.Contains("Id=CHANGED", preview.Text);
            Assert.Contains("Version=2.5.0", preview.Text);
            Assert.Contains("[Component:qraptor]", preview.Text);
            Assert.Contains(@"Profiles\CHANGED.ini", F<TextBlock>(s, "PreviewPath").Text);
        });
    }

    [Fact]
    public void CorruptProfileFile_ShowsNotice_AndTheRestStillWorks()
    {
        File.WriteAllText(ProfileFile("BROKEN"), "this is [[ not an ini file\n=\n");

        WpfHost.Run(() =>
        {
            var s = NewScreen();
            var rows = Items(F<DataGrid>(s, "CatalogGrid"));

            Assert.Equal(Visibility.Visible, F<Notice>(s, "CatalogNotice").Visibility);
            Assert.Equal(StatusKind.Error, F<Notice>(s, "CatalogNotice").Kind);
            Assert.Equal("Unreadable", P(rows.Single(r => P(r, "Id") == "BROKEN"), "StatusText"));
            Assert.Equal(3, rows.Count);

            s.Open("BROKEN");                                             // tidak crash, form tidak berubah
            Assert.Contains("Cannot open profile 'BROKEN'", BannerText(s));
            s.Open("MDD");
            Assert.Equal("MDD", F<TextBox>(s, "IdBox").Text);
        });
    }

    [Fact]
    public void ImportAndExport_RoundTrip()
    {
        WpfHost.Run(() =>
        {
            var s = NewScreen();
            var messages = new List<string>();
            s.Message += messages.Add;
            s.Open("MDD");

            string export = Path.Combine(_dir, "exported.ini");
            Assert.True(s.ExportTo(export));
            string text = File.ReadAllText(export).Replace("Id=MDD", "Id=MDD2");
            string incoming = Path.Combine(_dir, "incoming.ini");
            File.WriteAllText(incoming, text);

            s.ImportFrom(incoming);

            Assert.True(File.Exists(ProfileFile("MDD2")));
            Assert.Equal("MDD2", F<TextBox>(s, "IdBox").Text);
            Assert.Equal(2, messages.Count);

            File.WriteAllText(incoming, "garbage");
            s.ImportFrom(incoming);
            Assert.Equal(StatusKind.Error, F<Notice>(s, "Banner").Kind);

            Click(F<Button>(s, "NewButton"));
            Assert.False(s.ExportTo(Path.Combine(_dir, "nope.ini")));     // form kosong tidak valid
            Assert.False(File.Exists(Path.Combine(_dir, "nope.ini")));
        });
    }

    [Fact]
    public void EveryTab_RendersWithin808px_WithoutHorizontalOverflow()
    {
        string[] names = ["components", "registry", "preview"];
        string outDir = Path.GetTempPath();

        for (int tab = 0; tab < 3; tab++)
        {
            int index = tab;
            WpfHost.Run(() =>
            {
                var s = NewScreen();
                s.Open("QRISTAP");
                F<TabControl>(s, "Tabs").SelectedIndex = index;
                string png = Path.Combine(outDir, $"ionpackman-profiles-{names[index]}.png");

                var (width, height) = WpfHost.RenderToPng(s, ContentWidth, png);

                Assert.True(File.Exists(png) && new FileInfo(png).Length > 5_000);
                Assert.True(width <= ContentWidth + 0.5);
                Assert.True(height > 400);
                Assert.True(s.DesiredSize.Width <= ContentWidth + 0.5, $"desired width {s.DesiredSize.Width}");

                // Tidak ada elemen yang menjulur melewati tepi kanan area konten (tanpa scroll horizontal).
                var w = Show(NewScreen());
                try
                {
                    var screen = (ProfilesScreen)w.Content;
                    screen.Open("QRISTAP");
                    F<TabControl>(screen, "Tabs").SelectedIndex = index;
                    w.UpdateLayout();
                    foreach (var e in WpfHost.Descendants<FrameworkElement>(screen).Where(e => e.IsVisible && e.ActualWidth > 0))
                    {
                        double right = e.TransformToAncestor(screen).Transform(new Point(e.ActualWidth, 0)).X;
                        if (e is not ScrollBar && e.GetType().Name != "DataGridCellsPresenter")
                            Assert.True(right <= ContentWidth + 0.5, $"{e.GetType().Name} '{e.Name}' ends at {right}px");
                    }
                }
                finally { w.Content = null; w.Close(); }
            });
        }
    }
}
