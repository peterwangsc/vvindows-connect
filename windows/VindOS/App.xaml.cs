using System.Windows;

namespace VindOS;

public partial class App : System.Windows.Application
{
    public static readonly string Version = typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0";

    public static readonly EventWaitHandle Show = new(false, EventResetMode.AutoReset, "vindOS-show", out created);
    public static bool FirstInstance => created;
    static bool created;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--restore-bridges"))
        {
            try { Bridge.Remove(Steam.Scan()); } catch (Exception x) { Log.Write($"restore bridges failed {x.Message}"); }
            Shutdown();
            return;
        }
        if (!FirstInstance) { Show.Set(); Shutdown(); return; }
        base.OnStartup(e);
    }
}
