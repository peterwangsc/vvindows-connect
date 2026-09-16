using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace VindOS;

sealed record GameEntry(string Id, string Name, string Dir, string Exe, string WorkingDir, string Api);

static class Steam
{
    public static string Exe => Path.Combine(Root, "steam.exe");

    public static Process? Running() => Process.GetProcessesByName("steam").FirstOrDefault();

    public static string Status()
    {
        using var steam = Running();
        if (steam is null) return "Steam is not running.";
        var env = ProcessEnv.Read(steam.Id);
        if (env is null) return "Steam is running; its environment could not be read.";
        return env.TryGetValue("XR_RUNTIME_JSON", out var json) && string.Equals(json, StreamManager.RuntimeJson, StringComparison.OrdinalIgnoreCase)
            ? "Steam is running through vindOS. Games you start from Steam use the Vision Pro in Immersive Mode."
            : "Steam is running without vindOS. Restart it through vindOS so games can use the Vision Pro.";
    }

    public static async Task RestartAsync(CancellationToken ct)
    {
        using (var steam = Running())
            if (steam is not null)
            {
                Process.Start(new ProcessStartInfo(Exe, "-shutdown") { UseShellExecute = false })!.Dispose();
                var deadline = DateTime.UtcNow.AddSeconds(60);
                while (Running() is { } alive) { alive.Dispose(); if (DateTime.UtcNow > deadline) throw new TimeoutException("Steam did not shut down."); await Task.Delay(500, ct); }
            }
        var start = new ProcessStartInfo(Exe) { UseShellExecute = false, WorkingDirectory = Root };
        start.Environment["XR_RUNTIME_JSON"] = StreamManager.RuntimeJson;
        Process.Start(start)!.Dispose();
        Log.Write("steam restarted through vindOS");
    }

    public static string Root => (string?)Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is { } p ? p.Replace('/', '\\') : @"C:\Program Files (x86)\Steam";

    public static List<GameEntry> Scan()
    {
        var root = Root;
        var games = new List<GameEntry>();
        var apps = ReadAppInfo(Path.Combine(root, "appcache", "appinfo.vdf"));
        var folders = new[] { root }.Concat(Regex.Matches(File.ReadAllText(Path.Combine(root, "steamapps", "libraryfolders.vdf")), "\"path\"\\s+\"([^\"]+)\"").Select(m => m.Groups[1].Value.Replace(@"\\", @"\"))).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            var steamapps = Path.Combine(folder, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                var text = File.ReadAllText(manifest);
                var appid = Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                var installdir = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                if (!apps.TryGetValue(appid, out var info) || Vr(info, Path.Combine(steamapps, "common", installdir)) is not { } entry) continue;
                games.Add(entry);
            }
        }
        return games.OrderBy(g => g.Name).ToList();
    }

    static GameEntry? Vr(Dictionary<string, object> app, string dir)
    {
        var info = Node(app, "appinfo") ?? app;
        var common = Node(info, "common");
        if (common is null || Str(common, "type") != "Game") return null;
        var launches = Node(Node(info, "config"), "launch")?.Values.OfType<Dictionary<string, object>>().ToList() ?? [];
        var openxr = common.ContainsKey("openxrsupport") || launches.Any(l => Str(l, "type") == "openxr");
        var openvr = common.ContainsKey("openvrsupport") || launches.Any(l => Str(l, "type") == "vr");
        if (!(openxr || openvr) || !Directory.Exists(dir)) return null;
        var api = openxr ? "openxr" : "openvr";
        var launch = launches
            .Where(l => { var c = Node(l, "config"); var os = Str(c, "oslist"); return (os is null || os.Contains("windows")) && Str(c, "osarch") != "32" && Str(c, "betakey") is null; })
            .OrderBy(l => Str(l, "type") == (openxr ? "openxr" : "vr") ? 0 : Str(l, "type") is null or "none" or "default" ? 1 : 2)
            .ThenBy(l => Str(Node(l, "config"), "osarch") == "64" ? 0 : 1)
            .FirstOrDefault();
        if (launch is null || Str(launch, "executable") is not { } executable) return null;
        var exe = Path.GetFullPath(Path.Combine(dir, executable.Replace('/', '\\')));
        if (!File.Exists(exe)) return null;
        var working = Str(launch, "workingdir") is { } w ? Path.GetFullPath(Path.Combine(dir, w.Replace('/', '\\'))) : Path.GetDirectoryName(exe)!;
        return new GameEntry(Str(info, "appid") ?? Str(common, "gameid") ?? "", Str(common, "name") ?? dir, dir, exe, working, api);
    }

    static Dictionary<string, object>? Node(Dictionary<string, object>? d, string key) => d is not null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;
    static string? Str(Dictionary<string, object>? d, string key) => d is not null && d.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).Value is { } v ? v.ToString() : null;

    static Dictionary<string, Dictionary<string, object>> ReadAppInfo(string path)
    {
        var d = File.ReadAllBytes(path);
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(d);
        if (magic != 0x07564429) throw new InvalidDataException($"appinfo.vdf format {magic:x8} is not supported.");
        var tableOffset = (int)BinaryPrimitives.ReadInt64LittleEndian(d.AsSpan(8));
        var count = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(tableOffset));
        var table = new string[count];
        for (int i = 0, p = tableOffset + 4; i < count; i++) { var e = Array.IndexOf(d, (byte)0, p); table[i] = Encoding.UTF8.GetString(d, p, e - p); p = e + 1; }
        var apps = new Dictionary<string, Dictionary<string, object>>();
        for (var p = 16; ; )
        {
            var appid = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p));
            if (appid == 0) break;
            var size = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p + 4));
            var q = p + 8 + 4 + 4 + 8 + 20 + 4 + 20;
            apps[appid.ToString()] = ReadNode(d, table, ref q);
            p += 8 + size;
        }
        return apps;
    }

    static Dictionary<string, object> ReadNode(byte[] d, string[] table, ref int p)
    {
        var node = new Dictionary<string, object>();
        while (true)
        {
            var type = d[p++];
            if (type is 8 or 11) return node;
            var key = table[BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p))];
            p += 4;
            switch (type)
            {
                case 0: node[key] = ReadNode(d, table, ref p); break;
                case 1: { var e = Array.IndexOf(d, (byte)0, p); node[key] = Encoding.UTF8.GetString(d, p, e - p); p = e + 1; break; }
                case 2: node[key] = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(p)); p += 4; break;
                case 7: node[key] = BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(p)); p += 8; break;
                default: throw new InvalidDataException($"appinfo.vdf value type {type} at {p}.");
            }
        }
    }
}
