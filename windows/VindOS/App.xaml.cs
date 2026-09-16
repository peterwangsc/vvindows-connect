using System.Windows;

namespace VindOS;

public partial class App : Application
{
    public static readonly string Version = typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0";

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--restore-bridges"))
        {
            try { Bridge.Remove(Steam.Scan()); } catch (Exception x) { Log.Write($"restore bridges failed {x.Message}"); }
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
}
