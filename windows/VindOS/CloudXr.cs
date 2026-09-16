using System.IO;
using System.IO.Compression;

namespace VindOS;

static class CloudXr
{
    public const string RuntimeVersion = "6.2.3", ManagerVersion = "6.1.0";
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vindOS", "CloudXR");
    static readonly string Bundled = Path.Combine(AppContext.BaseDirectory, "Server");
    static readonly string[] Required = ["NvStreamManager.exe", "CloudXrService.exe", "cloudxr-runtime.yaml", "NvStreamManagerClient.dll", Path.Combine("releases", RuntimeVersion, "openxr_cloudxr.json"), Path.Combine("releases", RuntimeVersion, "openxr_cloudxr.dll")];

    public static string ServerDir => Complete(Path.Combine(Dir, "Server")) ? Path.Combine(Dir, "Server") : Bundled;

    public static bool Installed => Complete(Path.Combine(Dir, "Server")) || Complete(Bundled);

    static bool Complete(string server) => Required.All(f => File.Exists(Path.Combine(server, f)));

    public static string Import()
    {
        Directory.CreateDirectory(Dir);
        var server = Path.Combine(Dir, "Server");
        var found = new List<string>();
        foreach (var zip in Directory.EnumerateFiles(Dir, "*.zip"))
        {
            using var archive = ZipFile.OpenRead(zip);
            var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet();
            if (names.Contains("Server/NvStreamManager.exe") && names.Contains("SampleClient/NvStreamManagerClient.dll"))
            {
                foreach (var e in archive.Entries.Where(e => e.FullName.StartsWith("Server/") && e.Name.Length > 0)) Extract(e, Path.Combine(server, e.FullName["Server/".Length..]));
                Extract(archive.GetEntry("SampleClient/NvStreamManagerClient.dll")!, Path.Combine(server, "NvStreamManagerClient.dll"));
                found.Add($"Stream Manager ({Path.GetFileName(zip)})");
            }
            else if (names.Contains("openxr_cloudxr.json") && names.Contains("openxr_cloudxr.dll"))
            {
                var release = Path.Combine(server, "releases", RuntimeVersion);
                foreach (var e in archive.Entries.Where(e => e.Name.Length > 0)) Extract(e, Path.Combine(release, e.FullName.Replace('/', '\\')));
                found.Add($"Runtime ({Path.GetFileName(zip)})");
            }
        }
        Log.Write($"cloudxr import: {(found.Count == 0 ? "no NVIDIA archives found" : string.Join(", ", found))}; installed={Complete(server)}");
        return found.Count == 0 ? "No NVIDIA archives found in the folder." : string.Join(" and ", found) + " imported.";
    }

    static void Extract(ZipArchiveEntry entry, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        entry.ExtractToFile(target, true);
    }
}
