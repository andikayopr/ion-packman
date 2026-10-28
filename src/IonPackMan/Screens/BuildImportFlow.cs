using System.Windows;
using System.Windows.Input;
using IonPackMan.Builds;
using IonPackMan.Components;
using Microsoft.Win32;

namespace IonPackMan.Screens;

/// <summary>
/// Alur "Import local build…" dari layar Components: pilih folder, konfirmasi bila menggantikan build lama,
/// lalu jalankan <see cref="BuildImporter"/>. Hanya dialog dan pesan; aturan impor ada di BuildImporter.
/// </summary>
public static class BuildImportFlow
{
    /// <returns>true bila build berhasil diimpor.</returns>
    public static bool Run(BuildImporter importer, ComponentRelease release, Action<string> notify)
    {
        var dialog = new OpenFolderDialog { Title = $"Select the build folder for {release.Id} {release.ReleaseVersion}" };
        if (dialog.ShowDialog() != true)
            return false;

        if (importer.Exists(release.Id, release.ReleaseVersion))
        {
            var answer = MessageBox.Show(
                $"A build for {release.Id} {release.ReleaseVersion} is already imported.\nReplace it with the files from:\n{dialog.FolderName}?",
                "Replace imported build", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
            if (answer != MessageBoxResult.OK)
                return false;
        }

        var previous = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        (BuildInfo? Build, List<string> Errors) result;
        try
        {
            result = importer.Import(release.Id, release.ReleaseVersion, dialog.FolderName);
        }
        finally
        {
            Mouse.OverrideCursor = previous;
        }

        if (result.Build is null)
        {
            MessageBox.Show(string.Join(Environment.NewLine, result.Errors), "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        notify($"Imported {result.Build.FileCount} files for {release.Id} {release.ReleaseVersion}.");
        return true;
    }
}
