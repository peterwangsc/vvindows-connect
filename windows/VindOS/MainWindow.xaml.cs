using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VindOS;

public partial class MainWindow : Window
{
    Host? _host;
    bool _armed;
    readonly DispatcherTimer _steam = new() { Interval = TimeSpan.FromSeconds(5) };

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = App.Version;
        Loaded += (_, _) => Start();
        Closed += (_, _) => { _steam.Stop(); _host?.Dispose(); };
    }

    void Start()
    {
        if (!CloudXr.Installed)
        {
            Heading.Text = "One more thing to set up.";
            Detail.Text = "vindOS streams VR through NVIDIA CloudXR, which NVIDIA provides directly.";
            SetupText.Text = CloudXr.Dir;
            SetupCard.Visibility = Visibility.Visible;
            SteamCard.Visibility = Visibility.Collapsed;
            PairButton.Visibility = Visibility.Collapsed;
            return;
        }
        SetupCard.Visibility = Visibility.Collapsed;
        SteamCard.Visibility = Visibility.Visible;
        PairButton.Visibility = Visibility.Visible;
        try
        {
            _host = new Host();
        }
        catch (Exception e)
        {
            Heading.Text = "vindOS can't start.";
            Detail.Text = e.Message;
            SteamCard.Visibility = Visibility.Collapsed;
            PairButton.IsEnabled = ForgetButton.IsEnabled = RescanButton.IsEnabled = BridgeButton.IsEnabled = UnbridgeButton.IsEnabled = false;
            return;
        }
        HostText.Text = $"This PC appears on Vision Pro as {_host.HostName}.";
        _host.StatusChanged += s => Dispatcher.Invoke(() => Status(s));
        _host.QrChanged += png => Dispatcher.Invoke(() => ShowQr(png));
        _host.PairChanged += p => Dispatcher.Invoke(() => Refresh(p));
        _host.GamesChanged += () => Dispatcher.Invoke(Games);
        Status(_host.StatusText);
        Refresh(_host.Pair);
        Games();
        SteamStatus();
        _steam.Tick += (_, _) => SteamStatus();
        _steam.Start();
    }

    void Status(string status)
    {
        var cut = status.IndexOf(". ", StringComparison.Ordinal);
        Heading.Text = cut > 0 ? status[..(cut + 1)] : status;
        Detail.Text = cut > 0 ? status[(cut + 2)..] : "";
    }

    void SteamStatus()
    {
        var through = Steam.ThroughVindOS();
        SteamTitle.Text = through switch { true => "Steam is ready for Vision Pro", false => "Steam needs a restart", null => "Steam is not running" };
        SteamText.Text = through switch
        {
            true => "Games you start from Steam play on Vision Pro in Fullscreen.",
            false => "Restart Steam through vindOS once so games you start from Steam can play on Vision Pro.",
            null => "Start Steam through vindOS so games you start from Steam can play on Vision Pro.",
        };
        SteamButton.Content = through is null ? "Start Steam through vindOS" : "Restart Steam through vindOS";
        SteamButton.Visibility = through == true ? Visibility.Collapsed : Visibility.Visible;
    }

    void Games()
    {
        var games = _host!.Games;
        GamesPanel.Children.Clear();
        GamesText.Text = games.Count == 0 ? "No VR games found in your Steam library." : "";
        foreach (var game in games)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var state = new TextBlock { Text = Bridge.State(game), Foreground = System.Windows.Media.Brushes.Gray, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(state, Dock.Right);
            row.Children.Add(state);
            row.Children.Add(new TextBlock { Text = game.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            GamesPanel.Children.Add(row);
        }
    }

    async void Steam_Click(object sender, RoutedEventArgs e)
    {
        SteamButton.IsEnabled = false;
        SteamTitle.Text = "Restarting Steam…";
        SteamText.Text = "";
        var result = await _host!.RestartSteamAsync();
        await Task.Delay(3000);
        SteamStatus();
        if (result is not null) SteamText.Text = result;
        SteamButton.IsEnabled = true;
    }

    void Refresh(Pair? pair)
    {
        _armed = false;
        ForgetButton.IsEnabled = pair is not null;
        PairText.Text = pair is null ? "No Vision Pro is paired." : $"Paired since {pair.PairedAt.LocalDateTime:g}.";
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

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        var open = SettingsPanel.Visibility == Visibility.Visible;
        SettingsPanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        Home.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (!open) Games();
    }

    void Forget_Click(object sender, RoutedEventArgs e) => _host!.Forget();

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(CloudXr.Dir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", CloudXr.Dir) { UseShellExecute = true });
    }

    void CheckAgain_Click(object sender, RoutedEventArgs e)
    {
        try { SetupText.Text = CloudXr.Import(); } catch (Exception x) { SetupText.Text = x.Message; return; }
        if (CloudXr.Installed) Start();
    }

    void Link_Click(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    void Rescan_Click(object sender, RoutedEventArgs e) => _host!.Rescan();

    void Bridge_Click(object sender, RoutedEventArgs e) { GamesText.Text = _host!.ApplyBridges(); Games(); }

    void Unbridge_Click(object sender, RoutedEventArgs e) { GamesText.Text = _host!.RemoveBridges(); Games(); }
}
