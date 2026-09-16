using System.Windows;

namespace VindOS;

public partial class App : Application
{
    public static readonly string Version = typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0";
}
