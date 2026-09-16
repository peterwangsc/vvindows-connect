using System.Runtime.InteropServices;
using System.Text;

namespace VindOS;

static class ProcessEnv
{
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(nint process, int infoClass, ref BasicInformation info, int size, nint returned);
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(nint process, nint address, byte[] buffer, nint size, out nint read);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(nint handle);

    [StructLayout(LayoutKind.Sequential)] struct BasicInformation { public nint Reserved0, Peb, Reserved1, Reserved2, Pid, Reserved3; }

    public static Dictionary<string, string>? Read(int pid)
    {
        var h = OpenProcess(0x1010, false, (uint)pid);
        if (h == 0) return null;
        try
        {
            var info = new BasicInformation();
            if (NtQueryInformationProcess(h, 0, ref info, Marshal.SizeOf<BasicInformation>(), 0) != 0) return null;
            if (Bytes(h, info.Peb + 0x20, 8) is not { } parameters) return null;
            var p = (nint)BitConverter.ToInt64(parameters);
            if (Bytes(h, p + 0x3F0, 4) is not { } sizeBytes || Bytes(h, p + 0x80, 8) is not { } blockBytes) return null;
            var size = BitConverter.ToInt32(sizeBytes);
            if (size <= 0 || size > 1 << 20 || Bytes(h, (nint)BitConverter.ToInt64(blockBytes), size) is not { } block) return null;
            var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Encoding.Unicode.GetString(block).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = entry.IndexOf('=', 1);
                if (eq > 0) env[entry[..eq]] = entry[(eq + 1)..];
            }
            return env;
        }
        finally { CloseHandle(h); }
    }

    static byte[]? Bytes(nint process, nint address, int size)
    {
        var buffer = new byte[size];
        return ReadProcessMemory(process, address, buffer, size, out var read) && read == size ? buffer : null;
    }
}
