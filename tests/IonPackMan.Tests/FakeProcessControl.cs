using IonPackMan.Deployment;

namespace IonPackMan.Tests;

/// <summary>Program palsu untuk tes: daftar exe yang "berjalan", mencatat panggilan, bisa diprogram gagal.</summary>
public sealed class FakeProcessControl : IProcessControl
{
    private readonly List<string> _running = [];
    private readonly Dictionary<string, Exception> _failures = new();

    /// <summary>Urutan panggilan, format "Stop:folder" dan "Start:exe".</summary>
    public List<string> Calls { get; } = [];

    public FakeProcessControl Run(string exePath)
    {
        _running.Add(exePath);
        return this;
    }

    /// <summary>operation: "Stop" atau "Start".</summary>
    public FakeProcessControl FailOn(string operation, Exception exception)
    {
        _failures[operation] = exception;
        return this;
    }

    public IReadOnlyList<string> FindExecutablesUnder(string directory) =>
        _running.Where(p => IsUnder(p, directory)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public void StopAllUnder(string directory, TimeSpan timeout)
    {
        Calls.Add($"Stop:{directory}");
        ThrowIfProgrammed("Stop");
        _running.RemoveAll(p => IsUnder(p, directory));
    }

    public void Start(string executablePath)
    {
        Calls.Add($"Start:{executablePath}");
        ThrowIfProgrammed("Start");
        _running.Add(executablePath);
    }

    private void ThrowIfProgrammed(string op)
    {
        if (_failures.TryGetValue(op, out var ex))
            throw ex;
    }

    private static bool IsUnder(string path, string directory) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
