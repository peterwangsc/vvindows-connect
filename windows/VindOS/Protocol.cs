using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VindOS;

sealed record RequestConnection(string ProtocolVersion, string SessionID, string ClientID);

static class Status
{
    public const string Waiting = "WAITING", Connecting = "CONNECTING", Connected = "CONNECTED", Paused = "PAUSED", Disconnected = "DISCONNECTED";
}

static class Wire
{
    static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static async Task<JsonDocument> ReadAsync(NetworkStream s, CancellationToken ct)
    {
        var head = new byte[4];
        await s.ReadExactlyAsync(head, ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (length is < 2 or > 65536) throw new InvalidDataException($"Frame length {length} out of range.");
        var body = new byte[length];
        await s.ReadExactlyAsync(body, ct);
        return JsonDocument.Parse(body);
    }

    public static async Task SendAsync(NetworkStream s, object message, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        var frame = new byte[4 + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, body.Length);
        body.CopyTo(frame, 4);
        await s.WriteAsync(frame, ct);
    }

    public static object AcknowledgeConnection(string sessionId, string serverId, string? fingerprint) =>
        new { Event = "AcknowledgeConnection", SessionID = sessionId, ServerID = serverId, CertificateFingerprint = fingerprint };
    public static object AcknowledgeBarcodePresentation(string sessionId) => new { Event = "AcknowledgeBarcodePresentation", SessionID = sessionId };
    public static object MediaStreamIsReady(string sessionId) => new { Event = "MediaStreamIsReady", SessionID = sessionId };
    public static object RequestSessionDisconnect(string sessionId) => new { Event = "RequestSessionDisconnect", SessionID = sessionId };
    public static string Paired(string serverId, string hostName, string sha256, string token) =>
        JsonSerializer.Serialize(new { v = 2, type = "paired", serverId, hostName, desktop = new { sha256, token } });
    public static string Barcode(string token, string digest) => JsonSerializer.Serialize(new { token, digest });
}
