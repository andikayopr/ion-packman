using System.IO;
using IonPackMan.Deployment;

namespace IonPackMan.Tests;

/// <summary>Registry palsu di memori untuk tes: bisa diprogram gagal, mencatat operasi, dan punya Snapshot().</summary>
public sealed class FakeRegistry : IRegistryAccess
{
    private readonly Dictionary<(string Key, string Name), RegistryValue> _values = new();
    private readonly HashSet<(string Key, string Name)> _failures = new();

    public List<string> Operations { get; } = new();

    /// <summary>Write/Delete pada key+value ini akan melempar IOException (Read tidak terpengaruh).</summary>
    public void FailOn(string key, string valueName) => _failures.Add(Norm(key, valueName));

    public void ClearFailures() => _failures.Clear();

    /// <summary>Mengisi nilai awal tanpa tercatat sebagai operasi.</summary>
    public void Seed(string key, string valueName, RegistryValueType type, string value) =>
        _values[Norm(key, valueName)] = new RegistryValue(type, value);

    public IReadOnlyDictionary<string, RegistryValue> Snapshot() =>
        _values.ToDictionary(kv => $"{kv.Key.Key}|{kv.Key.Name}", kv => kv.Value);

    public RegistryValue? Read(string key, string valueName)
    {
        Operations.Add($"Read {key}|{valueName}");
        return _values.TryGetValue(Norm(key, valueName), out var v) ? v : null;
    }

    public void Write(string key, string valueName, RegistryValueType type, string value)
    {
        Operations.Add($"Write {key}|{valueName}={type}:{value}");
        ThrowIfFailing(key, valueName);
        _values[Norm(key, valueName)] = new RegistryValue(type, value);
    }

    public void Delete(string key, string valueName)
    {
        Operations.Add($"Delete {key}|{valueName}");
        ThrowIfFailing(key, valueName);
        _values.Remove(Norm(key, valueName));
    }

    private void ThrowIfFailing(string key, string valueName)
    {
        if (_failures.Contains(Norm(key, valueName)))
            throw new IOException($"Fake failure on '{key}' value '{valueName}'.");
    }

    private static (string, string) Norm(string key, string name) =>
        (key.Trim('\\').ToUpperInvariant(), name.ToUpperInvariant());
}
