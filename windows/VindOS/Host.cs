using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using QRCoder;

namespace VindOS;

sealed class Host : IDisposable
{
    sealed record Session(string Id, string ClientId, string Token, string Fingerprint, string DesktopToken, bool Reconnect);

    public event Action<string>? StatusChanged;
    public event Action<byte[]?>? QrChanged;
    public event Action<Pair?>? PairChanged;

    readonly HostIdentity _identity;
    readonly TcpListener _listener = new(IPAddress.Any, 0);
    readonly Bonjour _bonjour;
    readonly StreamManager _manager;
    readonly Desktop _desktop;
    readonly CancellationTokenSource _cts = new();
    NetworkStream? _stream;
    Session? _session;
    XrSession? _xr;
    bool _armed, _immersive;

    public Pair? Pair { get; private set; } = PairStore.LoadPair();
    public string HostName => _bonjour.InstanceName;

    public Host()
    {
        using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            if (new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("Run vindOS without administrator rights. The OpenXR loader ignores the app's runtime selection when elevated.");
        _identity = PairStore.Identity();
        _manager = new StreamManager();
        _desktop = new Desktop(_identity.DesktopPfx, _identity.DesktopPort, () => Pair?.TokenHash);
        _desktop.StatusChanged += s => { if (s is null) Idle(); else StatusChanged?.Invoke(s); };
        _desktop.ImmersiveRequested = BeginImmersiveAsync;
        _desktop.DesktopRequested = EndImmersiveAsync;
        _listener.Start();
        _desktop.ApplePort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _bonjour = new Bonjour((ushort)_desktop.ApplePort, _identity.ServerId, _identity.DesktopPort);
        Log.Write($"listening {_listener.LocalEndpoint} as {HostName}, desktop port {_identity.DesktopPort}");
        _ = ListenAsync();
        Idle();
    }

    public void Arm()
    {
        _armed = true;
        StatusChanged?.Invoke($"On Vision Pro, click Pair and choose {HostName}.");
    }

    public void Cancel()
    {
        _armed = false;
        _ = DisconnectAsync();
        Idle();
    }

    public void Forget()
    {
        PairStore.Forget();
        Pair = null;
        PairChanged?.Invoke(null);
        _desktop.Disconnect();
        _ = DisconnectAsync();
        Idle();
    }

    void Idle()
    {
        QrChanged?.Invoke(null);
        StatusChanged?.Invoke(Pair is null ? "Not paired." : $"Paired since {Pair.PairedAt.LocalDateTime:g}.");
    }

    async Task ListenAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                client.NoDelay = true;
                using var stream = client.GetStream();
                _stream = stream;
                Log.Write($"tcp connect {client.Client.RemoteEndPoint}");
                while (client.Connected) await HandleAsync(await Wire.ReadAsync(stream, _cts.Token));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log.Write($"tcp end {e.GetType().Name} {e.Message}"); }
            finally
            {
                _stream = null;
                if (_session is not null) await EndSessionAsync();
            }
        }
    }

    async Task HandleAsync(JsonDocument doc)
    {
        var root = doc.RootElement;
        var ev = root.GetProperty("Event").GetString();
        var sessionId = root.GetProperty("SessionID").GetString()!;
        Log.Write($"recv {ev}");
        if (ev != "RequestConnection" && _session?.Id != sessionId) { await SendAsync(Wire.RequestSessionDisconnect(sessionId)); return; }
        switch (ev)
        {
            case "RequestConnection":
                var clientId = root.GetProperty("ClientID").GetString()!;
                var reconnect = Pair is not null && Pair.ClientId == clientId;
                if (root.GetProperty("ProtocolVersion").GetString() != "1" || !(reconnect || _armed)) { await SendAsync(Wire.RequestSessionDisconnect(sessionId)); return; }
                var token = _manager.ClientToken(clientId);
                var fingerprint = _manager.Fingerprint();
                _session = new Session(sessionId, clientId, token, fingerprint, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), reconnect);
                await SendAsync(Wire.AcknowledgeConnection(sessionId, _identity.ServerId, reconnect ? fingerprint : null));
                break;
            case "RequestBarcodePresentation":
                await SendAsync(Wire.AcknowledgeBarcodePresentation(sessionId));
                using (var gen = new QRCodeGenerator())
                    QrChanged?.Invoke(new PngByteQRCode(gen.CreateQrCode(Wire.Barcode(_session!.Token, _session.Fingerprint), QRCodeGenerator.ECCLevel.L)).GetGraphic(12));
                StatusChanged?.Invoke("Scan this code with Vision Pro.");
                break;
            case "SessionStatusDidChange":
                var status = root.GetProperty("Status").GetString();
                Log.Write($"status {status}");
                QrChanged?.Invoke(null);
                if (status == Status.Waiting) await BeginStreamAsync();
                else if (status == Status.Connected)
                {
                    if (!_session!.Reconnect)
                    {
                        var hash = Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString(_session.DesktopToken)));
                        PairStore.SavePair(Pair = new Pair(_session.ClientId, _identity.ServerId, _session.Fingerprint, hash, DateTimeOffset.Now));
                        _armed = false;
                        PairChanged?.Invoke(Pair);
                    }
                    StatusChanged?.Invoke(_immersive ? "Vision Pro is in Immersive Mode." : "Vision Pro connected.");
                }
                else if (status == Status.Disconnected) await EndSessionAsync();
                break;
        }
    }

    async Task BeginStreamAsync()
    {
        if (_xr is null)
        {
            StatusChanged?.Invoke("Starting session…");
            var paired = _session!.Reconnect ? null : Wire.Paired(_identity.ServerId, HostName, _desktop.Fingerprint, _session.DesktopToken);
            var error = await StartXrAsync(paired, false);
            if (error is not null) Log.Write($"xr start failed {error}");
        }
        if (_session is not null) { await SendAsync(Wire.MediaStreamIsReady(_session.Id)); Log.Write("sent MediaStreamIsReady"); }
    }

    async Task<string?> StartXrAsync(string? paired, bool quad)
    {
        try { await _manager.StartServiceAsync(_cts.Token).WaitAsync(TimeSpan.FromSeconds(15), _cts.Token); }
        catch (Exception e) { return $"runtime: {e.Message}"; }
        var ready = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _xr = new XrSession(paired, quad, (kind, value) =>
        {
            Log.Write($"xr {kind} {value}");
            if (kind == XrSession.Kind.Stage && value == XrSession.StageLoop) ready.TrySetResult(null);
            if (kind == XrSession.Kind.Error) ready.TrySetResult($"openxr {value}");
            if (kind == XrSession.Kind.Exit) ready.TrySetResult("openxr exited");
            if (kind == XrSession.Kind.Sent) StatusChanged?.Invoke("Paired. Vision Pro is saving the connection.");
        });
        var result = await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(quad ? 20 : 5), _cts.Token)) == ready.Task ? ready.Task.Result : quad ? "openxr timeout" : null;
        if (result is not null) { _xr.Dispose(); _xr = null; _manager.StopService(); }
        return result;
    }

    async Task<string?> BeginImmersiveAsync()
    {
        if (_immersive) return null;
        var error = await StartXrAsync(null, true);
        if (error is null) { _immersive = true; StatusChanged?.Invoke("Immersive Mode ready. Waiting for Vision Pro."); }
        return error;
    }

    async Task EndImmersiveAsync()
    {
        if (!_immersive) return;
        _immersive = false;
        if (_session is not null) await SendAsync(Wire.RequestSessionDisconnect(_session.Id));
        _session = null;
        _xr?.Dispose();
        _xr = null;
        _ = Task.Run(_manager.StopService);
        Idle();
    }

    async Task EndSessionAsync()
    {
        _session = null;
        _xr?.Dispose();
        _xr = null;
        _ = Task.Run(_manager.StopService);
        if (_immersive) { _immersive = false; await _desktop.EndImmersiveAsync(); }
        Idle();
        if (_armed) Arm();
    }

    async Task DisconnectAsync()
    {
        if (_session is not null) await SendAsync(Wire.RequestSessionDisconnect(_session.Id));
        await EndSessionAsync();
    }

    async Task SendAsync(object message)
    {
        if (_stream is null) return;
        try { await Wire.SendAsync(_stream, message, _cts.Token); } catch (Exception e) { Log.Write($"send failed {e.Message}"); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _xr?.Dispose();
        _listener.Stop();
        _bonjour.Dispose();
        _desktop.Dispose();
        _manager.Dispose();
    }
}
