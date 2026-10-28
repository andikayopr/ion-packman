namespace IonPackMan.Screens;

/// <summary>
/// Kesepakatan semua layar menu: MainWindow memanggil <see cref="Refresh"/> setiap layar dibuka
/// dan menampilkan <see cref="Message"/> sebagai toast.
/// </summary>
public interface IScreen
{
    event Action<string>? Message;
    void Refresh();
}
