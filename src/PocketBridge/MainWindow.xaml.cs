using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using QRCoder;

namespace PocketBridge;

public partial class MainWindow : Window
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    private readonly InboxStore _store;
    private readonly ReceiverServer _server;
    private readonly ReceiptSpeaker _speaker;
    private readonly int _port;
    private BitmapSource? _bitmap;
    private bool _ready, _closing, _closed;
    private string BaseUrl => $"http://{AddressBox.SelectedItem ?? "127.0.0.1"}:{_port}";
    private string PairingUrl => BaseUrl + "/#" + _server.Key;
    private InboxItem? Selected => InboxList.SelectedItem as InboxItem;
    public MainWindow() : this(new InboxStore()) { }
    public MainWindow(InboxStore store, int port = ReceiverServer.Port)
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var caption = 0x00FFFFFF;
            var text = 0x00372218;
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        };
        _store = store; _port = port;
        _speaker = new ReceiptSpeaker(Notify);
        _server = new ReceiverServer(_store, port);
        _server.TransferReceived += receipt =>
        {
            if (_store.Settings.SpeechEnabled && !Dispatcher.HasShutdownStarted)
                Dispatcher.InvokeAsync(() => { if (!_closing && _store.Settings.SpeechEnabled) _speaker.Announce(receipt); });
        };
        _store.Received += item => Dispatcher.InvokeAsync(() =>
        {
            SearchBox.Clear(); Refresh(item.Id);
            Notify($"{(item.Kind == "text" ? "Text" : "Foto")} empfangen · {item.Name}");
        });
        var addresses = ReceiverServer.Addresses();
        AddressBox.ItemsSource = addresses.Length > 0 ? addresses : ["127.0.0.1"];
        AddressBox.SelectedIndex = 0;
        _ready = true;
        UpdateQr(); Refresh();
        Loaded += async (_, _) =>
        {
            try
            {
                ConfirmCleanup(false);
                await _server.StartAsync();
                StatusLabel.Text = "Empfang bereit"; PauseButton.IsEnabled = true;
                if (addresses.Length == 0) Notify("Kein Netzwerk gefunden. Verbindung prüfen und PocketBridge neu starten.");
                if (_store.Settings.LoadWarning is { } warning) Notify(warning);
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Start fehlgeschlagen";
                StatusDot.Fill = Brushes.Gray;
                Notify("Der Empfang konnte nicht starten: " + ex.Message);
            }
        };
    }
    private void Refresh(string? selectId = null)
    {
        selectId ??= Selected?.Id;
        var allItems = _store.Items;
        var items = allItems.Where(x => x.Name.Contains(SearchBox.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        InboxList.ItemsSource = items;
        CountLabel.Text = SearchBox.Text.Length == 0 ? allItems.Length.ToString() : $"{items.Length}/{allItems.Length}";
        ListEmpty.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListEmpty.Text = string.IsNullOrEmpty(SearchBox.Text) ? "Hier kommt dein nächster\nGedanke an." : "Keine passenden Einträge.";
        InboxList.SelectedItem = items.FirstOrDefault(x => x.Id == selectId) ?? items.FirstOrDefault();
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (_ready) Refresh(); }
    private void Address_Changed(object sender, SelectionChangedEventArgs e) { if (_ready) UpdateQr(); }
    private void UpdateQr()
    {
        using var data = QRCodeGenerator.GenerateQrCode(PairingUrl, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        using var stream = new MemoryStream(qr.GetGraphic(8));
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
        QrImage.Source = image;
    }
    private void Inbox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var item = Selected;
        DetailView.Visibility = item == null ? Visibility.Collapsed : Visibility.Visible;
        EmptyView.Visibility = item == null ? Visibility.Visible : Visibility.Collapsed;
        PhotoPreview.Source = null; TextPreview.Text = ""; _bitmap = null;
        if (item == null) return;
        ItemTitle.Text = item.Name; ItemMeta.Text = item.Summary + "  ·  " + item.TimeLabel;
        TextPreview.Visibility = item.Kind == "text" ? Visibility.Visible : Visibility.Collapsed;
        PhotoPreview.Visibility = item.Kind == "image" ? Visibility.Visible : Visibility.Collapsed;
        PreviewError.Visibility = Visibility.Collapsed; CopyButton.IsEnabled = true;
        try
        {
            if (item.Kind == "text") TextPreview.Text = _store.ReadText(item);
            else
            {
                using var stream = File.OpenRead(_store.DataPath(item));
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                if ((long)frame.PixelWidth * frame.PixelHeight > 80_000_000) throw new NotSupportedException("Bild zu groß für die Vorschau.");
                stream.Position = 0;
                var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = Math.Min(frame.PixelWidth, 2048); image.StreamSource = stream; image.EndInit(); image.Freeze();
                _bitmap = ImageSupport.Orient(image, ImageSupport.Orientation(frame)); PhotoPreview.Source = _bitmap;
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException or InvalidOperationException)
        {
            PreviewError.Text = "Für dieses Foto ist keine Vorschau verfügbar. HEIC und WebP benötigen ggf. einen Windows-Codec. Du kannst das Original weiterhin speichern.";
            PreviewError.Visibility = Visibility.Visible; CopyButton.IsEnabled = false;
        }
        if (SystemParameters.ClientAreaAnimation)
            DetailView.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        TryAction(() =>
        {
            if (item.Kind == "text") Clipboard.SetText(_store.ReadText(item));
            else
            {
                // Clipboard receives the full-resolution image, not the reduced preview.
                using var stream = File.OpenRead(_store.DataPath(item));
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                var oriented = ImageSupport.Orient(frame, ImageSupport.Orientation(frame)); oriented.Freeze();
                var data = new DataObject();
                data.SetImage(oriented);
                using var png = new MemoryStream();
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(oriented)); encoder.Save(png);
                data.SetData("PNG", new MemoryStream(png.ToArray()));
                Clipboard.SetDataObject(data, true);
            }
            Notify("In die Zwischenablage kopiert.");
        });
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        var dialog = new SaveFileDialog
        {
            FileName = item.Kind == "text" ? "Text-" + item.Received.ToLocalTime().ToString("yyyyMMdd-HHmmss") + ".txt" : item.Name,
            Filter = item.Kind == "text" ? "Textdatei (*.txt)|*.txt|Alle Dateien (*.*)|*.*" : "Originaldatei (*" + Path.GetExtension(item.Name) + ")|*" + Path.GetExtension(item.Name),
            AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true) TryAction(() => { File.Copy(_store.DataPath(item), dialog.FileName, true); Notify("Gespeichert: " + dialog.FileName); });
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        TryAction(() => { _store.Delete(item); Refresh(); Notify("Aus dem Eingang gelöscht."); });
    }
    private void CopyLink_Click(object sender, RoutedEventArgs e) => TryAction(() => { Clipboard.SetText(PairingUrl); Notify("Verbindungslink kopiert."); });
    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _server.Paused = !_server.Paused;
        PauseButton.Content = _server.Paused ? "Fortsetzen" : "Pausieren";
        StatusLabel.Text = _server.Paused ? "Empfang pausiert" : "Empfang bereit";
        StatusDot.Fill = _server.Paused ? Brushes.Gray : (Brush)FindResource("Accent");
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(this, _store, _speaker, async (limits, directory, speechEnabled) =>
        {
            var wasPaused = _server.Paused;
            await _server.PauseAndDrainAsync();
            try
            {
                var notice = await _store.ApplySettingsAsync(limits, directory, speechEnabled);
                if (!speechEnabled) _speaker.Stop();
                return notice;
            }
            finally { _server.Paused = wasPaused; }
        });
        if (dialog.ShowDialog() == true)
        {
            Refresh();
            Notify(dialog.SaveNotice ?? "Speicherort und Limits gespeichert. Sie gelten ab der nächsten Übertragung.");
        }
    }
    private bool ConfirmCleanup(bool exiting)
    {
        var items = _store.Items;
        if (items.Length == 0) return true;
        var dialog = new CleanupDialog(this, items, exiting, _store.Settings.Current.MaxInboxBytes);
        dialog.ShowDialog();
        if (dialog.Choice == CleanupChoice.Cancel) return false;
        if (dialog.Choice == CleanupChoice.Keep) return true;
        try { _store.Clear(); Refresh(); Notify("Der Eingang wurde geleert."); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Refresh();
            MessageBox.Show(this, "Einige Einträge konnten nicht gelöscht werden: " + ex.Message, "Eingang leeren", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        TryAction(() => { _server.RotateKey(); UpdateQr(); Notify("Neuer Zugangsschlüssel. iPhone neu verbinden und Kurzbefehle aktualisieren."); });
    }
    private void Guide_Click(object sender, RoutedEventArgs e)
    {
        var window = new Window { Title = "iPhone einrichten · PocketBridge", Owner = this, Width = 630, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.White, FontFamily = FontFamily };
        var panel = new StackPanel { Margin = new Thickness(30) };
        panel.Children.Add(new TextBlock { Text = "Aus dem Teilen-Menü senden", FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new TextBlock { Text = "Erstelle in der Apple-App »Kurzbefehle« einen Kurzbefehl namens »An meinen PC«. Aktiviere in den Details »Im Share-Sheet anzeigen« für Bilder, Text und URLs.\n\nFür Fotos\n1. Aktion »Bild konvertieren«: Kurzbefehleingabe → JPEG.\n2. Aktion »Inhalte von URL abrufen« mit der URL unten.\n3. Methode POST. Header X-Pocket-Key mit dem Schlüssel unten.\n4. Anfragetext: Formular. Feld vom Typ Datei, Name files, Wert: konvertiertes Bild. Für mehrere Bilder diese Schritte mit »Mit jedem wiederholen« ausführen.\n\nFür Text und Links\nEinen zweiten Kurzbefehl mit Text/URLs als Eingabe erstellen. Gleicher POST und Header, aber Formularfeld vom Typ Text mit Name text und Wert Kurzbefehleingabe.\n\nBeide Varianten benötigen dasselbe Netzwerk und den laufenden Empfang am PC. Erlaube den Netzwerkzugriff auf dem iPhone, wenn iOS danach fragt.", TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 22 });
        panel.Children.Add(new TextBlock { Text = "ENDPUNKT", Margin = new Thickness(0, 24, 0, 7), FontSize = 11 });
        panel.Children.Add(new TextBox { Text = BaseUrl + "/api/receive", IsReadOnly = true, Padding = new Thickness(9) });
        panel.Children.Add(new TextBlock { Text = "HEADER: X-Pocket-Key", Margin = new Thickness(0, 17, 0, 7), FontSize = 11 });
        panel.Children.Add(new TextBox { Text = _server.Key, IsReadOnly = true, Padding = new Thickness(9) });
        panel.Children.Add(new TextBlock { Text = "AirDrop verbindet regulär Apple-Geräte. Dieser Kurzbefehl ist unser Weg ins iPhone-Teilen-Menü. Eine reine Safari-Webseite registriert dort kein eigenes Empfangsziel.\n\nVerbindung klappt nicht? Windows-Firewall und Netzwerkprofil prüfen. Nur im vertrauenswürdigen LAN verwenden: Die lokale HTTP-Verbindung ist unverschlüsselt.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 22, 0, 0), LineHeight = 19 });
        window.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; window.Show();
    }
    private void Notify(string message)
    {
        Feedback.Text = message;
        if (SystemParameters.ClientAreaAnimation) Feedback.BeginAnimation(OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(220)));
    }
    private void TryAction(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        { Notify("Aktion fehlgeschlagen: " + ex.Message); }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        var wasPaused = _server.Paused;
        try
        {
            IsEnabled = false;
            StatusLabel.Text = "Empfang wird angehalten …";
            await _server.PauseAndDrainAsync();
            IsEnabled = true;
            if (!ConfirmCleanup(true))
            {
                _server.Paused = wasPaused; _closing = false;
                StatusLabel.Text = wasPaused ? "Empfang pausiert" : "Empfang bereit";
                return;
            }
            IsEnabled = false;
            await _server.DisposeAsync();
            _speaker.Dispose();
            _closed = true; Close();
        }
        catch (Exception ex)
        {
            IsEnabled = true; _closing = false; _server.Paused = wasPaused;
            Notify("Beenden fehlgeschlagen: " + ex.Message);
        }
    }
}
