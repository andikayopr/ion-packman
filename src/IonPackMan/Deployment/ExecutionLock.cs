namespace IonPackMan.Deployment;

/// <summary>
/// Satu eksekusi pada satu waktu untuk seluruh aplikasi: Developer Console (satu component) dan pindah Profile sama-sama
/// mengubah mesin, jadi tidak boleh berjalan bersamaan. Tidak menunggu: yang kedua langsung ditolak.
/// </summary>
public sealed class ExecutionLock
{
    private int _held;

    public bool IsHeld => Volatile.Read(ref _held) == 1;

    /// <summary>True bila berhasil memegang; pemegang wajib memanggil <see cref="Exit"/>.</summary>
    public bool TryEnter() => Interlocked.CompareExchange(ref _held, 1, 0) == 0;

    public void Exit() => Volatile.Write(ref _held, 0);
}
