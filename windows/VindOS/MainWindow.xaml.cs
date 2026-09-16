using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace VindOS;

public partial class MainWindow : Window
{
    Host? _host;
    bool _armed;
    GameEntry? _running;

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
            PairButton.IsEnabled = ForgetButton.IsEnabled = RescanButton.IsEnabled = false;
            return;
        }
        HostText.Text = $"This PC appears as {_host.HostName}.";
        _host.StatusChanged += s => Dispatcher.Invoke(() => StatusText.Text = s);
        _host.QrChanged += png => Dispatcher.Invoke(() => ShowQr(png));
        _host.PairChanged += p => Dispatcher.Invoke(() => Refresh(p));
        _host.GamesChanged += _ => Dispatcher.Invoke(Games);
        _host.GameChanged += g => Dispatcher.Invoke(() => { _running = g; Games(); });
        _host.ImmersiveChanged += _ => Dispatcher.Invoke(Games);
        Refresh(_host.Pair);
        Games();
    }

    void Games()
    {
        var host = _host!;
        GamesPanel.Children.Clear();
        GamesHeader.Text = host.Games.Count == 0 ? "No VR games found in the Steam library."
            : host.Immersive ? "VR games. Play launches into Immersive Mode." : "VR games. Play launches on the desktop; enter Fullscreen on the Vision Pro first to play in VR.";
        foreach (var game in host.Games)
        {
            var running = _running?.Id == game.Id;
            var button = new Button { Content = running ? "Stop" : "Play", Width = 72, Height = 32, IsEnabled = running || _running is null, Tag = game };
            button.Click += Play_Click;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            row.Children.Add(new TextBlock { Text = game.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            GamesPanel.Children.Add(row);
        }
    }

    async void Play_Click(object sender, RoutedEventArgs e)
    {
        var game = (GameEntry)((Button)sender).Tag;
        if (_running?.Id == game.Id) { _host!.StopGame(); return; }
        var error = await _host!.PlayAsync(game);
        if (error is not null) StatusText.Text = error;
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
}
