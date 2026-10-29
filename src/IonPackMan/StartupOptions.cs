using System.IO;

namespace IonPackMan;

/// <summary>
/// Opsi baris perintah: <c>--release &lt;folder&gt;</c>, <c>--machine &lt;file machine.ini&gt;</c>, dan <c>--window &lt;lebar&gt;x&lt;tinggi&gt;</c>.
/// Dipakai uji manual di VM (sandbox) tanpa menyentuh D:\ION-PackMan. Tanpa opsi, lokasi bawaan dari <see cref="Paths"/>.
/// <c>--window 1024x768</c> menjalankan jendela dengan ukuran itu (tidak maximize) untuk menguji layout layar kecil,
/// tanpa mengubah resolusi layar. Hanya ukuran jendela; tidak disimpan.
/// </summary>
public sealed record StartupOptions(string ReleaseDirectory, string MachineIni, (double Width, double Height)? WindowSize = null)
{
    private const int MinSide = 320, MaxSide = 10000;

    public static (StartupOptions? Options, string? Error) Parse(string[] args)
    {
        string release = Paths.ReleaseDirectory;
        string machine = Paths.MachineIni;
        (double, double)? window = null;

        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            if (name is not ("--release" or "--machine" or "--window"))
                return (null, $"Unknown argument '{name}'. Supported: --release <folder> --machine <machine.ini> --window <width>x<height>.");
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return (null, $"Argument '{name}' needs a value.");

            string value = args[++i];
            if (name == "--window")
            {
                var parts = value.Split('x', 'X');
                if (parts.Length != 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h)
                    || w is < MinSide or > MaxSide || h is < MinSide or > MaxSide)
                    return (null, $"Argument '--window' must look like 1024x768 (each side {MinSide}-{MaxSide}); got '{value}'.");
                window = (w, h);
                continue;
            }
            if (!Path.IsPathFullyQualified(value))
                return (null, $"Argument '{name}' must be a full path (got '{value}').");

            if (name == "--release") release = value; else machine = value;
        }
        return (new StartupOptions(release, machine, window), null);
    }
}
