using System.IO;
using System.Text;

namespace IonPackMan.Builds;

/// <summary>
/// Branch dan commit git dari folder sumber build (ADR-006), dibaca langsung dari folder .git tanpa memanggil git.
/// Hanya informasi: folder build biasanya ada di dalam repository (mis. bin\Debug), jadi .git dicari ke folder induk.
/// Apa pun yang tidak terbaca menghasilkan string kosong, tidak pernah error.
/// </summary>
public static class GitInfo
{
    public static (string Branch, string Commit) Read(string folder)
    {
        try
        {
            string? gitDir = FindGitDir(folder);
            if (gitDir is null)
                return ("", "");

            string head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
            const string refPrefix = "ref: ";
            if (!head.StartsWith(refPrefix, StringComparison.Ordinal))
                return ("", Short(head));   // detached HEAD: berisi hash commit

            string reference = head[refPrefix.Length..].Trim();
            string branch = reference.StartsWith("refs/heads/", StringComparison.Ordinal) ? reference["refs/heads/".Length..] : reference;
            return (IniSafe(branch), Short(ResolveRef(gitDir, reference)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return ("", "");
        }
    }

    // ".git" bisa folder, atau file "gitdir: <path>" (worktree/submodule).
    private static string? FindGitDir(string folder)
    {
        for (var dir = new DirectoryInfo(folder); dir is not null; dir = dir.Parent)
        {
            string git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git))
                return git;
            if (File.Exists(git))
            {
                string text = File.ReadAllText(git).Trim();
                if (text.StartsWith("gitdir:", StringComparison.Ordinal))
                    return Path.GetFullPath(Path.Combine(dir.FullName, text["gitdir:".Length..].Trim()));
            }
        }
        return null;
    }

    private static string ResolveRef(string gitDir, string reference)
    {
        string loose = Path.Combine(gitDir, reference.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(loose))
            return File.ReadAllText(loose).Trim();

        // Ref yang sudah dipadatkan git ada di packed-refs: "<hash> <ref>".
        string packed = Path.Combine(gitDir, "packed-refs");
        if (File.Exists(packed))
            foreach (var line in File.ReadLines(packed, Encoding.UTF8))
                if (line.Length > 41 && line[40] == ' ' && line[41..].Trim() == reference)
                    return line[..40];
        return "";
    }

    private static string Short(string hash) => hash.Length >= 7 && hash.All(Uri.IsHexDigit) ? hash[..7] : "";

    // Nilai build.ini tidak boleh berisi " ;" (komentar inline).
    private static string IniSafe(string value) => value.Replace(" ;", " ,").Trim();
}
