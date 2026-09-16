using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VindOS;

sealed class VrWatch : IDisposable
{
    static readonly ushort[] RecenterScans = [0x1D, 0x39];

    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    public event Action<int, string, string>? Started;
    public event Action<int, string>? Ended;

    readonly int _self = Environment.ProcessId;
    readonly Timer _timer;
    int _pid, _busy;
    string _id = "";
    bool _timed;

    public int Pid => _pid;

    public VrWatch() => _timer = new Timer(Tick, null, 0, 500);

    void Tick(object? _)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        try
        {
            if (_pid != 0)
            {
                if (Alive(_pid)) return;
                var (pid, id) = (_pid, _id);
                _pid = 0;
                Log.Write($"vr process {pid} ended");
                Ended?.Invoke(pid, id);
                return;
            }
            var watch = Stopwatch.StartNew();
            foreach (var p in Process.GetProcesses())
                using (p)
                {
                    if (p.Id == _self || _pid != 0) continue;
                    try
                    {
                        foreach (ProcessModule m in p.Modules)
                            if (m.ModuleName.Equals("openxr_loader.dll", StringComparison.OrdinalIgnoreCase) || m.ModuleName.Equals("openvr_api.dll", StringComparison.OrdinalIgnoreCase))
                            {
                                _pid = p.Id;
                                var env = ProcessEnv.Read(p.Id);
                                _id = env is not null && env.TryGetValue("SteamAppId", out var app) ? app : p.ProcessName;
                                var runtime = env is null ? "env unreadable" : env.TryGetValue("XR_RUNTIME_JSON", out var json) ? (string.Equals(json, StreamManager.RuntimeJson, StringComparison.OrdinalIgnoreCase) ? "vindOS runtime" : "other runtime") : "no XR_RUNTIME_JSON";
                                Log.Write($"vr process {p.ProcessName} pid {p.Id} loaded {m.ModuleName}, id {_id}, {runtime}");
                                Started?.Invoke(p.Id, p.ProcessName, _id);
                                break;
                            }
                    }
                    catch (Exception e) when (e is Win32Exception or InvalidOperationException) { }
                }
            if (!_timed) { _timed = true; Log.Write($"vr watch scan {watch.ElapsedMilliseconds} ms"); }
        }
        finally { _busy = 0; }
    }

    static bool Alive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }

    public bool Recenter()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        if (_pid == 0 || pid != _pid) return false;
        Input.Chord(RecenterScans);
        return true;
    }

    public void Dispose() => _timer.Dispose();
}
