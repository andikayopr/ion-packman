using System.Diagnostics;
using System.IO;
using IonPackMan.Machine;

namespace IonPackMan.Deployment;

/// <summary>Implementasi asli dengan System.Diagnostics.Process; pencarian proses memakai <see cref="MachineInspector"/> yang sama dengan Dashboard.</summary>
public sealed class ProcessControl(MachineInspector inspector) : IProcessControl
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(10);

    public IReadOnlyList<string> FindExecutablesUnder(string directory) =>
        Candidates(directory).Select(p => p.ExecutablePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public void StopAllUnder(string directory, TimeSpan timeout)
    {
        var grace = timeout < GracePeriod ? timeout : GracePeriod;
        foreach (var info in Candidates(directory))
        {
            Process process;
            try { process = Process.GetProcessById(info.Pid); }
            catch (ArgumentException) { continue; }   // sudah mati sejak dicari

            using (process)
            {
                try
                {
                    if (process.CloseMainWindow() && process.WaitForExit(grace))
                        continue;
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                    continue;   // sudah selesai sendiri
                }
                if (!process.WaitForExit(timeout))
                    throw new TimeoutException($"Program '{info.Name}' (PID {info.Pid}) belum berhenti setelah {timeout.TotalSeconds:0.#} detik (timeout).");
            }
        }
    }

    public void Start(string executablePath)
    {
        using var process = Process.Start(new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = true,
        });
    }

    private List<ProcessInfo> Candidates(string directory) =>
        inspector.GetProcessesUnder(directory).Where(p => p.Pid != Environment.ProcessId).ToList();
}
