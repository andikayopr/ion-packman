using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IonPackMan.Components;
using IonPackMan.Controls;
using IonPackMan.Machine;

namespace IonPackMan.Screens;

/// <summary>
/// Layar Components (UI/UX Spec §4.2): katalog, form release, pratinjau INI.
/// Hanya tampilan; aturan isi dan penyimpanan ada di ComponentRelease dan ComponentStore.
/// </summary>
public partial class ComponentsScreen : UserControl, IScreen, IListDetailScreen
{
    private const int PageSize = 5;

    /// <summary>Satu baris tabel katalog.</summary>
    private sealed record CatalogRow(string Id, string Display, string Version, string LocalBuild, bool HasVersions, bool HasBuild);

    private readonly ComponentStore _store;
    private readonly AllowList _allowList;
    private readonly Func<string, string, string?>? _localBuildLabel;

    private List<CatalogEntry> _entries = [];
    private List<CatalogRow> _rows = [];
    private int _page;
    private bool _ready;
    private bool _loadingForm;

    /// <summary>Dipicu bila pengguna meminta import local build untuk release yang tampil (sudah tersimpan).</summary>
    public event Action<ComponentRelease>? ImportBuildRequested;
    /// <summary>Pengguna ingin memakai release ini di Developer Console (P1-4); MainWindow membuka konsol dan memilihnya.</summary>
    public event Action<ComponentRelease>? UseInConsoleRequested;

    /// <summary>Pesan singkat sukses untuk toast di MainWindow.</summary>
    public event Action<string>? Message;

    public ComponentsScreen(ComponentStore store, AllowList allowList, Func<string, string, string?>? localBuildLabel = null)
    {
        _store = store;
        _allowList = allowList;
        _localBuildLabel = localBuildLabel;
        InitializeComponent();
        // Tooltip tetap muncul pada tombol nonaktif.
        UseInConsoleButton.SetValue(ToolTipService.ShowOnDisabledProperty, true);
        ImportBuildButton.SetValue(ToolTipService.ShowOnDisabledProperty, true);
        _ready = true;
        Refresh();
    }

    /// <summary>Muat ulang katalog dan tabel (form yang sedang diisi dipertahankan).</summary>
    public void Refresh()
    {
        var (entries, errors) = _store.LoadCatalog();
        _entries = entries;
        if (errors.Count > 0)
        {
            Show(CatalogNotice, StatusKind.Error, errors.Prepend("The component catalog could not be loaded."));
            _entries = [];
        }
        else
        {
            CatalogNotice.Visibility = Visibility.Collapsed;
        }

        _rows = _entries.SelectMany(BuildRows).ToList();

        // Isi ulang pilihan Component, pertahankan pilihan lama bila masih ada.
        string? selected = SelectedId;
        _loadingForm = true;
        ComponentBox.Items.Clear();
        foreach (var e in _entries)
            ComponentBox.Items.Add(new ComboBoxItem { Content = $"{e.DisplayName} ({e.Id})", Tag = e.Id });
        ComponentBox.SelectedItem = ComponentBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == selected);
        _loadingForm = false;

        ApplyCatalogFilter(resetPage: true);
        OnFormStateChanged();
    }

    // ---------- Katalog ----------

    // Satu baris per release (component@versi), terbaru dulu. Component tanpa release tetap tampil satu baris "None".
    private IEnumerable<CatalogRow> BuildRows(CatalogEntry entry)
    {
        string display = $"{entry.DisplayName} ({entry.Id})";
        var versions = _store.ListVersions(entry.Id);
        if (versions.Count == 0)
        {
            yield return new CatalogRow(entry.Id, display, "None", "Not imported", false, false);
            yield break;
        }
        foreach (string version in versions)
        {
            string? build = SafeBuildLabel(entry.Id, version);
            yield return new CatalogRow(entry.Id, display, version, build ?? "Not imported", true, build is not null);
        }
    }

    private string? SafeBuildLabel(string id, string version)
    {
        try { return _localBuildLabel?.Invoke(id, version); }
        catch (Exception) { return null; }   // label hanya info tambahan; jangan jatuhkan layar
    }

    private void ApplyCatalogFilter(bool resetPage)
    {
        if (resetPage) _page = 0;

        string q = SearchBox.Text.Trim();
        int filter = Math.Max(FilterBox.SelectedIndex, 0);
        var filtered = _rows.Where(r =>
            (q.Length == 0 || r.Display.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Version.Contains(q, StringComparison.OrdinalIgnoreCase)) &&
            filter switch { 1 => r.HasVersions, 2 => r.HasBuild, 3 => !r.HasVersions, _ => true }).ToList();

        int pages = Math.Max(1, (filtered.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);

        CatalogGrid.ItemsSource = filtered.Skip(_page * PageSize).Take(PageSize).ToList();
        PageText.Text = $"Page {_page + 1} of {pages} · {filtered.Count} release{(filtered.Count == 1 ? "" : "s")}";
        PrevButton.IsEnabled = _page > 0;
        NextButton.IsEnabled = _page < pages - 1;
    }

    private void OnCatalogFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) ApplyCatalogFilter(resetPage: true);
    }

    private void OnPrevPage(object sender, RoutedEventArgs e) { _page--; ApplyCatalogFilter(false); }
    private void OnNextPage(object sender, RoutedEventArgs e) { _page++; ApplyCatalogFilter(false); }

    private void OnOpenRow(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CatalogRow row)
            OpenComponent(row.Id, row.HasVersions ? row.Version : null);
    }

    // Klik dua kali pada baris sama dengan Open (tombol Open tetap ada supaya jelas bisa diklik).
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) is null
            && CatalogGrid.SelectedItem is CatalogRow row)
            OpenComponent(row.Id, row.HasVersions ? row.Version : null);
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var node = start; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
            if (node is T found)
                return found;
        return null;
    }

    // ---------- Daftar <-> form ----------

    /// <summary>True bila form release yang tampil (bukan daftar).</summary>
    public bool IsDetailShown => DetailPanel.Visibility == Visibility.Visible;

    private void ShowDetail()
    {
        ListPanel.Visibility = ListHead.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = DetailHead.Visibility = Visibility.Visible;
        OnFormStateChanged();   // judul mengikuti isi form
    }

    /// <summary>Kembali ke daftar dengan data terbaru (release baru/terhapus langsung terlihat).</summary>
    public void ShowList()
    {
        DetailPanel.Visibility = DetailHead.Visibility = Visibility.Collapsed;
        ListPanel.Visibility = ListHead.Visibility = Visibility.Visible;
        Refresh();
    }

    private void OnBack(object sender, RoutedEventArgs e) => ShowList();

    /// <summary>Form kosong untuk release baru; memilih component memuat release terbarunya sebagai titik awal.</summary>
    public void NewRelease()
    {
        _loadingForm = true;
        ComponentBox.SelectedItem = null;
        VersionBox.Text = BuildRefBox.Text = ServiceBox.Text = TargetBox.Text = "";
        _loadingForm = false;
        ReleaseNotice.Visibility = Visibility.Collapsed;
        ShowDetail();
        Show(ReleaseNotice, StatusKind.Info, ["Choose a component. Its latest release is loaded as a starting point; change the version to save a new release."]);
    }

    private void OnNewRelease(object sender, RoutedEventArgs e) => NewRelease();

    /// <summary>Pilih component dan muat <paramref name="version"/> (null = versi tersimpan terbaru); bila belum ada, form dikosongkan.</summary>
    public void OpenComponent(string id, string? version = null)
    {
        ShowDetail();
        SelectComponent(id);
        version ??= _store.ListVersions(id).FirstOrDefault();
        if (version is null)
        {
            FillForm(null, id);
            Show(ReleaseNotice, StatusKind.Info, ["No saved release for this component yet. Fill in the form and choose Save Component."]);
            return;
        }
        LoadVersion(id, version);
    }

    // ---------- Form ----------

    private string? SelectedId => (ComponentBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private void SelectComponent(string id)
    {
        _loadingForm = true;
        ComponentBox.SelectedItem = ComponentBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == id);
        _loadingForm = false;
    }

    private void FillForm(ComponentRelease? release, string id)
    {
        _loadingForm = true;
        SelectComponent(id);
        VersionBox.Text = release?.ReleaseVersion ?? "";
        BuildRefBox.Text = release?.BuildReference ?? "";
        ServiceBox.Text = release?.ServiceName ?? "";
        TargetBox.Text = release?.TargetDirectory ?? "";
        _loadingForm = false;
        OnFormStateChanged();
    }

    /// <summary>Isi release dari form (spasi di tepi dibuang). Null bila Component belum dipilih.</summary>
    private ComponentRelease? BuildRelease()
    {
        if (SelectedId is not { } id)
            return null;
        return new ComponentRelease
        {
            Id = id,
            DisplayName = _entries.FirstOrDefault(e => e.Id == id)?.DisplayName ?? "",
            ReleaseVersion = VersionBox.Text.Trim(),
            BuildReference = BuildRefBox.Text.Trim(),
            TargetDirectory = TargetBox.Text.Trim(),
            ServiceName = ServiceBox.Text.Trim(),
        };
    }

    private void OnFormChanged(object sender, RoutedEventArgs e)
    {
        if (_ready && !_loadingForm) OnFormStateChanged();
    }

    /// <summary>Segarkan pratinjau INI, status tombol, dan teks bantuan.</summary>
    private void OnFormStateChanged()
    {
        var release = BuildRelease();
        if (release is null)
        {
            IniBox.Text = "";
        }
        else
        {
            try { IniBox.Text = release.ToIni().ToText(); }
            catch (ArgumentException ex) { IniBox.Text = "; " + ex.Message; }   // nilai yang tidak bisa jadi INI
        }

        List<string> saved = release is null ? new() : _store.ListVersions(release.Id);
        bool isSaved = release is not null && saved.Contains(release.ReleaseVersion);
        ImportBuildButton.IsEnabled = isSaved;
        UseInConsoleButton.IsEnabled = isSaved;
        UseInConsoleButton.ToolTip = isSaved ? "Open this release in the Developer Console" : "Save the release first";
        DetailTitle.Text = isSaved ? $"{_entries.FirstOrDefault(e => e.Id == release!.Id)?.DisplayName ?? release!.Id} {release!.ReleaseVersion}" : "New release";
        DetailSubtitle.Text = release is null ? "Choose a component to start."
            : isSaved ? "Saved release. Change the version and save to add another release."
            : "Not saved yet. Fill in the form and choose Save Component.";
        DeleteButton.IsEnabled = isSaved;
        DeleteButton.ToolTip = isSaved ? "Delete this release. Other versions and imported builds are kept." : "Only a saved release can be deleted";
        SavedVersionsText.Text = release is null ? "Choose a component to see its saved versions."
            : saved.Count == 0 ? "No saved versions yet."
            : "Saved versions: " + string.Join(", ", saved) + (!isSaved && release.ReleaseVersion.Length > 0 ? $". Saving {release.ReleaseVersion} creates a new release." : "");

        string? build = isSaved ? SafeBuildLabel(release!.Id, release.ReleaseVersion) : null;
        BuildText.Text = build is null ? "" : "Imported: " + build;
    }

    // Memilih component dari dropdown memuat release terbarunya (atau mengosongkan form bila belum ada).
    private void OnComponentChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_loadingForm && SelectedId is { } id)
            OpenComponent(id);
    }

    private void LoadVersion(string id, string version)
    {
        var (release, errors) = _store.Load(id, version);
        if (release is null)
        {
            Show(ReleaseNotice, StatusKind.Error, errors.Count > 0 ? errors : [$"Release {id}@{version} was not found."]);
            return;
        }
        FillForm(release, id);
        Show(ReleaseNotice, StatusKind.Info, [$"Loaded {id}@{version}."]);
    }

    // ---------- Tab ----------

    private void OnShowForm(object sender, RoutedEventArgs e) => ShowTab(ini: false);
    private void OnShowIni(object sender, RoutedEventArgs e) => ShowTab(ini: true);

    private void ShowTab(bool ini)
    {
        FormPanel.Visibility = ini ? Visibility.Collapsed : Visibility.Visible;
        IniBox.Visibility = ini ? Visibility.Visible : Visibility.Collapsed;
        FormTab.Tag = ini ? null : "active";
        IniTab.Tag = ini ? "active" : null;
    }

    // ---------- Validate / Save ----------

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var release = BuildRelease();
        if (release is null) { Show(ReleaseNotice, StatusKind.Error, ["Choose a component first."]); return; }

        var problems = release.Validate(_allowList);
        if (problems.Count > 0) Show(ReleaseNotice, StatusKind.Error, problems);
        else Show(ReleaseNotice, StatusKind.Success, ["No problems found."]);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var release = BuildRelease();
        if (release is null) { Show(ReleaseNotice, StatusKind.Error, ["Choose a component first."]); return; }

        var problems = _store.Save(release, _allowList);
        if (problems.Count > 0)
        {
            Show(ReleaseNotice, StatusKind.Error, problems);
            return;
        }

        Refresh();
        Show(ReleaseNotice, StatusKind.Success, [$"Saved {release.Id}@{release.ReleaseVersion}."]);
        Message?.Invoke("Component release saved.");
    }

    // ---------- Import / Export INI ----------

    /// <summary>Baca file INI, tolak bila Id berbeda dari component terpilih, lalu simpan dan tampilkan.</summary>
    public void ImportIni(string path)
    {
        if (SelectedId is not { } id)
        {
            Show(ReleaseNotice, StatusKind.Error, ["Choose a component first."]);
            return;
        }

        var (release, errors) = _store.Import(path, id, _allowList);
        if (release is null)
        {
            Show(ReleaseNotice, StatusKind.Error, errors);
            return;
        }

        Refresh();
        FillForm(release, id);
        Show(ReleaseNotice, StatusKind.Success, [$"Imported and saved {release.Id}@{release.ReleaseVersion}."]);
        Message?.Invoke("Component release imported.");
    }

    /// <summary>Tulis isi form ke file (hanya bila valid, supaya file hasil ekspor selalu bisa diimpor lagi).</summary>
    public void ExportIni(string path)
    {
        var release = BuildRelease();
        if (release is null) { Show(ReleaseNotice, StatusKind.Error, ["Choose a component first."]); return; }

        var problems = release.Validate(_allowList);
        if (problems.Count > 0) { Show(ReleaseNotice, StatusKind.Error, problems); return; }

        try
        {
            release.ToIni().Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Show(ReleaseNotice, StatusKind.Error, [$"Cannot export to {path}: {ex.Message}"]);
            return;
        }
        Show(ReleaseNotice, StatusKind.Success, [$"Exported to {path}."]);
        Message?.Invoke("Component INI exported.");
    }

    private void OnDeleteRelease(object sender, RoutedEventArgs e)
    {
        if (BuildRelease() is not { } release)
            return;
        var answer = MessageBox.Show(
            $"Delete the release {release.Id} {release.ReleaseVersion}?\nOther versions and the imported build stay. A profile that uses this version becomes invalid until you change its version.",
            "Delete release", MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (answer == MessageBoxResult.OK)
            DeleteRelease();
    }

    /// <summary>Menghapus release yang sedang tampil di form (hanya yang sudah tersimpan); konfirmasi ada di handler tombol.</summary>
    public void DeleteRelease()
    {
        var release = BuildRelease();
        if (release is null) { Show(ReleaseNotice, StatusKind.Error, ["Choose a component first."]); return; }

        var problems = _store.Delete(release.Id, release.ReleaseVersion);
        if (problems.Count > 0)
        {
            Show(ReleaseNotice, StatusKind.Error, problems);
            return;
        }

        Refresh();
        OpenComponent(release.Id);   // tampilkan release terbaru yang tersisa (atau form kosong)
        Show(ReleaseNotice, StatusKind.Success, [$"Deleted {release.Id}@{release.ReleaseVersion}. Its imported build, if any, stays in the local cache."]);
        Message?.Invoke("Component release deleted.");
    }

    private void OnUseInConsole(object sender, RoutedEventArgs e)
    {
        if (BuildRelease() is { } release)
            UseInConsoleRequested?.Invoke(release);
    }

    private void OnImportBuild(object sender, RoutedEventArgs e)
    {
        if (BuildRelease() is { } release)
            ImportBuildRequested?.Invoke(release);
    }

    // ---------- Notice ----------

    /// <summary>Satu baris per pesan.</summary>
    private static void Show(Notice notice, StatusKind kind, IEnumerable<string> lines)
    {
        var panel = new StackPanel();
        foreach (var line in lines)
            panel.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
        notice.Kind = kind;
        notice.Content = panel;
        notice.Visibility = Visibility.Visible;
    }
}
