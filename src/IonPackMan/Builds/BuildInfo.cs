namespace IonPackMan.Builds;

/// <summary>Metadata satu build yang sudah diimpor (isi Builds\&lt;id&gt;@&lt;versi&gt;\build.ini).</summary>
public sealed record BuildInfo(
    string ComponentId,
    string ReleaseVersion,
    string SourceFolderName,
    int FileCount,
    long TotalBytes,
    DateTimeOffset ImportedAt);

/// <summary>Satu baris untuk tabel katalog: "nama folder · N files". SourceFolderName null bila build.ini tidak terbaca.</summary>
public sealed record BuildSummary(string ComponentId, string ReleaseVersion, string? SourceFolderName, int FileCount)
{
    public string Display => SourceFolderName is null ? "unreadable build" : $"{SourceFolderName} · {FileCount} files";
}
