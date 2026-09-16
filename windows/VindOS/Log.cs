using System.IO;

namespace VindOS;

static class Log
{
    static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vindOS", "log.txt");
    static readonly object Gate = new();

    public static void Write(string line)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (File.Exists(Path) && new FileInfo(Path).Length > 1 << 20) File.Delete(Path);
            File.AppendAllText(Path, $"{DateTimeOffset.Now:O} {line}\n");
        }
    }
}
