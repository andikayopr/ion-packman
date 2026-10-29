namespace IonPackMan.Deployment;

/// <summary>
/// Menghentikan dan menjalankan lagi program biasa (bukan Windows service) yang berjalan dari folder target component.
/// Punya dua implementasi (ProcessControl asli, FakeProcessControl di tes), sama seperti <see cref="IServiceControl"/>.
/// Kontrak: hanya menyentuh proses yang file exe-nya berada di folder itu, tidak pernah menyentuh proses PackMan sendiri,
/// penantian selalu berbatas timeout.
/// </summary>
public interface IProcessControl
{
    /// <summary>Path exe (tanpa duplikat) dari proses yang berjalan dari <paramref name="directory"/> atau subfoldernya.</summary>
    IReadOnlyList<string> FindExecutablesUnder(string directory);

    /// <summary>Tutup semua proses itu: minta menutup jendela dulu, lalu paksa berhenti bila belum selesai. Tunggu sampai mati.</summary>
    /// <exception cref="TimeoutException">Ada proses yang belum mati dalam <paramref name="timeout"/>.</exception>
    void StopAllUnder(string directory, TimeSpan timeout);

    /// <summary>Jalankan exe dengan folder exe itu sebagai working directory. Tidak menunggu program selesai.</summary>
    void Start(string executablePath);
}
