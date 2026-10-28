namespace IonPackMan.Deployment;

public enum ServiceState { NotFound, Stopped, Running, StartPending, StopPending, Paused, Other }

/// <summary>
/// Start/stop satu Windows service dan menunggu hasilnya (R10/R11, System Design §3.4).
/// Satu dari dua interface yang diizinkan: punya dua implementasi (ServiceControl asli, FakeServiceControl di tes).
/// Kontrak: tidak pernah mengubah startup type, tidak pernah membunuh proses, penantian selalu berbatas timeout.
/// </summary>
public interface IServiceControl
{
    /// <summary>Status service; <see cref="ServiceState.NotFound"/> bila tidak terpasang atau nama tidak valid.</summary>
    /// <exception cref="ArgumentException">Nama null/kosong/spasi.</exception>
    ServiceState GetState(string serviceName);

    /// <summary>Stop lalu tunggu Stopped. Tidak error bila sudah Stopped.</summary>
    /// <exception cref="InvalidOperationException">Service tidak ada.</exception>
    /// <exception cref="TimeoutException">Belum Stopped dalam <paramref name="timeout"/>.</exception>
    void Stop(string serviceName, TimeSpan timeout);

    /// <summary>Start lalu tunggu Running. Tidak error bila sudah Running.</summary>
    /// <exception cref="InvalidOperationException">Service tidak ada.</exception>
    /// <exception cref="TimeoutException">Belum Running dalam <paramref name="timeout"/>.</exception>
    void StartAndWaitRunning(string serviceName, TimeSpan timeout);
}
