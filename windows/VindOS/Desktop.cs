using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace VindOS;

sealed class Desktop : IDisposable
{
    public const int Fps = 60, Bitrate = 20_000_000;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void FrameFn(long pts, int flags, nint data, int length);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void EventFn(int code, int value);
    [DllImport("VindOS.Xr.dll")] static extern int vindos_desktop_start(uint fps, uint bitrate, FrameFn onFrame, EventFn onEvent);
    [DllImport("VindOS.Xr.dll")] static extern void vindos_desktop_idr();
    [DllImport("VindOS.Xr.dll")] static extern void vindos_desktop_stop();
    [DllImport("VindOS.Xr.dll")] static extern void vindos_desktop_rect(out int left, out int top, out int right, out int bottom);

    public event Action<string>? StatusChanged;
    public Func<Task<string?>>? ImmersiveRequested;
    public Func<Task>? DesktopRequested;
    public Func<string, Task<string?>>? GameRequested;
    public Func<bool>? RecenterRequested;

    readonly X509Certificate2 _certificate;
    readonly TcpListener _listener;
    readonly Func<string?> _tokenHash;
    readonly CancellationTokenSource _cts = new();
    readonly FrameFn _onFrame;
    readonly EventFn _onEvent;
    readonly object _gate = new();
    SslStream? _stream;
    readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _control = new();
    readonly Queue<byte[]> _video = new();
    SemaphoreSlim? _signal;
    TaskCompletionSource<(int, int)>? _size;
    bool _capturing, _announced;
    (int, int) _lastSize;
    volatile bool _immersive;

    public string Fingerprint { get; }
    public int Port { get; }
    public int ApplePort { get; set; }
    public bool Streaming => _stream is not null;

    public Desktop(byte[] pfx, int port, Func<string?> tokenHash)
    {
        _certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet);
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(_certificate.RawData));
        _tokenHash = tokenHash;
        _onFrame = OnFrame;
        _onEvent = OnEvent;
        Port = port;
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();
        _ = AcceptAsync();
    }

    public static byte[] CreatePfx()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=vindOS", key, HashAlgorithmName.SHA256);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return cert.Export(X509ContentType.Pfx);
    }

    async Task AcceptAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); } catch (OperationCanceledException) { break; }
            _ = ServeAsync(client);
        }
    }

    async Task ServeAsync(TcpClient client)
    {
        client.NoDelay = true;
        using var _ = client;
        using var ssl = new SslStream(client.GetStream(), false);
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _certificate, ClientCertificateRequired = false, EnabledSslProtocols = SslProtocols.Tls13,
                ApplicationProtocols = [new SslApplicationProtocol("vindos/1")],
            }, _cts.Token);
            var (kind, payload) = await ReadAsync(ssl);
            using var hello = JsonDocument.Parse(payload);
            var token = kind == 1 && hello.RootElement.GetProperty("type").GetString() == "hello" ? hello.RootElement.GetProperty("token").GetString() : null;
            var expected = _tokenHash();
            if (token is null || expected is null || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Convert.FromHexString(token)), Convert.FromHexString(expected)))
            {
                Log.Write($"desktop hello rejected {client.Client.RemoteEndPoint}");
                return;
            }
            Log.Write($"desktop hello {client.Client.RemoteEndPoint}");
            SemaphoreSlim signal;
            TaskCompletionSource<(int, int)> size;
            lock (_gate)
            {
                _stream?.Dispose();
                _stream = ssl;
                _announced = false;
                _control.Clear();
                lock (_video) _video.Clear();
                _signal = signal = new SemaphoreSlim(0);
                _size = size = new TaskCompletionSource<(int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!_capturing) { _capturing = true; if (vindos_desktop_start(Fps, Bitrate, _onFrame, _onEvent) != 0) throw new InvalidOperationException("Capture already running."); }
                else size.TrySetResult(_lastSize);
            }
            var (w, h) = await size.Task.WaitAsync(TimeSpan.FromSeconds(5), _cts.Token);
            Send(new { v = 1, type = "stream", width = w, height = h, fps = Fps });
            Send(new { v = 1, type = "games", games = Game.Installed().Select(g => new { id = g.Id, name = g.Name }).ToArray() });
            _announced = true;
            vindos_desktop_idr();
            Log.Write($"desktop stream sent {w}x{h}");
            StatusChanged?.Invoke("Vision Pro is viewing this desktop.");
            long sent = 0;
            var lastReport = Environment.TickCount64;
            vindos_desktop_rect(out var left, out var top, out var right, out var bottom);
            var input = new Input(left, top, right - left, bottom - top);
            var reader = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var (k, p) = await ReadAsync(ssl);
                        if (k == 2) { input.Apply(p); continue; }
                        if (k != 1) continue;
                        using var doc = JsonDocument.Parse(p);
                        var type = doc.RootElement.GetProperty("type").GetString();
                        Log.Write($"desktop control {type} after {sent} frames");
                        switch (type)
                        {
                            case "keyframe": vindos_desktop_idr(); break;
                            case "bye": return;
                            case "immersive":
                                if (!_immersive) { _immersive = true; lock (_gate) StopCapture(); }
                                var reason = ImmersiveRequested is null ? "unsupported" : await ImmersiveRequested();
                                if (reason is null) Send(new { v = 1, type = "immersive", port = ApplePort });
                                else { Log.Write($"immersive failed {reason}"); await EndImmersiveAsync(); }
                                break;
                            case "windowed":
                                if (DesktopRequested is not null) await DesktopRequested();
                                await EndImmersiveAsync();
                                break;
                            case "recenter":
                                Log.Write($"recenter {(RecenterRequested?.Invoke() == true ? "applied" : "ignored: nothing to recenter")}");
                                break;
                            case "game":
                                var id = doc.RootElement.GetProperty("id").GetString() ?? "";
                                var failure = !_immersive ? "not in immersive mode" : GameRequested is null ? "unsupported" : await GameRequested(id);
                                if (failure is not null) Log.Write($"game {id} refused: {failure}");
                                if (failure is null) Send(new { v = 1, type = "game", id, running = true }); else Send(new { v = 1, type = "game", id, running = false, reason = failure });
                                break;
                        }
                    }
                }
                catch (Exception e) { Log.Write($"desktop read end {e.GetType().Name} {e.Message} after {sent} frames"); }
                finally
                {
                    input.ReleaseAll();
                    Log.Write($"desktop input {input.Summary}");
                    if (_immersive) { _immersive = false; if (DesktopRequested is not null) await DesktopRequested(); }
                    signal.Release();
                }
            });
            while (true)
            {
                await signal.WaitAsync(_cts.Token);
                if (reader.IsCompleted) break;
                while (_control.TryDequeue(out var control)) await ssl.WriteAsync(control, _cts.Token);
                byte[]? bytes;
                lock (_video) bytes = _video.Count > 0 ? _video.Dequeue() : null;
                if (bytes is null) continue;
                await ssl.WriteAsync(bytes, _cts.Token);
                sent++;
                if (Environment.TickCount64 - lastReport >= 1000) { lastReport = Environment.TickCount64; Log.Write($"desktop frames={sent} {input.Summary}"); }
            }
            await reader;
            Log.Write($"desktop session end after {sent} frames");
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or InvalidDataException or AuthenticationException or OperationCanceledException or TimeoutException or JsonException or KeyNotFoundException)
        {
            Log.Write($"desktop end {e.GetType().Name} {e.Message}");
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_stream, ssl)) { _stream = null; _signal = null; StopCapture(); StatusChanged?.Invoke(null!); }
        }
    }

    void Send(object control)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(control);
        var frame = new byte[5 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)(1 + payload.Length));
        frame[4] = 1;
        payload.CopyTo(frame, 5);
        _control.Enqueue(frame);
        _signal?.Release();
    }

    void OnEvent(int code, int value)
    {
        Log.Write($"desktop event {code} {value:x}");
        if (code == 1) { _lastSize = (value >> 16, value & 0xFFFF); _size?.TrySetResult(_lastSize); }
        if (code == 4) { _size?.TrySetException(new InvalidDataException($"Capture failed {value:x}")); lock (_gate) { _stream?.Dispose(); } }
    }

    void OnFrame(long pts, int flags, nint data, int length)
    {
        if (!_announced) return;
        var frame = new byte[14 + length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)(10 + length));
        frame[4] = 0;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(5), pts);
        frame[13] = (byte)flags;
        Marshal.Copy(data, frame, 14, length);
        lock (_video)
        {
            if (_video.Count >= 3) { _video.Dequeue(); vindos_desktop_idr(); }
            _video.Enqueue(frame);
        }
        _signal?.Release();
    }

    void StopCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        vindos_desktop_stop();
    }

    void StartCapture()
    {
        if (_capturing || _stream is null) return;
        _capturing = true;
        vindos_desktop_start(Fps, Bitrate, _onFrame, _onEvent);
    }

    public Task EndImmersiveAsync()
    {
        lock (_gate)
        {
            if (!_immersive) return Task.CompletedTask;
            _immersive = false;
            StartCapture();
        }
        vindos_desktop_idr();
        Send(new { v = 1, type = "windowed" });
        return Task.CompletedTask;
    }

    public Task GameEndedAsync(string id, string reason)
    {
        if (_immersive) Send(new { v = 1, type = "game", id, running = false, reason });
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        lock (_gate) _stream?.Dispose();
    }

    static async Task<(int, byte[])> ReadAsync(SslStream s)
    {
        var head = new byte[4];
        await s.ReadExactlyAsync(head);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(head);
        if (length is 0 or > 16 << 20) throw new InvalidDataException($"Frame length {length} out of range.");
        var body = new byte[length];
        await s.ReadExactlyAsync(body);
        return (body[0], body[1..]);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        Disconnect();
        StopCapture();
        _certificate.Dispose();
    }
}
