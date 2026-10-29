using System.IO;
using System.Windows;
using System.Windows.Controls;
using IonPackMan.Components;
using IonPackMan.Controls;
using IonPackMan.Machine;

namespace IonPackMan.Screens;

/// <summary>
/// Layar Components (UI/UX Spec §4.2): katalog, form release, pratinjau INI.
/// Hanya tampilan; aturan isi dan penyimpanan ada di ComponentRelease dan ComponentStore.
/// </summary>
public partial class ComponentsScreen : UserControl, IScreen
{
    private const int PageSize = 4;

    /// <summary>Satu baris tabel katalog.</summary>
    private sealed record CatalogRow(string Id, string Display, string Versions, string LocalBuild, bool HasVersions, bool HasBuild);

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
            // Sisa file dari sebelum aturan satu release: tidak dipakai di mana pun, tapi pengguna harus tahu dan bisa membereskannya.
            var ignored = _entries.Select(e => (e.Id, Versions: _store.IgnoredVersions(e.Id))).Where(x => x.Versions.Count > 0).ToList();
            if (ignored.Count == 0)
                CatalogNotice.Visibility = Visibility.Collapsed;
            else
                Show(CatalogNotice, StatusKind.Warning, ignored.Select(x => $"{x.Id}: release file {string.Join(", ", x.Versions)} is ignored because a newer release exists. Save the component to remove it.")
                    .Prepend("A component can have only one release."));
        }

        _rows = _entries.Select(BuildRow).ToList();

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

    private CatalogRow BuildRow(CatalogEntry entry)
    {
        var versions = _store.ListVersions(entry.Id);
        string? build = null;
        foreach (var v in versions)
        {
            build = SafeBuildLabel(entry.Id, v);
            if (build is not null) break;
        }
        return new CatalogRow(entry.Id, $"{entry.DisplayName} ({entry.Id})",
            versions.Count > 0 ? string.Join(", ", versions) : "None",
            build ?? "Not imported", versions.Count > 0, build is not null);
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
            (q.Length == 0 || r.Display.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Versions.Contains(q, StringComparison.OrdinalIgnoreCase)) &&
            filter switch { 1 => r.HasVersions, 2 => r.HasBuild, 3 => !r.HasVersions, _ => true }).ToList();

        int pages = Math.Max(1, (filtered.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(_page, 0, pages - 1);

        CatalogGrid.ItemsSource = filtered.Skip(_page * PageSize).Take(PageSize).ToList();
        PageText.Text = $"Page {_page + 1} of {pages} · {filtered.Count} component{(filtered.Count == 1 ? "" : "s")}";
        PrevButton.IsEnabled = _page > 0;
        NextButton.IsEnabled = _page < pages - 1;
    }

    private void OnCatalogFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_ready) ApplyCatalogFilter(resetPage: true);
    }

    private void OnPrevPage(object sender, RoutedEventArgs e) { _page--; ApplyCatalogFilter(false); }
    private void OnNextPage(object sender, RoutedEventArgs e) { _page++; ApplyCatalogFilter(false); }

    private void OnOpenRow(object sender, RoutedEventArgs e) => OpenComponent((string)((FrameworkElement)sender).Tag);

    /// <summary>Pilih component dan muat versi tersimpan terbaru; bila belum ada, form dikosongkan.</summary>
    public void OpenComponent(string id)
    {
        SelectComponent(id);
        var latest = _store.ListVersions(id).FirstOrDefault();
        if (latest is null)
        {
            FillForm(null, id);
            Show(ReleaseNotice, StatusKind.Info, ["No saved release for this component yet. Fill in the form and choose Save Component."]);
            return;
        }
        LoadVersion(id, latest);
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
        SavedVersionsText.Text = release is null ? "Choose a component to see its saved release."
            : saved.Count == 0 ? "No release saved yet."
            : isSaved ? $"Saved version: {saved[0]}."
            : $"Saved version: {saved[0]}. Saving {release.ReleaseVersion} replaces it.";

        string? build = isSaved ? SafeBuildLabel(release!.Id, release.ReleaseVersion) : null;
        BuildText.Text = build is null ? "" : "Imported: " + build;
    }

    // Satu component = satu release: memilih component langsung memuat release-nya (atau mengosongkan form bila belum ada).
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
