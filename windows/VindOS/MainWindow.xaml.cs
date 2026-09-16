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
            PairButton.IsEnabled = ForgetButton.IsEnabled = false;
            return;
        }
        HostText.Text = $"This PC appears as {_host.HostName}.";
        _host.StatusChanged += s => Dispatcher.Invoke(() => StatusText.Text = s);
        _host.QrChanged += png => Dispatcher.Invoke(() => ShowQr(png));
        _host.PairChanged += p => Dispatcher.Invoke(() => Refresh(p));
        Refresh(_host.Pair);
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
}
