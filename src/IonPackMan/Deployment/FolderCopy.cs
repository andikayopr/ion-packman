using System.IO;

namespace IonPackMan.Deployment;

/// <summary>
/// Mengukur dan menyalin seluruh isi sebuah folder untuk snapshot sebelum patch. Link (junction/symlink) tidak diikuti,
/// supaya snapshot tidak menyalin isi folder lain. File dibaca dengan FileShare.ReadWrite agar file yang sedang dibuka
/// program (mis. log) tetap bisa disalin; file yang dikunci eksklusif membuat salinan gagal (IOException).
/// </summary>
public static class FolderCopy
{
    public static long SizeOf(string root)
    {
        if (!Directory.Exists(root))
            return 0;
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(root).Where(f => !IsLink(f)))
            total += new FileInfo(file).Length;
        foreach (var dir in Directory.EnumerateDirectories(root).Where(d => !IsLink(d)))
            total += SizeOf(dir);
        return total;
    }

    /// <summary>Menyalin isi <paramref name="source"/> (termasuk folder kosong) ke <paramref name="destination"/> yang belum ada.</summary>
    public static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source).Where(f => !IsLink(f)))
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            using var from = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var to = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            from.CopyTo(to);
            to.Flush();
            if (to.Length != from.Length)
                throw new IOException($"Snapshot copy of '{file}' is not the same size as the original (the file changed while it was copied).");
        }
        foreach (var dir in Directory.EnumerateDirectories(source).Where(d => !IsLink(d)))
            Copy(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    private static bool IsLink(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
}
