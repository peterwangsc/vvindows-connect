using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace VindOS;

public partial class MainWindow : Window
{
    Host? _host;
    bool _armed;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Start();
        Closed += (_, _) => _host?.Dispose();
    }

    void Start()
    {
        try
        {
            _host = new Host();
        }
        catch (Exception e)
        {
            StatusText.Text = e.Message;
            PairButton.IsEnabled = ForgetButton.IsEnabled = RescanButton.IsEnabled = BridgeButton.IsEnabled = UnbridgeButton.IsEnabled = SteamButton.IsEnabled = false;
            return;
        }
        HostText.Text = $"This PC appears as {_host.HostName}.";
        _host.StatusChanged += s => Dispatcher.Invoke(() => StatusText.Text = s);
        _host.QrChanged += png => Dispatcher.Invoke(() => ShowQr(png));
        _host.PairChanged += p => Dispatcher.Invoke(() => Refresh(p));
        _host.GamesChanged += () => Dispatcher.Invoke(Games);
        Refresh(_host.Pair);
        Games();
        SteamText.Text = Steam.Status();
    }

    void Games()
    {
        var games = _host!.Games;
        GamesText.Text = games.Count == 0 ? "No VR games found in the Steam library."
            : "VR games: " + string.Join("; ", games.Select(g => $"{g.Name} ({Bridge.State(g)})")) + ".";
    }

    async void Steam_Click(object sender, RoutedEventArgs e)
    {
        SteamButton.IsEnabled = false;
        SteamText.Text = "Restarting Steam…";
        SteamText.Text = await _host!.RestartSteamAsync();
        await Task.Delay(3000);
        SteamText.Text = Steam.Status();
        SteamButton.IsEnabled = true;
    }

    void Refresh(Pair? pair)
    {
        _armed = false;
        ForgetButton.IsEnabled = pair is not null;
        PairButton.Content = pair is null ? "Pair" : "Pair a different Vision Pro";
    }

    void ShowQr(byte[]? png)
    {
        if (png is null) { Qr.Visibility = Visibility.Collapsed; Qr.Source = null; return; }
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(png);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        Qr.Source = image;
        Qr.Visibility = Visibility.Visible;
    }

    void Pair_Click(object sender, RoutedEventArgs e)
    {
        if (_armed) { _host!.Cancel(); Refresh(_host.Pair); return; }
        _armed = true;
        PairButton.Content = "Cancel";
        _host!.Arm();
    }

    void Forget_Click(object sender, RoutedEventArgs e) => _host!.Forget();

    void Rescan_Click(object sender, RoutedEventArgs e) => _host!.Rescan();

    void Bridge_Click(object sender, RoutedEventArgs e) => GamesText.Text = _host!.ApplyBridges();

    void Unbridge_Click(object sender, RoutedEventArgs e) => GamesText.Text = _host!.RemoveBridges();
}
