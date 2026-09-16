using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VindOS;

sealed class StreamManager : IDisposable
{
    const string Dll = "NvStreamManagerClient.dll";
    public const string RuntimeVersion = "6.2.3";

    [StructLayout(LayoutKind.Sequential)]
    struct ServiceStatus
    {
        [MarshalAs(UnmanagedType.I1)] public bool RuntimeRunning;
        [MarshalAs(UnmanagedType.I1)] public bool AppConnected;
        [MarshalAs(UnmanagedType.I1)] public bool ClientConnected;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] LogPath;
        public nuint LogPathLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4096)] public int[] Reserved;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int nv_rpc_client_create(string? pipe, out nint client);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int nv_rpc_client_destroy(nint client);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int nv_rpc_client_connect(nint client);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int nv_rpc_client_disconnect(nint client);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int nv_rpc_client_set_client_id(nint client, string id, nuint idLength, byte[] token, nuint tokenSize, out nuint tokenSizeOut);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int nv_rpc_client_start_cxr_service(nint client, string version, nuint versionLength);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int nv_rpc_client_stop_cxr_service(nint client);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int nv_rpc_client_get_crypto_key_fingerprint(nint client, int algorithm, StringBuilder fingerprint, nuint size);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern int nv_rpc_client_get_cxr_service_status(nint client, out ServiceStatus status);
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] static extern nint nv_rpc_client_get_error_string(int result);

    [DllImport("kernel32.dll", SetLastError = true)] static extern nint CreateJobObjectW(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(nint job, int infoClass, ref JobLimits info, int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(nint job, nint process);

    [StructLayout(LayoutKind.Sequential)]
    struct JobLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    public static readonly string ServerDir = Path.Combine(AppContext.BaseDirectory, "Server");
    public static readonly string RuntimeJson = Path.Combine(ServerDir, "releases", RuntimeVersion, "openxr_cloudxr.json");

    readonly nint _job;
    readonly Process _manager;
    readonly nint _client;
    bool _serviceRunning;

    public StreamManager()
    {
        if (Process.GetProcessesByName("NvStreamManager").Length > 0) throw new InvalidOperationException("Another NvStreamManager is already running on this PC.");
        _job = CreateJobObjectW(0, null);
        var limits = new JobLimits { LimitFlags = 0x2000 };
        SetInformationJobObject(_job, 9, ref limits, Marshal.SizeOf<JobLimits>());
        _manager = Process.Start(new ProcessStartInfo(Path.Combine(ServerDir, "NvStreamManager.exe"), $"--config \"{Path.Combine(ServerDir, "cloudxr-runtime.yaml")}\"")
        {
            WorkingDirectory = ServerDir, UseShellExecute = false, CreateNoWindow = true,
        })!;
        AssignProcessToJobObject(_job, _manager.Handle);
        Check(nv_rpc_client_create(null, out _client));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        int r;
        while ((r = nv_rpc_client_connect(_client)) != 0 && DateTime.UtcNow < deadline) Thread.Sleep(200);
        Check(r);
    }

    public string ClientToken(string clientId)
    {
        var buf = new byte[512];
        Check(nv_rpc_client_set_client_id(_client, clientId, (nuint)clientId.Length, buf, (nuint)buf.Length, out var n));
        return Encoding.ASCII.GetString(buf, 0, (int)n);
    }

    public string Fingerprint()
    {
        var sb = new StringBuilder(256);
        Check(nv_rpc_client_get_crypto_key_fingerprint(_client, 2, sb, (nuint)sb.Capacity));
        return sb.ToString();
    }

    public async Task StartServiceAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_serviceRunning && RuntimeRunning()) return;
            var r = nv_rpc_client_start_cxr_service(_client, RuntimeVersion, (nuint)RuntimeVersion.Length);
            _serviceRunning = true;
            if (r != 0) { nv_rpc_client_stop_cxr_service(_client); _serviceRunning = false; Check(r); }
            while (!RuntimeRunning()) await Task.Delay(100, ct);
            _ = Task.Run(async () => { while (_serviceRunning) { LogStatus(); await Task.Delay(500, ct); } }, ct);
        }
        finally { _gate.Release(); }
    }

    public void StopService()
    {
        _gate.Wait();
        try
        {
            if (!_serviceRunning && !RuntimeRunning()) return;
            _serviceRunning = false;
            var r = nv_rpc_client_stop_cxr_service(_client);
            if (r != 0) Log.Write($"runtime stop failed {r}");
        }
        finally { _gate.Release(); }
    }

    readonly SemaphoreSlim _gate = new(1, 1);

    public bool RuntimeRunning() => nv_rpc_client_get_cxr_service_status(_client, out var s) == 0 && s.RuntimeRunning;

    (bool, bool, bool) _lastStatus;

    public void LogStatus()
    {
        if (nv_rpc_client_get_cxr_service_status(_client, out var s) != 0) return;
        var cur = (s.RuntimeRunning, s.AppConnected, s.ClientConnected);
        if (cur == _lastStatus) return;
        _lastStatus = cur;
        Log.Write($"runtime status running={s.RuntimeRunning} app={s.AppConnected} client={s.ClientConnected}");
    }

    static void Check(int result)
    {
        if (result != 0) throw new InvalidOperationException(Marshal.PtrToStringAnsi(nv_rpc_client_get_error_string(result)) ?? $"NvStreamManager error {result}");
    }

    public void Dispose()
    {
        StopService();
        nv_rpc_client_disconnect(_client);
        nv_rpc_client_destroy(_client);
        if (!_manager.HasExited) _manager.Kill(true);
    }
}
