using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace VindOS;

sealed record Pair(string ClientId, string ServerId, string Fingerprint, string TokenHash, DateTimeOffset PairedAt);

sealed record HostIdentity(string ServerId, int DesktopPort, byte[] DesktopPfx);

static class PairStore
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vindOS");
    static readonly string PairPath = Path.Combine(Dir, "pair.bin");
    static readonly string IdentityPath = Path.Combine(Dir, "identity.bin");
    static readonly byte[] Entropy = "vindOS.pair.v1"u8.ToArray();

    public static Pair? LoadPair() => Load<Pair>(PairPath);
    public static void SavePair(Pair pair) => Save(PairPath, pair);
    public static void Forget() => File.Delete(PairPath);

    public static HostIdentity Identity()
    {
        var id = Load<HostIdentity>(IdentityPath);
        if (id is null || id.DesktopPfx is null || id.DesktopPort == 0)
            Save(IdentityPath, id = new HostIdentity(id?.ServerId ?? Guid.NewGuid().ToString("N"), RandomNumberGenerator.GetInt32(49152, 65536), Desktop.CreatePfx()));
        return id;
    }

    static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<T>(plain);
    }

    static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Dir);
        var cipher = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value), Entropy, DataProtectionScope.CurrentUser);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, cipher);
        File.Move(tmp, path, true);
    }
}
