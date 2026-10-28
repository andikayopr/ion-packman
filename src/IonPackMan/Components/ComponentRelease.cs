using System.IO;
using System.Text.RegularExpressions;
using IonPackMan.Ini;
using IonPackMan.Machine;

namespace IonPackMan.Components;

/// <summary>
/// Satu release component (<c>componentId@releaseVersion</c>): target direktori, nama service, dan referensi build.
/// Disimpan sebagai Components\&lt;id&gt;\&lt;id&gt;-&lt;version&gt;.ini (PRD R4, §7.2).
/// Metadata build yang diimpor ada di local cache (Builds\&lt;id&gt;@&lt;version&gt;\build.ini), bukan di file ini,
/// karena folder rilis boleh berada di USB yang hanya-baca.
/// </summary>
public sealed record ComponentRelease
{
    // Id dan versi dipakai membentuk nama file/folder, jadi dibatasi ketat (mencegah "..", "\" dan "@").
    private static readonly Regex IdPattern = new(@"^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex VersionPattern = new(@"^[0-9A-Za-z][0-9A-Za-z._+-]{0,63}$", RegexOptions.Compiled);

    public const string InPlaceMode = "in-place";

    public required string Id { get; init; }
    public string DisplayName { get; init; } = "";
    public required string ReleaseVersion { get; init; }
    public string BuildReference { get; init; } = "";
    public string Mode { get; init; } = InPlaceMode;
    public required string TargetDirectory { get; init; }
    /// <summary>Kosong bila component tidak berjalan sebagai Windows service.</summary>
    public string ServiceName { get; init; } = "";

    public static bool IsValidId(string id) => IdPattern.IsMatch(id);
    public static bool IsValidVersion(string version) => VersionPattern.IsMatch(version);

    /// <summary>
    /// Aturan isi (R4). Dengan <paramref name="allowList"/>, target harus di dalam base directory
    /// dan service harus terdaftar; tanpanya hanya aturan bentuk yang diperiksa.
    /// </summary>
    public List<string> Validate(AllowList? allowList = null)
    {
        var problems = new List<string>();

        if (!IsValidId(Id))
            problems.Add($"Component Id '{Id}' must be lowercase letters, digits and '-' (e.g. non-ndc-parser).");
        if (!IsValidVersion(ReleaseVersion))
            problems.Add($"Release version '{ReleaseVersion}' must start with a letter or digit and use only letters, digits, '.', '_', '+', '-' (e.g. 2.1.0).");
        if (Mode != InPlaceMode)
            problems.Add($"Installation Mode '{Mode}' is not supported (only '{InPlaceMode}').");

        if (string.IsNullOrWhiteSpace(TargetDirectory))
            problems.Add("Target directory is required.");
        else if (!Path.IsPathFullyQualified(TargetDirectory) || TargetDirectory.StartsWith(@"\\", StringComparison.Ordinal))
            problems.Add($"Target directory '{TargetDirectory}' must be a full local path such as D:\\APCONTAINER\\WIND.");
        else if (allowList is not null && !allowList.IsUnderBaseDirectory(TargetDirectory))
            problems.Add($"Target directory '{TargetDirectory}' is not inside an allowed base directory.");

        if (allowList is not null && ServiceName.Length > 0 && !allowList.IsServiceAllowed(ServiceName))
            problems.Add($"Service '{ServiceName}' is not in the allow-list.");

        return problems;
    }

    /// <summary>
    /// Membaca file INI component. Mengembalikan release hanya bila struktur dan isinya valid;
    /// selain itu semua masalah dikembalikan sekaligus.
    /// </summary>
    public static (ComponentRelease? Release, List<string> Errors) LoadFile(string path)
    {
        var (ini, errors) = IniSchema.TryLoad(path);
        if (ini is null)
            return (null, errors);

        errors.AddRange(IniSchema.CheckMeta(ini));
        errors.AddRange(ini.CheckSections("Meta", "Component", "Installation", "WindowsService"));
        errors.AddRange(ini.CheckKeys("Component", required: ["Id", "ReleaseVersion"], optional: ["DisplayName", "BuildReference"]));
        errors.AddRange(ini.CheckKeys("Installation", required: ["TargetDirectory"], optional: ["Mode"]));
        if (ini.HasSection("WindowsService"))
            errors.AddRange(ini.CheckKeys("WindowsService", required: [], optional: ["ServiceName"]));
        if (errors.Count > 0)
            return (null, errors.Select(e => $"{path}: {e}").ToList());

        var release = new ComponentRelease
        {
            Id = ini.Get("Component", "Id")!,
            DisplayName = ini.Get("Component", "DisplayName") ?? "",
            ReleaseVersion = ini.Get("Component", "ReleaseVersion")!,
            BuildReference = ini.Get("Component", "BuildReference") ?? "",
            Mode = ini.Get("Installation", "Mode") ?? InPlaceMode,
            TargetDirectory = ini.Get("Installation", "TargetDirectory")!,
            ServiceName = ini.Get("WindowsService", "ServiceName") ?? "",
        };

        var problems = release.Validate();
        return problems.Count > 0
            ? (null, problems.Select(e => $"{path}: {e}").ToList())
            : (release, []);
    }

    /// <summary>Isi file INI (juga dipakai sebagai pratinjau). Melempar ArgumentException bila ada nilai yang tidak bisa disimpan.</summary>
    public IniFile ToIni()
    {
        var ini = new IniFile();
        ini.Set("Meta", "SchemaVersion", IniSchema.CurrentVersion);
        ini.Set("Component", "Id", Id);
        ini.Set("Component", "DisplayName", DisplayName);
        ini.Set("Component", "ReleaseVersion", ReleaseVersion);
        ini.Set("Component", "BuildReference", BuildReference);
        ini.Set("Installation", "Mode", Mode);
        ini.Set("Installation", "TargetDirectory", TargetDirectory);
        ini.Set("WindowsService", "ServiceName", ServiceName);
        return ini;
    }
}
