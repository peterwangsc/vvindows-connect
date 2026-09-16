using System.Runtime.InteropServices;

namespace VindOS;

sealed class XrSession : IDisposable
{
    public enum Kind { Stage = 1, Session = 2, Channel = 3, Sent = 4, Error = 5, Exit = 6 }
    public const int StageLoop = 7;
    public const float QuadWidth = 2.4f, QuadDistance = 2.0f;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void EventFn(int kind, int value);
    [DllImport("VindOS.Xr.dll", CharSet = CharSet.Unicode)] static extern int vindos_xr_start(string runtimeJson, [MarshalAs(UnmanagedType.LPUTF8Str)] string? paired, float quadWidth, float quadDistance, EventFn onEvent);
    [DllImport("VindOS.Xr.dll")] static extern void vindos_xr_stop();
    [DllImport("VindOS.Xr.dll")] static extern void vindos_xr_recenter();

    readonly EventFn _callback;

    static XrSession() => Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", StreamManager.RuntimeJson);

    public XrSession(string? pairedJson, bool desktopQuad, Action<Kind, int> onEvent)
    {
        _callback = (k, v) => onEvent((Kind)k, v);
        if (vindos_xr_start(StreamManager.RuntimeJson, pairedJson, desktopQuad ? QuadWidth : 0, QuadDistance, _callback) != 0) throw new InvalidOperationException("An OpenXR session is already running.");
    }

    public void Recenter() => vindos_xr_recenter();

    int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) vindos_xr_stop();
    }
}
