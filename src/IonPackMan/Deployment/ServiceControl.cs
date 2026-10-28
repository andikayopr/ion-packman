using System.ComponentModel;
using System.ServiceProcess;

namespace IonPackMan.Deployment;

/// <summary>
/// Implementasi asli dengan ServiceController. Hanya Start/Stop service yang diminta; tidak mengubah startup type,
/// tidak membunuh proses. Semua penantian memakai WaitForStatus(status, timeout) sehingga tidak bisa hang.
/// </summary>
public sealed class ServiceControl : IServiceControl
{
    private const int ErrorInvalidName = 1057;
    private const int ErrorServiceDoesNotExist = 1060;

    public ServiceState GetState(string serviceName)
    {
        RequireName(serviceName);
        try
        {
            using var sc = new ServiceController(serviceName);
            return Map(sc.Status);
        }
        catch (InvalidOperationException ex) when (IsMissing(ex))
        {
            return ServiceState.NotFound;
        }
    }

    public void Stop(string serviceName, TimeSpan timeout)
    {
        using var sc = Open(serviceName);
        if (sc.Status == ServiceControllerStatus.Stopped)
            return;
        if (sc.Status != ServiceControllerStatus.StopPending)
            sc.Stop();
        WaitFor(sc, ServiceControllerStatus.Stopped, serviceName, timeout, "berhenti");
    }

    public void StartAndWaitRunning(string serviceName, TimeSpan timeout)
    {
        using var sc = Open(serviceName);
        if (sc.Status == ServiceControllerStatus.Running)
            return;
        if (sc.Status != ServiceControllerStatus.StartPending)
            sc.Start();
        WaitFor(sc, ServiceControllerStatus.Running, serviceName, timeout, "berjalan");
    }

    private static ServiceController Open(string serviceName)
    {
        RequireName(serviceName);
        var sc = new ServiceController(serviceName);
        try
        {
            _ = sc.Status; // memicu pembacaan; melempar bila tidak ada
            return sc;
        }
        catch (InvalidOperationException ex) when (IsMissing(ex))
        {
            sc.Dispose();
            throw new InvalidOperationException($"Service '{serviceName}' tidak ditemukan di mesin ini.", ex);
        }
        catch
        {
            sc.Dispose();
            throw;
        }
    }

    private static void WaitFor(ServiceController sc, ServiceControllerStatus target, string name, TimeSpan timeout, string what)
    {
        try
        {
            sc.WaitForStatus(target, timeout);
        }
        catch (System.ServiceProcess.TimeoutException ex)
        {
            throw new System.TimeoutException($"Service '{name}' belum {what} setelah {timeout.TotalSeconds:0.#} detik (timeout).", ex);
        }
    }

    private static void RequireName(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Nama service tidak boleh kosong.", nameof(serviceName));
    }

    // Tidak ada / nama tidak valid (mis. mengandung '\' atau '/'). Error lain (akses ditolak) tetap dilempar.
    private static bool IsMissing(InvalidOperationException ex) =>
        ex.InnerException is Win32Exception w && (w.NativeErrorCode == ErrorServiceDoesNotExist || w.NativeErrorCode == ErrorInvalidName);

    private static ServiceState Map(ServiceControllerStatus s) => s switch
    {
        ServiceControllerStatus.Stopped => ServiceState.Stopped,
        ServiceControllerStatus.Running => ServiceState.Running,
        ServiceControllerStatus.StartPending => ServiceState.StartPending,
        ServiceControllerStatus.StopPending => ServiceState.StopPending,
        ServiceControllerStatus.Paused => ServiceState.Paused,
        _ => ServiceState.Other,
    };
}
