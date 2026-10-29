using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using IonPackMan.Components;
using IonPackMan.Controls;
using IonPackMan.Deployment;
using IonPackMan.Machine;
using IonPackMan.Profiles;
using Microsoft.Win32;

namespace IonPackMan.Screens;

/// <summary>
/// Layar Profiles (T12): katalog profile + definisi profile (Components, Registry &amp; INI, INI preview).
/// Hanya tampilan: aturan ada di ProfileValidator, ConfigResolver dan ProfileStore.
/// </summary>
public partial class ProfilesScreen : UserControl, IScreen
{
    private const int PageSize = 4;

    private readonly ProfileStore _store;
    private readonly ComponentStore _components;
    private readonly ProfileValidator _validator;
    private readonly AllowList _allowList;
    private readonly ProfileSwitcher? _switcher;
    private readonly string _machineName;

    private List<CatalogEntry> _catalog = [];
    private List<CatalogRow> _rows = [];
    private Profile _profile = NewProfile();
    private int _page;
    private bool _loading;     // true saat kode mengisi form (abaikan event)
    private bool _ready;       // true setelah konstruktor selesai

    /// <summary>Pesan singkat untuk toast di MainWindow.</summary>
    public event Action<string>? Message;

    /// <param name="switcher">null bila machine.ini atau allowlist.ini tidak valid: tombol Switch dinonaktifkan.</param>
    public ProfilesScreen(ProfileStore store, ComponentStore components, ProfileValidator validator, AllowList allowList,
        ProfileSwitcher? switcher = null, string machineName = "")
    {
        _switcher = switcher;
        _machineName = machineName;
        _store = store;
        _components = components;
        _validator = validator;
        _allowList = allowList;
        InitializeComponent();
        _ready = true;
        Refresh();
        LoadIntoForm(_profile);
    }

    // ---------- Publik ----------

    /// <summary>Membaca ulang katalog component dan daftar profile; isi form yang sedang diedit dipertahankan.</summary>
    public void Refresh()
    {
        var (entries, catalogErrors) = _components.LoadCatalog();
        _catalog = entries;

        string? selected = (AddComponentBox.SelectedItem as ComponentChoice)?.Id;
        AddComponentBox.ItemsSource = entries.Select(e => new ComponentChoice(e.Id, $"{e.DisplayName} ({e.Id})")).ToList();
        AddComponentBox.SelectedItem = ((IEnumerable<ComponentChoice>)AddComponentBox.ItemsSource).FirstOrDefault(c => c.Id == selected)
                                       ?? ((IEnumerable<ComponentChoice>)AddComponentBox.ItemsSource).FirstOrDefault();

        var problems = new List<string>(catalogErrors);
        _rows = [];
        foreach (string id in _store.ListIds())
        {
            var (profile, errors) = _store.Load(id);
            if (profile is null)
            {
                problems.AddRange(errors);
                _rows.Add(new CatalogRow(id, id, "—", -1, "Unreadable", StatusKind.Error, false, false));
                continue;
            }
            bool valid = _validator.Validate(profile).Count == 0;
            string title = profile.DisplayName.Length > 0 && profile.DisplayName != id ? $"{profile.DisplayName} ({id})" : id;
            _rows.Add(new CatalogRow(id, title, profile.Version, profile.Components.Count,
                valid ? "Valid" : "Invalid", valid ? StatusKind.Success : StatusKind.Error, true,
                CanSwitch: valid && _switcher is not null && profile.Components.Count > 0));
        }

        ShowNotice(CatalogNotice, problems.Count == 0 ? null : problems, "Some profile data cannot be read:");
        ApplyCatalogFilter();
        RebuildComponentRows();
        Recompute();
    }

    /// <summary>Memuat profile tersimpan ke form. Bila file rusak: tampilkan error dan biarkan form apa adanya.</summary>
    public void Open(string profileId)
    {
        var (profile, errors) = _store.Load(profileId);
        if (profile is null)
        {
            ShowNotice(Banner, errors, $"Cannot open profile '{profileId}':", StatusKind.Error);
            return;
        }
        HideBanner();
        LoadIntoForm(profile);
    }

    /// <summary>Import INI dari path (dipanggil setelah dialog dipilih).</summary>
    public void ImportFrom(string path)
    {
        var (profile, errors) = _store.Import(path);
        if (profile is null)
        {
            ShowNotice(Banner, errors, "Import failed:", StatusKind.Error);
            return;
        }
        Refresh();
        LoadIntoForm(profile);
        ShowNotice(Banner, [$"Imported profile '{profile.Id}'."], null, StatusKind.Success);
        Message?.Invoke($"Profile '{profile.Id}' imported.");
    }

    /// <summary>Export profile yang sedang diedit ke path (hanya bila valid).</summary>
    public bool ExportTo(string path)
    {
        var problems = _validator.Validate(_profile);
        if (problems.Count > 0)
        {
            ShowNotice(Banner, problems, "Fix these issues before exporting:", StatusKind.Error);
            return false;
        }
        try
        {
            _profile.ToIni().Save(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowNotice(Banner, [ex.Message], "Export failed:", StatusKind.Error);
            return false;
        }
        ShowNotice(Banner, [$"Exported to {path}"], null, StatusKind.Success);
        Message?.Invoke($"Profile '{_profile.Id}' exported.");
        return true;
    }

    // ---------- Tombol ----------

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Import profile INI", Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
            ImportFrom(dialog.FileName);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export profile INI", Filter = "INI files (*.ini)|*.ini", FileName = (_profile.Id.Length > 0 ? _profile.Id : "Profile") + ".ini",
        };
        if (dialog.ShowDialog() == true)
            ExportTo(dialog.FileName);
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        var problems = _validator.Validate(_profile);
        if (problems.Count == 0)
            ShowNotice(Banner, ["Profile is valid."], null, StatusKind.Success);
        else
            ShowNotice(Banner, problems, $"{problems.Count} issue(s) found:", StatusKind.Error);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var problems = _store.Save(_profile);
        if (problems.Count > 0)
        {
            ShowNotice(Banner, problems, "Profile was not saved:", StatusKind.Error);
            return;
        }
        ShowNotice(Banner, [$"Profile '{_profile.Id}' saved."], null, StatusKind.Success);
        Message?.Invoke($"Profile '{_profile.Id}' saved.");
        Refresh();
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        HideBanner();
        LoadIntoForm(NewProfile());
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
            Open(id);
    }

    private void OnSwitchClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
            SwitchTo(id);
    }

    /// <summary>
    /// Membuka dialog pindah Profile (preview, lalu Switch). Hanya di mesin Development: PlanBuilder/Guards menolak sisanya dengan
    /// alasan yang tampil di dialog. Null bila tombol tidak tersedia (machine.ini atau allowlist.ini tidak valid).
    /// </summary>
    public SwitchProfileDialog? SwitchTo(string profileId)
    {
        if (_switcher is null)
            return null;

        var dialog = new SwitchProfileDialog(_switcher, profileId, _machineName);
        if (Window.GetWindow(this) is { } owner)
            dialog.Owner = owner;
        dialog.ShowDialog();

        if (dialog.Switched)
            Message?.Invoke($"Switched to profile '{profileId}'.");
        return dialog;
    }

    // ---------- Katalog ----------

    private void OnCatalogQueryChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        _page = 0;
        ApplyCatalogFilter();
    }

    private void OnPrevPage(object sender, RoutedEventArgs e) { _page--; ApplyCatalogFilter(); }
    private void OnNextPage(object sender, RoutedEventArgs e) { _page++; ApplyCatalogFilter(); }

    private void ApplyCatalogFilter()
    {
        string query = SearchBox.Text.Trim();
        int filter = FilterBox.SelectedIndex;      // 0 All, 1 With components, 2 Empty
        var matches = _rows.Where(r =>
            (query.Length == 0 || r.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || r.Version.Contains(query, StringComparison.OrdinalIgnoreCase))
            && (filter <= 0 || (filter == 1 && r.ComponentCount > 0) || (filter == 2 && r.ComponentCount == 0))).ToList();

        int pages = Math.Max(1, (int)Math.Ceiling(matches.Count / (double)PageSize));
        _page = Math.Clamp(_page, 0, pages - 1);
        CatalogGrid.ItemsSource = matches.Skip(_page * PageSize).Take(PageSize).ToList();
        CatalogEmpty.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CatalogGrid.Visibility = matches.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PageText.Text = $"Page {_page + 1} of {pages} · {matches.Count} profile{(matches.Count == 1 ? "" : "s")}";
        PrevButton.IsEnabled = _page > 0;
        NextButton.IsEnabled = _page < pages - 1;
    }

    // ---------- Form ----------

    private static Profile NewProfile() => new() { Id = "", Version = "1.0.0" };

    private void LoadIntoForm(Profile profile)
    {
        _loading = true;
        _profile = profile;
        IdBox.Text = profile.Id;
        NameBox.Text = profile.DisplayName;
        VersionBox.Text = profile.Version;
        _loading = false;
        RebuildComponentRows();
        Recompute();
    }

    /// <summary>Profile sesuai isi form (input di-trim).</summary>
    private Profile Current() => _profile with
    {
        Id = IdBox.Text.Trim(), DisplayName = NameBox.Text.Trim(), Version = VersionBox.Text.Trim(),
    };

    private void OnFormChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _loading) return;
        _profile = Current();
        Recompute();
    }

    private void OnFeatureChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready || _loading) return;
        bool webUi = WebUiCheck.IsChecked == true;
        if (webUi != _profile.WindWebUIEnabled)
            _profile = _profile.WithWindWebUI(webUi);      // mematikan WebUI otomatis mematikan JNLLog
        _profile = _profile with { JnlLogEnabled = JnlLogCheck.IsChecked == true && _profile.WindWebUIEnabled };
        Recompute();
    }

    // ---------- Add component ----------

    private void OnAddComponentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var versions = AddComponentBox.SelectedItem is ComponentChoice choice ? _components.ListVersions(choice.Id) : [];
        AddVersionBox.ItemsSource = versions;
        AddVersionBox.SelectedIndex = versions.Count > 0 ? 0 : -1;
        AddHint.Visibility = AddComponentBox.SelectedItem is not null && versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateAddButton();
    }

    private void OnAddVersionChanged(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateAddButton(); }

    private void UpdateAddButton() =>
        AddButton.IsEnabled = AddComponentBox.SelectedItem is not null && AddVersionBox.SelectedItem is not null;

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (AddComponentBox.SelectedItem is not ComponentChoice choice || AddVersionBox.SelectedItem is not string version)
            return;
        if (_profile.Components.Any(c => c.Id == choice.Id))
        {
            ShowNotice(Banner, [$"'{choice.Id}' is already in this profile. Change its version in the list instead."], null, StatusKind.Warning);
            return;
        }
        HideBanner();
        _profile = Current() with { Components = [.. _profile.Components, new ProfileComponent(choice.Id, version, [])] };
        RebuildComponentRows();
        Recompute();
    }

    // ---------- Daftar component ----------

    private void RebuildComponentRows()
    {
        ComponentRows.Children.Clear();
        var ids = _profile.Components.Select(c => c.Id).ToHashSet();
        foreach (var component in _profile.Components)
            ComponentRows.Children.Add(BuildComponentRow(component, ids));
        NoComponents.Visibility = _profile.Components.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private UIElement BuildComponentRow(ProfileComponent component, HashSet<string> ids)
    {
        string name = _catalog.FirstOrDefault(c => c.Id == component.Id)?.DisplayName ?? component.Id;

        var title = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0) };
        title.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
        title.Children.Add(new TextBlock { Text = component.Id, Style = (Style)FindResource("MutedText"), FontSize = 11 });

        // Versi: dipilih dari versi tersimpan (versi yang tidak tersimpan tetap tampil agar validator bisa menandainya)
        var versions = _components.ListVersions(component.Id);
        if (!versions.Contains(component.Version))
            versions.Insert(0, component.Version);
        var versionBox = new ComboBox { ItemsSource = versions, SelectedItem = component.Version, Tag = "version:" + component.Id, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        versionBox.SelectionChanged += (_, _) =>
        {
            if (_loading || versionBox.SelectedItem is not string v) return;
            _profile = Current() with { Components = _profile.Components.Select(c => c.Id == component.Id ? c with { Version = v } : c).ToList() };
            Recompute();
        };

        // DependsOn: chip (merah bila tidak ada di profile) + kotak teks daftar dipisah koma
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        foreach (string dep in component.DependsOn)
            chips.Children.Add(new StatusPill
            {
                Text = ids.Contains(dep) ? dep : $"{dep} (missing)",
                Kind = ids.Contains(dep) ? StatusKind.Neutral : StatusKind.Error,
                Margin = new Thickness(0, 0, 6, 4),
                ToolTip = ids.Contains(dep) ? null : $"'{dep}' is not in this profile.",
            });
        chips.Visibility = component.DependsOn.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        var depsBox = new TextBox
        {
            Text = string.Join(", ", component.DependsOn), Tag = "deps:" + component.Id, FontSize = 12,
            ToolTip = "Component IDs separated by commas",
        };
        depsBox.LostFocus += (_, _) => CommitDependencies(component.Id, depsBox);
        depsBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitDependencies(component.Id, depsBox); };
        var deps = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        deps.Children.Add(chips);
        deps.Children.Add(depsBox);

        var remove = new Button { Content = "Remove", Style = (Style)FindResource("RowButton"), Tag = "remove:" + component.Id, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
        remove.Click += (_, _) =>
        {
            HideBanner();
            _profile = Current() with { Components = _profile.Components.Where(c => c.Id != component.Id).ToList() };
            RebuildComponentRows();
            Recompute();
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        foreach (var (child, col) in new (UIElement, int)[] { (title, 0), (versionBox, 1), (deps, 2), (remove, 3) })
        {
            Grid.SetColumn(child, col);
            grid.Children.Add(child);
        }

        return new Border
        {
            Child = grid, Padding = new Thickness(11, 8, 11, 8),
            BorderBrush = (Brush)FindResource("Brush.RowLine"), BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    private void CommitDependencies(string componentId, TextBox box)
    {
        if (_loading) return;
        var deps = box.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
        string? bad = deps.FirstOrDefault(d => !ComponentRelease.IsValidId(d));
        if (bad is not null)
        {
            ShowNotice(Banner, [$"'{bad}' is not a valid component ID. Use lowercase letters, digits and '-', separated by commas."], null, StatusKind.Error);
            box.Text = string.Join(", ", _profile.Components.First(c => c.Id == componentId).DependsOn);
            return;
        }
        var current = _profile.Components.FirstOrDefault(c => c.Id == componentId);
        if (current is null || current.DependsOn.SequenceEqual(deps))
        {
            box.Text = string.Join(", ", deps);       // rapikan spasi
            return;
        }
        HideBanner();
        _profile = Current() with { Components = _profile.Components.Select(c => c.Id == componentId ? c with { DependsOn = deps } : c).ToList() };
        RebuildComponentRows();
        Recompute();
    }

    // ---------- Hitung ulang tampilan ----------

    private void Recompute()
    {
        _loading = true;
        WebUiCheck.IsChecked = _profile.WindWebUIEnabled;
        JnlLogCheck.IsChecked = _profile.JnlLogEnabled && _profile.WindWebUIEnabled;
        JnlLogCheck.IsEnabled = _profile.WindWebUIEnabled;
        JnlLogHint.Visibility = _profile.WindWebUIEnabled ? Visibility.Collapsed : Visibility.Visible;
        _loading = false;

        var problems = _validator.Validate(_profile);
        SaveButton.IsEnabled = problems.Count == 0;
        ValidPill.Kind = problems.Count == 0 ? StatusKind.Success : StatusKind.Error;
        ValidPill.Text = problems.Count == 0 ? "Valid" : $"{problems.Count} issue{(problems.Count == 1 ? "" : "s")}";

        var rows = _validator.Resolve(_profile).Select(r => new ConfigRow(r)).ToList();
        ConfigGrid.ItemsSource = rows;
        ConfigGrid.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        NoConfig.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            PreviewBox.Text = _profile.ToIni().ToText();
        }
        catch (ArgumentException ex)
        {
            PreviewBox.Text = "; Cannot build INI yet: " + ex.Message;
        }
        PreviewPath.Text = $"Profiles\\{(_profile.Id.Length > 0 ? _profile.Id : "<Id>")}.ini (read-only; edit using the form)";
    }

    // ---------- Notice ----------

    private void HideBanner() => Banner.Visibility = Visibility.Collapsed;

    private static void ShowNotice(Notice notice, IEnumerable<string>? lines, string? heading, StatusKind kind = StatusKind.Error)
    {
        var list = lines?.ToList();
        if (list is null || list.Count == 0)
        {
            notice.Visibility = Visibility.Collapsed;
            return;
        }
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        if (heading is not null)
            text.Inlines.Add(new Run(heading) { FontWeight = FontWeights.Bold });
        for (int i = 0; i < list.Count; i++)
        {
            if (heading is not null || i > 0)
                text.Inlines.Add(new LineBreak());
            text.Inlines.Add(new Run((list.Count > 1 ? "• " : "") + list[i]));
        }
        notice.Kind = kind;
        notice.Content = text;
        notice.Visibility = Visibility.Visible;
    }

    // ---------- Model baris tabel ----------

    private sealed record ComponentChoice(string Id, string Label)
    {
        public override string ToString() => Label;     // dipakai kotak pilihan yang terpilih
    }

    /// <param name="ComponentCount">-1 bila file tidak terbaca.</param>
    private sealed record CatalogRow(string Id, string Title, string Version, int ComponentCount, string StatusText, StatusKind StatusKind, bool CanOpen, bool CanSwitch)
    {
        public string ComponentsText => ComponentCount < 0 ? "—" : ComponentCount.ToString();
    }

    private sealed class ConfigRow
    {
        private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        public ConfigRow(ResolvedEntry e)
        {
            bool allowed = e.AllowListProblem is null;
            Scope = e.Scope;
            Target = e.Target;
            Entry = e.Type.Length > 0 ? $"{e.Entry} ({e.Type})" : e.Entry;
            Value = e.Value;
            StateText = e.State == EntryState.Enabled ? "Enabled" : "Not applied";
            StateKind = e.State == EntryState.Enabled ? StatusKind.Success : StatusKind.Warning;
            AllowSymbol = allowed ? "✔" : "✖";
            AllowTip = allowed ? "Allowed by the allow-list." : e.AllowListProblem!;
            AllowBrush = Res(allowed ? "Brush.Success.Fg" : "Brush.Red");
            TextBrush = Res(allowed ? "Brush.TextSoft" : "Brush.Red");
        }

        public string Scope { get; }
        public string Target { get; }
        public string Entry { get; }
        public string Value { get; }
        public string StateText { get; }
        public StatusKind StateKind { get; }
        public string AllowSymbol { get; }
        public string AllowTip { get; }
        public Brush AllowBrush { get; }
        public Brush TextBrush { get; }
    }
}
