using System.Windows;
using System.Windows.Controls;
using IonPackMan.Components;

namespace IonPackMan.Screens;

/// <summary>Dialog "New component": id dan nama tampilan, disimpan ke catalog.ini lewat <see cref="ComponentStore.AddToCatalog"/>. Pesan validasi tampil di dalam dialog.</summary>
public partial class NewComponentDialog : Window
{
    private readonly ComponentStore _store;

    public NewComponentDialog(ComponentStore store)
    {
        InitializeComponent();
        _store = store;
    }

    /// <summary>Id component yang berhasil ditambahkan.</summary>
    public string ComponentId { get; private set; } = "";

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var problems = _store.AddToCatalog(IdBox.Text, NameBox.Text);
        if (problems.Count > 0)
        {
            ErrorNotice.Content = new TextBlock { Text = string.Join(Environment.NewLine, problems), TextWrapping = TextWrapping.Wrap };
            ErrorNotice.Visibility = Visibility.Visible;
            return;
        }
        ComponentId = IdBox.Text.Trim();
        DialogResult = true;
    }
}
