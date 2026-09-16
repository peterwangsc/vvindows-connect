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
using System.Threading.Channels;

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

    public async Task GameEndedAsync(string id, string reason)
    {
        SslStream? stream;
        lock (_gate) stream = _stream;
        if (stream is null || !_immersive) return;
        try { await WriteAsync(stream, 1, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, type = "game", id, running = false, reason })); }
        catch (Exception e) { Log.Write($"game end send failed {e.Message}"); }
    }

    readonly X509Certificate2 _certificate;
    readonly TcpListener _listener;
    readonly Func<string?> _tokenHash;
    readonly CancellationTokenSource _cts = new();
    readonly FrameFn _onFrame;
    readonly EventFn _onEvent;
    readonly object _gate = new();
    SslStream? _stream;
    Channel<byte[]>? _frames;
    TaskCompletionSource<(int, int)>? _size;
    bool _capturing;

    public string Fingerprint { get; }
    public int Port { get; }

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
            Channel<byte[]> frames;
            TaskCompletionSource<(int, int)> size;
            lock (_gate)
            {
                _stream?.Dispose();
                _stream = ssl;
                _frames = frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(3) { FullMode = BoundedChannelFullMode.DropOldest }, _ => vindos_desktop_idr());
                _size = size = new TaskCompletionSource<(int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!_capturing) { _capturing = true; if (vindos_desktop_start(Fps, Bitrate, _onFrame, _onEvent) != 0) throw new InvalidOperationException("Capture already running."); }
                else size.TrySetResult(_lastSize);
            }
            vindos_desktop_idr();
            var (w, h) = await size.Task.WaitAsync(TimeSpan.FromSeconds(5), _cts.Token);
            await WriteAsync(ssl, 1, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, type = "stream", width = w, height = h, fps = Fps }));
            Log.Write($"desktop stream sent {w}x{h}");
            await WriteAsync(ssl, 1, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, type = "games", games = Game.Installed().Select(g => new { id = g.Id, name = g.Name }).ToArray() }));
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
                                if (reason is null) await WriteAsync(ssl, 1, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, type = "immersive", port = ApplePort }));
                                else { Log.Write($"immersive failed {reason}"); await EndImmersiveAsync(); }
                                break;
                            case "windowed":
                                if (DesktopRequested is not null) await DesktopRequested();
                                await EndImmersiveAsync();
                                break;
                            case "recenter":
                                Log.Write($"recenter {(RecenterRequested?.Invoke() == true ? "sent" : "ignored: game not in foreground")}");
                                break;
                            case "game":
                                var id = doc.RootElement.GetProperty("id").GetString() ?? "";
                                var failure = !_immersive ? "not in immersive mode" : GameRequested is null ? "unsupported" : await GameRequested(id);
                                if (failure is not null) Log.Write($"game {id} refused: {failure}");
                                await WriteAsync(ssl, 1, failure is null ? JsonSerializer.SerializeToUtf8Bytes(new { v = 1, type = "game", id, running = true }) : JsonSerializer.SerializeToUtf8Bytes(new { v = 1, type = "game", id, running = false, reason = failure }));
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
                }
            });
            await foreach (var frame in frames.Reader.ReadAllAsync(_cts.Token))
            {
                if (reader.IsCompleted) break;
                await ssl.WriteAsync(frame, _cts.Token);
                sent++;
                if (Environment.TickCount64 - lastReport >= 1000) { lastReport = Environment.TickCount64; Log.Write($"desktop frames={sent} {input.Summary}"); }
            }
            Log.Write($"desktop session end after {sent} frames");
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or InvalidDataException or AuthenticationException or OperationCanceledException or TimeoutException or JsonException or KeyNotFoundException)
        {
            Log.Write($"desktop end {e.GetType().Name} {e.Message}");
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_stream, ssl)) { _stream = null; _frames = null; StopCapture(); StatusChanged?.Invoke(null!); }
        }
    }

    (int, int) _lastSize;

    void OnEvent(int code, int value)
    {
        Log.Write($"desktop event {code} {value:x}");
        if (code == 1) { _lastSize = (value >> 16, value & 0xFFFF); _size?.TrySetResult(_lastSize); }
        if (code == 4) { _size?.TrySetException(new InvalidDataException($"Capture failed {value:x}")); lock (_gate) { _stream?.Dispose(); } }
    }

    void OnFrame(long pts, int flags, nint data, int length)
    {
        var frame = new byte[14 + length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)(10 + length));
        frame[4] = 0;
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(5), pts);
        frame[13] = (byte)flags;
        Marshal.Copy(data, frame, 14, length);
        _frames?.Writer.TryWrite(frame);
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

    public async Task EndImmersiveAsync()
    {
        SslStream? stream;
        lock (_gate)
        {
            if (!_immersive) return;
            _immersive = false;
            StartCapture();
            stream = _stream;
        }
        vindos_desktop_idr();
        if (stream is not null) try { await WriteAsync(stream, 1, JsonSerializer.SerializeToUtf8Bytes(new { v = 1, type = "windowed" })); } catch (Exception e) { Log.Write($"windowed send failed {e.Message}"); }
    }

    public int ApplePort { get; set; }
    public bool Streaming => _stream is not null;
    volatile bool _immersive;

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

    static async Task WriteAsync(SslStream s, byte kind, byte[] payload)
    {
        var frame = new byte[5 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)(1 + payload.Length));
        frame[4] = kind;
        payload.CopyTo(frame, 5);
        await s.WriteAsync(frame);
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
