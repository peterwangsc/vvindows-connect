using System.Runtime.InteropServices;

namespace VindOS;

sealed class XrSession : IDisposable
{
    public enum Kind { Stage = 1, Session = 2, Channel = 3, Sent = 4, Error = 5, Exit = 6 }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void EventFn(int kind, int value);
    [DllImport("VindOS.Xr.dll", CharSet = CharSet.Unicode)] static extern int vindos_xr_start(string runtimeJson, [MarshalAs(UnmanagedType.LPUTF8Str)] string paired, EventFn onEvent);
    [DllImport("VindOS.Xr.dll")] static extern void vindos_xr_stop();

    readonly EventFn _callback;

    static XrSession() => Environment.SetEnvironmentVariable("XR_RUNTIME_JSON", StreamManager.RuntimeJson);

    public XrSession(string pairedJson, Action<Kind, int> onEvent)
    {
        _callback = (k, v) => onEvent((Kind)k, v);
        if (vindos_xr_start(StreamManager.RuntimeJson, pairedJson, _callback) != 0) throw new InvalidOperationException("An OpenXR session is already running.");
    }

    public void Dispose() => vindos_xr_stop();
}
