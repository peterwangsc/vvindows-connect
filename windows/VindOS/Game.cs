using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace VindOS;

sealed class Game : IDisposable
{
    static readonly string BridgeDll = Path.Combine(AppContext.BaseDirectory, "opencomposite", "openvr_api.dll");
    const string OriginalSuffix = ".vindos-original";
    static readonly ushort[] RecenterScans = [0x1D, 0x39];

    [DllImport("kernel32.dll", SetLastError = true)] static extern nint CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(nint job, int infoClass, ref JobLimits info, int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool QueryInformationJobObject(nint job, int infoClass, ref JobAccounting info, int size, nint returned);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateJobObject(nint job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool IsProcessInJob(nint process, nint job, out bool result);
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, uint pid);
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

    [StructLayout(LayoutKind.Sequential)]
    struct JobAccounting { public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime; public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses; }

    public static string? Bridge(GameEntry entry)
    {
        if (entry.Api != "openvr") return null;
        if (!File.Exists(BridgeDll)) throw new InvalidOperationException("The OpenComposite bridge is missing from the vindOS install.");
        var expected = Sha256(BridgeDll);
        string? dir = null;
        foreach (var dll in Directory.EnumerateFiles(entry.Dir, "openvr_api.dll", SearchOption.AllDirectories).Where(Is64Bit))
        {
            if (Sha256(dll) != expected)
            {
                if (!File.Exists(dll + OriginalSuffix)) File.Copy(dll, dll + OriginalSuffix);
                File.Copy(BridgeDll, dll, true);
                Log.Write($"game {entry.Id} bridged {Path.GetRelativePath(entry.Dir, dll)}");
            }
            dir ??= Path.GetDirectoryName(dll);
        }
        return dir ?? throw new InvalidOperationException($"{entry.Name} has no 64-bit openvr_api.dll to bridge.");
    }

    static bool Is64Bit(string pe)
    {
        using var f = File.OpenRead(pe);
        Span<byte> b = stackalloc byte[4];
        f.Position = 0x3C; f.ReadExactly(b);
        f.Position = BitConverter.ToInt32(b) + 4; f.ReadExactly(b[..2]);
        return BitConverter.ToUInt16(b) == 0x8664;
    }

    static string Sha256(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    readonly nint _job;
    readonly Process _process;
    readonly Timer _watch;
    public GameEntry Entry { get; }
    public event Action<int>? Exited;
    public event Action<string>? VrLoaded;
    bool _vr;

    public Game(GameEntry entry, bool immersive)
    {
        Entry = entry;
        if (!File.Exists(entry.Exe)) throw new InvalidOperationException($"{entry.Name} is not installed.");
        var bridgeDir = immersive ? Bridge(entry) : null;
        var start = new ProcessStartInfo(entry.Exe) { WorkingDirectory = entry.WorkingDir, UseShellExecute = false };
        if (immersive) start.Environment["XR_RUNTIME_JSON"] = StreamManager.RuntimeJson;
        if (bridgeDir is not null) start.Environment["PATH"] = bridgeDir + ";" + Environment.GetEnvironmentVariable("PATH");
        foreach (var name in new[] { "PYTHONHOME", "PYTHONPATH", "PYTHONOPTIMIZE" }) start.Environment.Remove(name);
        _job = CreateJobObjectW(0, null);
        var limits = new JobLimits { LimitFlags = 0x2000 };
        SetInformationJobObject(_job, 9, ref limits, Marshal.SizeOf<JobLimits>());
        _process = Process.Start(start)!;
        AssignProcessToJobObject(_job, _process.Handle);
        Log.Write($"game {entry.Id} started {Path.GetFileName(entry.Exe)} pid {_process.Id} {(immersive ? "immersive" : "desktop")}");
        _watch = new Timer(_ =>
        {
            if (!Running) { _watch!.Dispose(); var code = _process.HasExited ? _process.ExitCode : 0; Log.Write($"game {entry.Id} exited {code}"); Exited?.Invoke(code); return; }
            if (!_vr && VrModule() is { } module) { _vr = true; Log.Write($"game {entry.Id} loaded {module}"); VrLoaded?.Invoke(module); }
        }, null, 250, 250);
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

    string? VrModule()
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (!InJob((uint)p.Id)) continue;
                try
                {
                    foreach (ProcessModule m in p.Modules)
                        if (m.ModuleName.Equals("openxr_loader.dll", StringComparison.OrdinalIgnoreCase) || m.ModuleName.Equals("openvr_api.dll", StringComparison.OrdinalIgnoreCase))
                            return $"{m.ModuleName} in {p.ProcessName} pid {p.Id}";
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        return null;
    }

    bool InJob(uint pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == 0) return false;
        var owns = IsProcessInJob(h, _job, out var inJob) && inJob;
        CloseHandle(h);
        return owns;
    }

    public bool Running
    {
        get
        {
            var info = new JobAccounting();
            return QueryInformationJobObject(_job, 1, ref info, Marshal.SizeOf<JobAccounting>(), 0) && info.ActiveProcesses > 0;
        }
    }

    public bool Recenter()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (!InJob(pid)) return false;
        Input.Chord(RecenterScans);
        return true;
    }

    public void Stop() => TerminateJobObject(_job, 1);

    public void Dispose()
    {
        _watch.Dispose();
        TerminateJobObject(_job, 1);
        CloseHandle(_job);
        _process.Dispose();
    }
}
