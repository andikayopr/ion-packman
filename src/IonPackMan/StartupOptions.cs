using System.IO;

namespace IonPackMan;

/// <summary>
/// Opsi baris perintah: <c>--release &lt;folder&gt;</c> dan <c>--machine &lt;file machine.ini&gt;</c>.
/// Dipakai uji manual di VM (sandbox) tanpa menyentuh D:\ION-PackMan. Tanpa opsi, lokasi bawaan dari <see cref="Paths"/>.
/// </summary>
public sealed record StartupOptions(string ReleaseDirectory, string MachineIni)
{
    public static (StartupOptions? Options, string? Error) Parse(string[] args)
    {
        string release = Paths.ReleaseDirectory;
        string machine = Paths.MachineIni;

        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i];
            if (name is not ("--release" or "--machine"))
                return (null, $"Unknown argument '{name}'. Supported: --release <folder> --machine <machine.ini>.");
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return (null, $"Argument '{name}' needs a value.");

            string value = args[++i];
            if (!Path.IsPathFullyQualified(value))
                return (null, $"Argument '{name}' must be a full path (got '{value}').");

            if (name == "--release") release = value; else machine = value;
        }
        return (new StartupOptions(release, machine), null);
    }
}
