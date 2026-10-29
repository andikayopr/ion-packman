namespace IonPackMan.Builds;

/// <summary>
/// Metadata satu build yang sudah diimpor (isi Builds\&lt;id&gt;@&lt;versi&gt;[@&lt;CLIENT&gt;]\build.ini).
/// <paramref name="Client"/> kosong = build default; <paramref name="GitBranch"/>/<paramref name="GitCommit"/> kosong bila tidak terbaca.
/// </summary>
public sealed record BuildInfo(
    string ComponentId,
    string ReleaseVersion,
    string SourceFolderName,
    int FileCount,
    long TotalBytes,
    DateTimeOffset ImportedAt,
    string Client = "",
    string GitBranch = "",
    string GitCommit = "");

/// <summary>Satu baris untuk tabel katalog: "nama folder · N files · Client". SourceFolderName null bila build.ini tidak terbaca.</summary>
public sealed record BuildSummary(string ComponentId, string ReleaseVersion, string? SourceFolderName, int FileCount, string Client = "")
{
    public string Display => SourceFolderName is null ? "unreadable build"
        : $"{SourceFolderName} · {FileCount} files" + (Client.Length > 0 ? $" · {Client}" : "");
}
