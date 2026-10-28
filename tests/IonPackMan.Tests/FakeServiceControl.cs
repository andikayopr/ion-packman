using IonPackMan.Deployment;

namespace IonPackMan.Tests;

/// <summary>Service palsu untuk tes: kamus nama->state, mencatat panggilan, bisa diprogram gagal.</summary>
public sealed class FakeServiceControl : IServiceControl
{
    private readonly Dictionary<string, ServiceState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Op, string Name), Exception> _failures = new();
    private readonly HashSet<string> _neverStops = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Urutan panggilan, format "GetState:Nama", "Stop:Nama", "Start:Nama".</summary>
    public List<string> Calls { get; } = new();

    public FakeServiceControl Add(string name, ServiceState state)
    {
        _states[name] = state;
        return this;
    }

    /// <summary>operation: "GetState", "Stop", atau "Start".</summary>
    public FakeServiceControl FailOn(string operation, string serviceName, Exception exception)
    {
        _failures[(operation, serviceName.ToLowerInvariant())] = exception;
        return this;
    }

    /// <summary>Stop pada service ini melempar TimeoutException dan state tetap tidak berubah.</summary>
    public FakeServiceControl NeverStops(string serviceName)
    {
        _neverStops.Add(serviceName);
        return this;
    }

    public ServiceState GetState(string serviceName)
    {
        Record("GetState", serviceName);
        ThrowIfProgrammed("GetState", serviceName);
        return _states.TryGetValue(serviceName, out var s) ? s : ServiceState.NotFound;
    }

    public void Stop(string serviceName, TimeSpan timeout)
    {
        Record("Stop", serviceName);
        ThrowIfProgrammed("Stop", serviceName);
        RequireExists(serviceName);
        if (_states[serviceName] == ServiceState.Stopped)
            return;
        if (_neverStops.Contains(serviceName))
            throw new TimeoutException($"Service '{serviceName}' belum berhenti setelah {timeout.TotalSeconds:0.#} detik (timeout).");
        _states[serviceName] = ServiceState.Stopped;
    }

    public void StartAndWaitRunning(string serviceName, TimeSpan timeout)
    {
        Record("Start", serviceName);
        ThrowIfProgrammed("Start", serviceName);
        RequireExists(serviceName);
        _states[serviceName] = ServiceState.Running;
    }

    private void Record(string op, string name) => Calls.Add($"{op}:{name}");

    private void ThrowIfProgrammed(string op, string name)
    {
        if (_failures.TryGetValue((op, name.ToLowerInvariant()), out var ex))
            throw ex;
    }

    private void RequireExists(string name)
    {
        if (!_states.ContainsKey(name))
            throw new InvalidOperationException($"Service '{name}' tidak ditemukan di mesin ini.");
    }
}
