namespace IonPackMan.Screens;

/// <summary>
/// Layar yang bisa sedang menjalankan sesuatu yang tidak boleh terputus (eksekusi di Developer Console).
/// MainWindow menolak ditutup selama <see cref="IsBusy"/> true, karena menutup aplikasi di tengah eksekusi
/// meninggalkan backup yang harus dipulihkan.
/// </summary>
public interface IBusyScreen
{
    bool IsBusy { get; }
}
