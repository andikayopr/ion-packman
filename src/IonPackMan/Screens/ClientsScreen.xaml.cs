using System.IO;
using System.Windows;
using System.Windows.Controls;
using IonPackMan.Controls;
using IonPackMan.Machine;

namespace IonPackMan.Screens;

/// <summary>
/// Layar Clients (ADR-006): Client mesin ini (machine.ini) dan daftar Client beserta component yang memakai branch-nya
/// (Config\clients.ini). Aturan validasi dan penulisan ada di <see cref="ClientConfig"/>; layar hanya mengisi form dan
/// menukar isi ClientConfig bersama setelah file tersimpan. Perubahan hanya di mesin Development.
/// </summary>
public partial class ClientsScreen : UserControl, IScreen
{
    private const string NoClient = "(none) default";

    private readonly AppServices _services;
    private readonly MachineConfig _machine;
    private readonly ClientConfig _clients;
    private readonly Func<string, bool> _confirmDelete;

    private bool _updating = true;
    private bool _isNew;
    private List<string> _catalogIds = [];

    public event Action<string>? Message;

    /// <param name="confirmDelete">Meminta konfirmasi hapus; bawaan MessageBox (diganti di tes).</param>
    public ClientsScreen(AppServices services, Func<string, bool>? confirmDelete = null)
    {
        _services = services;
        _machine = services.Machine ?? throw new ArgumentException("machine.ini is not valid.", nameof(services));
        _clients = services.Clients;
        _confirmDelete = confirmDelete ?? (text => MessageBox.Show(text, "Delete client", MessageBoxButton.OKCancel,
            MessageBoxImage.Warning, MessageBoxResult.Cancel) == MessageBoxResult.OK);

        InitializeComponent();
        Refresh();
    }

    /// <summary>Client yang sedang dipilih di daftar; null saat membuat Client baru atau daftar kosong.</summary>
    public string? SelectedClient => _isNew ? null : ClientPick.SelectedItem as string;

    private bool CanEdit => _machine.IsDevelopment;

    public void Refresh()
    {
        _updating = true;
        try
        {
            ShowProblems();
            ShowMachine();
            string? keep = SelectedClient;
            var ids = _clients.Clients.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();
            ClientPick.ItemsSource = ids;
            if (!_isNew)
                ClientPick.SelectedItem = keep is not null && ids.Contains(keep, StringComparer.OrdinalIgnoreCase) ? _clients.ExactId(keep)
                    : ids.FirstOrDefault();
            ShowForm();
        }
        finally { _updating = false; }
    }

    // ---------- This machine ----------

    private void ShowProblems()
    {
        var problems = _services.ClientErrors;
        ProblemNotice.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ProblemNotice.Content = problems.Count == 0 ? null
            : new TextBlock { Text = string.Join(Environment.NewLine, problems) + Environment.NewLine + "Fix it here, or edit the file by hand.", TextWrapping = TextWrapping.Wrap };
    }

    private void ShowMachine()
    {
        MachineNameText.Text = _machine.MachineName;
        EnvironmentText.Text = _machine.Environment;
        ProfileText.Text = _machine.Profile.Length > 0 ? _machine.Profile : "not set";
        CacheText.Text = _machine.LocalCacheDirectory;
        CacheText.ToolTip = _machine.LocalCacheDirectory;

        // Client yang belum ada di clients.ini tetap ditampilkan (bukan dibuang) supaya terlihat apa yang salah.
        var items = new List<string> { NoClient };
        items.AddRange(_clients.Clients.Keys.Order(StringComparer.OrdinalIgnoreCase));
        if (_machine.Client.Length > 0 && !_clients.IsKnown(_machine.Client))
            items.Add(_machine.Client);
        MachineClientBox.ItemsSource = items;
        MachineClientBox.SelectedItem = _machine.Client.Length == 0 ? NoClient
            : _clients.IsKnown(_machine.Client) ? _clients.ExactId(_machine.Client) : _machine.Client;
        MachineClientBox.IsEnabled = CanEdit;

        MachineClientHint.Text = !CanEdit ? $"Only Development machines can change the Client (this machine is '{_machine.Environment}'). It is saved in machine.ini."
            : _machine.Client.Length == 0 ? "No Client: this machine deploys Default builds. Pick a Client to deploy its branch builds. Saved in machine.ini."
            : $"This machine deploys {_machine.Client} builds for: {(_clients.OverriddenBy(_machine.Client) is { Count: > 0 } list ? string.Join(", ", list) : "no component")}. Everything else uses the Default build.";
    }

    private void OnMachineClientChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || MachineClientBox.SelectedItem is not string chosen)
            return;
        string client = chosen == NoClient ? "" : chosen;
        if (client.Equals(_machine.Client, StringComparison.OrdinalIgnoreCase))
            return;

        if (_machine.TrySetClient(client) is { } problem)
            Message?.Invoke(problem);
        else
            Message?.Invoke(client.Length == 0 ? "Client cleared: this machine now deploys Default builds." : $"Client set to {client}.");
        Refresh();
    }

    // ---------- Clients (clients.ini) ----------

    private void ShowForm()
    {
        var (entries, _) = _services.Components.LoadCatalog();
        _catalogIds = entries.Select(e => e.Id).ToList();

        string? id = SelectedClient;
        var overridden = id is null ? [] : _clients.OverriddenBy(id);

        IdBox.Text = id ?? "";
        IdBox.IsReadOnly = !_isNew;
        NameBox.Text = id is not null && _clients.Clients.TryGetValue(id, out var name) ? name : "";
        ErrorNotice.Visibility = Visibility.Collapsed;

        ComponentList.Children.Clear();
        foreach (var entry in entries)
            ComponentList.Children.Add(new CheckBox
            {
                Content = $"{entry.DisplayName} ({entry.Id})",
                Tag = entry.Id,
                IsChecked = overridden.Contains(entry.Id),
                IsEnabled = CanEdit,
                Margin = new Thickness(0, 3, 0, 0),
            });

        bool hasForm = _isNew || id is not null;
        Form.Visibility = hasForm ? Visibility.Visible : Visibility.Collapsed;
        NewButton.IsEnabled = CanEdit;
        IdBox.IsEnabled = CanEdit;
        NameBox.IsEnabled = CanEdit;
        SaveButton.IsEnabled = CanEdit;
        DeleteButton.IsEnabled = CanEdit && !_isNew;
        EditNote.Visibility = CanEdit ? Visibility.Collapsed : Visibility.Visible;
        EditNote.Text = $"Clients can only be edited on Development machines (this machine is '{_machine.Environment}').";
    }

    private void OnClientPicked(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || ClientPick.SelectedItem is null)
            return;
        _isNew = false;
        _updating = true;
        try { ShowForm(); }
        finally { _updating = false; }
    }

    private void OnNew(object sender, RoutedEventArgs e) => NewClient();

    /// <summary>Mengosongkan form untuk Client baru.</summary>
    public void NewClient()
    {
        _isNew = true;
        _updating = true;
        try
        {
            ClientPick.SelectedItem = null;
            ShowForm();
        }
        finally { _updating = false; }
        IdBox.Focus();
    }

    /// <summary>Memilih Client di daftar dan mengisi form-nya.</summary>
    public void Select(string clientId)
    {
        _isNew = false;
        _updating = true;
        try
        {
            ClientPick.SelectedItem = _clients.ExactId(clientId);
            ShowForm();
        }
        finally { _updating = false; }
    }

    private void OnSave(object sender, RoutedEventArgs e) => Save();

    /// <summary>Menyimpan isi form ke clients.ini; tampil error di form bila ditolak.</summary>
    public bool Save()
    {
        if (!CanEdit)
            return false;

        string id = IdBox.Text.Trim();
        string name = NameBox.Text.Trim();
        var components = ComponentList.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();

        if (_isNew)
        {
            if (!ClientConfig.IsValidId(id))
                return Fail($"'{id}' is not a valid Client Id (letters and digits, optionally separated by '-').");
            if (_clients.IsKnown(id))
                return Fail($"Client '{id}' already exists.");
        }
        else if (SelectedClient is null)
            return false;
        else
            id = SelectedClient;

        return Commit(_clients.With(id, name, components), $"Saved client {id}.", id);
    }

    private void OnDelete(object sender, RoutedEventArgs e) => Delete();

    /// <summary>Menghapus Client terpilih. Ditolak bila Client itu sedang dipakai di machine.ini.</summary>
    public bool Delete()
    {
        if (!CanEdit || SelectedClient is not { } id)
            return false;
        if (_machine.Client.Equals(id, StringComparison.OrdinalIgnoreCase))
            return Fail($"{id} is the Client of this machine. Choose another Client (or none) for this machine first.");
        if (!_confirmDelete($"Delete client {id}? Its builds stay in the cache but can no longer be imported or deployed."))
            return false;

        _isNew = false;
        return Commit(_clients.Without(id), $"Deleted client {id}.", null);
    }

    // Menulis lewat ClientConfig.Save (aturan yang sama dengan Load); baru setelah file tersimpan, isi bersama ditukar.
    private bool Commit(ClientConfig updated, string message, string? select)
    {
        var errors = updated.Save(Paths.ClientsIni(_services.ReleaseDirectory), _catalogIds);
        if (errors.Count > 0)
            return Fail(string.Join(Environment.NewLine, errors));

        _clients.Replace(updated);
        _services.ClientsSaved();
        _isNew = false;
        Refresh();
        if (select is not null)
            Select(select);
        Message?.Invoke(message);
        return true;
    }

    private bool Fail(string text)
    {
        ErrorNotice.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        ErrorNotice.Visibility = Visibility.Visible;
        return false;
    }
}
