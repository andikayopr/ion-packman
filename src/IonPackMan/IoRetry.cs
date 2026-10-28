using System.IO;

namespace IonPackMan;

/// <summary>
/// Mengulang operasi file singkat bila ditolak sesaat. Di Windows, antivirus atau indexer sering memegang file/folder yang baru
/// ditulis beberapa puluh milidetik, sehingga rename (.tmp -> nama akhir) atau copy gagal dengan "Access denied" lalu berhasil
/// beberapa saat kemudian. Penolakan yang sungguhan tetap dilempar setelah percobaan habis (total jeda ±0,4 detik).
/// File/folder yang memang tidak ada tidak diulang.
/// </summary>
internal static class IoRetry
{
    public static void Run(Action action, int attempts = 5)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < attempts
                                       && ex is IOException or UnauthorizedAccessException
                                       && ex is not (FileNotFoundException or DirectoryNotFoundException))
            {
                Thread.Sleep(40 * attempt);
            }
        }
    }
}
