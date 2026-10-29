namespace IonPackMan.Screens;

/// <summary>
/// Layar dengan dua tampilan: daftar dan form (Components, Profiles). MainWindow memanggil <see cref="ShowList"/> saat menu
/// layar yang sedang tampil diklik lagi, sehingga dari form selalu ada jalan kembali yang jelas selain tombol Back.
/// </summary>
public interface IListDetailScreen
{
    void ShowList();
}
