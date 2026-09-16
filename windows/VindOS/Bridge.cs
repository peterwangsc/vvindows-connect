using System.IO;
using System.Security.Cryptography;

namespace VindOS;

static class Bridge
{
    static readonly string Dll = Path.Combine(AppContext.BaseDirectory, "opencomposite", "openvr_api.dll");
    const string OriginalSuffix = ".vindos-original";

    public static string State(GameEntry entry)
    {
        if (entry.Api != "openvr") return "OpenXR, no bridge needed";
        var targets = Targets(entry).ToList();
        if (targets.Count == 0) return "no 64-bit openvr_api.dll found";
        var expected = Sha256(Dll);
        return targets.All(t => Sha256(t) == expected) ? "bridged" : "not bridged";
    }

    public static void Apply(IEnumerable<GameEntry> entries)
    {
        if (!File.Exists(Dll)) throw new InvalidOperationException("The OpenComposite bridge is missing from the vindOS install.");
        var expected = Sha256(Dll);
        foreach (var entry in entries.Where(e => e.Api == "openvr"))
            foreach (var dll in Targets(entry))
            {
                if (Sha256(dll) == expected) continue;
                if (!File.Exists(dll + OriginalSuffix)) File.Copy(dll, dll + OriginalSuffix);
                File.Copy(Dll, dll, true);
                Log.Write($"bridge applied {entry.Id} {Path.GetRelativePath(entry.Dir, dll)}");
            }
    }

    public static void Remove(IEnumerable<GameEntry> entries)
    {
        foreach (var entry in entries)
            foreach (var original in Directory.EnumerateFiles(entry.Dir, "openvr_api.dll" + OriginalSuffix, SearchOption.AllDirectories))
            {
                File.Copy(original, original[..^OriginalSuffix.Length], true);
                File.Delete(original);
                Log.Write($"bridge removed {entry.Id} {Path.GetRelativePath(entry.Dir, original)}");
            }
    }

    static IEnumerable<string> Targets(GameEntry entry) => Directory.EnumerateFiles(entry.Dir, "openvr_api.dll", SearchOption.AllDirectories).Where(Is64Bit);

    static bool Is64Bit(string pe)
    {
        using var f = File.OpenRead(pe);
        Span<byte> b = stackalloc byte[4];
        f.Position = 0x3C; f.ReadExactly(b);
        f.Position = BitConverter.ToInt32(b) + 4; f.ReadExactly(b[..2]);
        return BitConverter.ToUInt16(b) == 0x8664;
    }

    static string Sha256(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }
}
