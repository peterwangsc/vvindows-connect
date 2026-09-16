using System.Runtime.InteropServices;

namespace VindOS;

sealed class Bonjour : IDisposable
{
    public const string ServiceType = "_apple-foveated-streaming._tcp";
    public const string BundleId = "com.golfcore.vvindowsconnect";

    [StructLayout(LayoutKind.Sequential)]
    struct RegisterRequest
    {
        public uint Version, InterfaceIndex; public nint Instance, Callback, Context, Credentials; public int UnicastEnabled;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void RegisterComplete(uint status, nint context, nint instance);

    [DllImport("dnsapi.dll", CharSet = CharSet.Unicode)] static extern nint DnsServiceConstructInstance(string serviceName, string hostName, nint ip4, nint ip6, ushort port, ushort priority, ushort weight, uint propertyCount, string[] keys, string[] values);
    [DllImport("dnsapi.dll")] static extern void DnsServiceFreeInstance(nint instance);
    [DllImport("dnsapi.dll")] static extern uint DnsServiceRegister(ref RegisterRequest request, nint cancel);
    [DllImport("dnsapi.dll")] static extern uint DnsServiceDeRegister(ref RegisterRequest request, nint cancel);

    static readonly RegisterComplete Complete = (_, _, instance) => { if (instance != 0) DnsServiceFreeInstance(instance); };
    readonly nint _instance;
    RegisterRequest _request;
    public string InstanceName { get; }

    public Bonjour(ushort port)
    {
        InstanceName = Environment.MachineName;
        _instance = DnsServiceConstructInstance($"{InstanceName}.{ServiceType}.local", $"{Environment.MachineName}.local", 0, 0, port, 0, 0, 1, ["Application-Identifier"], [BundleId]);
        if (_instance == 0) throw new InvalidOperationException("DnsServiceConstructInstance failed.");
        _request = new RegisterRequest { Version = 1, Instance = _instance, Callback = Marshal.GetFunctionPointerForDelegate(Complete) };
        var r = DnsServiceRegister(ref _request, 0);
        if (r != 9506) throw new InvalidOperationException($"DnsServiceRegister failed: {r}");
    }

    public void Dispose()
    {
        DnsServiceDeRegister(ref _request, 0);
        DnsServiceFreeInstance(_instance);
    }
}
