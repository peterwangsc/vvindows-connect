using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace VindOS;

sealed record GameEntry(string Id, string Name, string Exe, string ExeSha256, string Bridge, string BridgeSha256, string ProcessName, ushort[] RecenterScans);

sealed class Game : IDisposable
{
    public static readonly GameEntry[] Registry =
    [
        new("assetto", "Assetto Corsa",
            @"C:\Program Files (x86)\Steam\steamapps\common\assettocorsa\acs.exe", "0df569c840f8303f7018f7891085e3a4c22cf93fb19327c6a0b85325cea23fd1",
            @"C:\Program Files (x86)\Steam\steamapps\common\assettocorsa\system\x64\openvr_api.dll", "827ad85f3606a4dc4a8f5561a8ca69e4c6c1b5d2b9cd3315a461b9270b08242c",
            "acs", [0x1D, 0x39]),
    ];

    [DllImport("kernel32.dll", SetLastError = true)] static extern nint CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(nint job, int infoClass, ref JobLimits info, int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(nint hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    [StructLayout(LayoutKind.Sequential)]
    struct JobLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    public static IEnumerable<GameEntry> Installed() => Registry.Where(g => Matches(g) is null);

    public static string? Matches(GameEntry g)
    {
        if (!File.Exists(g.Exe)) return "not installed";
        if (Sha256(g.Exe) != g.ExeSha256) return "game binary differs from the verified build";
        if (!File.Exists(g.Bridge) || Sha256(g.Bridge) != g.BridgeSha256) return "OpenXR bridge missing or differs from the verified build";
        return null;
    }

    static string Sha256(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    readonly nint _job;
    readonly Process _process;
    public GameEntry Entry { get; }
    public event Action<int>? Exited;

    public Game(GameEntry entry)
    {
        Entry = entry;
        var reason = Matches(entry);
        if (reason is not null) throw new InvalidOperationException(reason);
        if (Process.GetProcessesByName(entry.ProcessName).Length > 0) throw new InvalidOperationException($"{entry.Name} is already running.");
        var dir = Path.GetDirectoryName(entry.Exe)!;
        var start = new ProcessStartInfo(entry.Exe) { WorkingDirectory = dir, UseShellExecute = false };
        start.Environment["XR_RUNTIME_JSON"] = StreamManager.RuntimeJson;
        start.Environment["PATH"] = Path.GetDirectoryName(entry.Bridge) + ";" + Environment.GetEnvironmentVariable("PATH");
        foreach (var name in new[] { "PYTHONHOME", "PYTHONPATH", "PYTHONOPTIMIZE" }) start.Environment.Remove(name);
        _job = CreateJobObjectW(0, null);
        var limits = new JobLimits { LimitFlags = 0x2000 };
        SetInformationJobObject(_job, 9, ref limits, Marshal.SizeOf<JobLimits>());
        _process = Process.Start(start)!;
        AssignProcessToJobObject(_job, _process.Handle);
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => { Log.Write($"game {entry.Id} exited {_process.ExitCode}"); Exited?.Invoke(_process.ExitCode); };
        Log.Write($"game {entry.Id} started pid {_process.Id}");
    }

    public static string Foreground()
    {
        var hwnd = GetForegroundWindow();
        var name = new StringBuilder(128);
        GetClassNameW(hwnd, name, name.Capacity);
        GetWindowThreadProcessId(hwnd, out var pid);
        string process;
        try { process = Process.GetProcessById((int)pid).ProcessName; } catch { process = "?"; }
        return $"class={name} process={process}";
    }

    public bool Running => !_process.HasExited;

    public bool Recenter()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (pid != _process.Id) return false;
        Input.Chord(Entry.RecenterScans);
        return true;
    }

    public void Dispose()
    {
        if (!_process.HasExited) { try { _process.Kill(true); } catch (InvalidOperationException) { } }
        CloseHandle(_job);
        _process.Dispose();
    }
}
