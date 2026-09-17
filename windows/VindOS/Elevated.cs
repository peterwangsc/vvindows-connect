using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32;

namespace VindOS;

static class Elevated
{
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, byte[] inputs, int size);
    [DllImport("kernel32.dll")] static extern bool GetNamedPipeClientProcessId(nint pipe, out uint pid);
    [DllImport("kernel32.dll")] static extern bool GetNamedPipeServerProcessId(nint pipe, out uint pid);

    const string Key = @"Software\vindOS", Prefix = "vindOS-input-";
    static readonly object Gate = new();
    static volatile NamedPipeClientStream? _pipe;

    public static bool Enabled
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue("ElevatedInput") is 1; }
        set
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("ElevatedInput", value ? 1 : 0, RegistryValueKind.DWord);
            Log.Write($"elevated input {(value ? "enabled" : "disabled")}");
            if (!value) Stop();
        }
    }

    public static void Start()
    {
        lock (Gate)
        {
            if (!Enabled || _pipe is not null) return;
            var name = Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            NamedPipeClientStream? pipe = null;
            try
            {
                using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--input {Environment.ProcessId} {name}") { UseShellExecute = true, Verb = "runas" })
                    ?? throw new InvalidOperationException("no process handle");
                pipe = new NamedPipeClientStream(".", name, PipeDirection.Out);
                pipe.Connect(10000);
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var server) || server != child.Id)
                    throw new InvalidOperationException($"pipe server {server} is not the input process {child.Id}");
                _pipe = pipe;
                Log.Write($"elevated input on, pid {child.Id}");
            }
            catch (Exception e) when (e is Win32Exception or IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException)
            {
                pipe?.Dispose();
                Log.Write($"elevated input unavailable ({e.Message}); using normal input");
            }
        }
    }

    public static void Stop()
    {
        lock (Gate) { _pipe?.Dispose(); _pipe = null; }
    }

    public static bool Write(ReadOnlySpan<byte> input)
    {
        var pipe = _pipe;
        if (pipe is null) return false;
        try { lock (pipe) pipe.Write(input); return true; }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            if (ReferenceEquals(_pipe, pipe)) { _pipe = null; Log.Write($"elevated input lost ({e.Message}); using normal input"); }
            return false;
        }
    }

    public static void Serve(string[] args)
    {
        var i = Array.IndexOf(args, "--input");
        if (args.Length < i + 3 || !uint.TryParse(args[i + 1], out var parent)) return;
        var name = args[i + 2];
        if (name.Length != Prefix.Length + 32 || !name.StartsWith(Prefix, StringComparison.Ordinal) || !name[Prefix.Length..].All(Uri.IsHexDigit)) return;
        Log.Name = "input-log.txt";
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        using var pipe = NamedPipeServerStreamAcl.Create(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        using (var wait = new CancellationTokenSource(15000))
            try { pipe.WaitForConnectionAsync(wait.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { Log.Write($"input process: no client within 15 s, parent {parent}"); return; }
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var client) || client != parent)
        {
            Log.Write($"input process: client {client} is not parent {parent}");
            return;
        }
        Log.Write($"input process serving parent {parent}");
        var buffer = new byte[Input.Size];
        var keys = new HashSet<(ushort Scan, uint Flags)>();
        int events = 0, failures = 0;
        uint last = 0, buttons = 0;
        try
        {
            while (true)
            {
                pipe.ReadExactly(buffer);
                events++;
                var flags = BitConverter.ToUInt32(buffer, buffer[0] == 1 ? 12 : 20);
                if (buffer[0] == 1) { var key = (BitConverter.ToUInt16(buffer, 10), flags & ~2u); if ((flags & 2) != 0) keys.Remove(key); else keys.Add(key); }
                else buttons = (buttons | (flags & 0x2A)) & ~((flags & 0x54) >> 1);
                if (SendInput(1, buffer, buffer.Length) == 0) { failures++; last = (uint)Marshal.GetLastWin32Error(); }
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException) { }
        foreach (var (scan, flags) in keys)
        {
            Array.Clear(buffer);
            buffer[0] = 1;
            BitConverter.TryWriteBytes(buffer.AsSpan(10), scan);
            BitConverter.TryWriteBytes(buffer.AsSpan(12), flags | 2);
            SendInput(1, buffer, buffer.Length);
        }
        for (var down = 2u; down <= 0x20; down <<= 2)
        {
            if ((buttons & down) == 0) continue;
            Array.Clear(buffer);
            BitConverter.TryWriteBytes(buffer.AsSpan(20), down << 1);
            SendInput(1, buffer, buffer.Length);
        }
        Log.Write($"input process ended events={events} failures={failures} lastError={last} released keys={keys.Count} buttons={buttons:x}");
    }
}
