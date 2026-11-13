using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using QRCoder;

namespace PocketBridge.Desktop;

public partial class MainWindow : Window
{
    private readonly InboxStore _store;
    private readonly ReceiverServer _server;
    private readonly LinuxSpeaker _speaker;
    private readonly FileStream _instanceLock;
    private readonly int _port;
    private Bitmap? _preview, _qr, _clipboardImage;
    private bool _ready, _closing, _closed;
    private string BaseUrl => $"http://{AddressBox.SelectedItem ?? "127.0.0.1"}:{_port}";
    private string PairingUrl => BaseUrl + "/#" + _server.Key;
    private InboxItem? Selected => InboxList.SelectedItem as InboxItem;
    public MainWindow() : this(null, ReceiverServer.Port) { }
    public MainWindow(string? dataDirectory, int port = ReceiverServer.Port)
    {
        InitializeComponent();
        var root = Path.GetFullPath(dataDirectory ?? AppPaths.DefaultRoot);
        AppPaths.CreateDataDirectory(root);
        _instanceLock = new FileStream(Path.Combine(root, "desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _store = new InboxStore(root); _port = port;
        _server = new ReceiverServer(_store, port);
        _speaker = new LinuxSpeaker(message => Dispatcher.UIThread.Post(() => Notify(message)));
        _store.Received += item => Dispatcher.UIThread.Post(() => { SearchBox.Text = ""; Refresh(item.Id); Notify("Empfangen: " + item.Name); });
        _server.TransferReceived += receipt => Dispatcher.UIThread.Post(() => { if (!_closing && _store.Settings.SpeechEnabled) _speaker.Announce(receipt); });
        var addresses = ReceiverServer.Addresses();
        AddressBox.ItemsSource = addresses.Length > 0 ? addresses : ["127.0.0.1"];
        AddressBox.SelectedIndex = 0;
        _ready = true; UpdateQr(); Refresh();
        Opened += async (_, _) =>
        {
            try
            {
                await Dialogs.Cleanup(this, _store, false); Refresh();
                await _server.StartAsync(); StatusLabel.Text = "Empfang bereit"; PauseButton.IsEnabled = true;
                if (_store.Settings.LoadWarning is { } warning) Notify(warning);
            }
            catch (Exception ex) { StatusLabel.Text = "Start fehlgeschlagen"; Notify(ex.Message); }
        };
        Closing += ClosingAsync;
    }
    private void Refresh(string? id = null)
    {
        id ??= Selected?.Id;
        var all = _store.Items;
        var query = SearchBox.Text ?? "";
        var visible = all.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        InboxList.ItemsSource = visible;
        CountLabel.Text = query.Length == 0 ? all.Length.ToString() : $"{visible.Length}/{all.Length}";
        InboxList.SelectedItem = visible.FirstOrDefault(item => item.Id == id) ?? visible.FirstOrDefault();
        if (visible.Length == 0) ShowItem();
    }
    private void Search_Changed(object? sender, TextChangedEventArgs e) { if (_ready) Refresh(); }
    private void Address_Changed(object? sender, SelectionChangedEventArgs e) { if (_ready) UpdateQr(); }
    private void Inbox_Changed(object? sender, SelectionChangedEventArgs e) { if (_ready) ShowItem(); }
    private void UpdateQr()
    {
        using var data = QRCodeGenerator.GenerateQrCode(PairingUrl, QRCodeGenerator.ECCLevel.M);
        using var qr = new PngByteQRCode(data);
        using var stream = new MemoryStream(qr.GetGraphic(8));
        QrImage.Source = null; _qr?.Dispose(); _qr = new Bitmap(stream); QrImage.Source = _qr;
    }
    private void ShowItem()
    {
        PhotoPreview.Source = null; _preview?.Dispose(); _preview = null; TextPreview.Text = "";
        TextPreview.IsVisible = false; PreviewMessage.IsVisible = true;
        var item = Selected;
        CopyButton.IsEnabled = SaveButton.IsEnabled = item != null;
        if (item == null)
        {
            ItemTitle.Text = "Bereit für deinen Transfer."; ItemMeta.Text = "Fotos und Text vom iPhone direkt hierher.";
            PreviewMessage.Text = "Ein Foto. Ein Link. Ein bisschen Text."; return;
        }
        ItemTitle.Text = item.Name; ItemMeta.Text = item.Summary + " · " + item.TimeLabel;
        try
        {
            if (item.Kind == "text") { TextPreview.Text = _store.ReadText(item); TextPreview.IsVisible = true; }
            else
            {
                var width = CheckImageSize(_store.DataPath(item));
                using var stream = File.OpenRead(_store.DataPath(item));
                _preview = Bitmap.DecodeToWidth(stream, Math.Min(width, 2048)); PhotoPreview.Source = _preview;
            }
            PreviewMessage.IsVisible = false;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException)
        {
            PreviewMessage.Text = "Keine Vorschau für dieses Format verfügbar. Das Original kann weiterhin gespeichert werden.";
            CopyButton.IsEnabled = false;
        }
    }
    private static int CheckImageSize(string path)
    {
        using var codec = SkiaSharp.SKCodec.Create(path);
        if (codec == null) throw new NotSupportedException("Kein passender Bilddecoder verfügbar.");
        if ((long)codec.Info.Width * codec.Info.Height > 80_000_000) throw new NotSupportedException("Bild zu groß für die Vorschau.");
        return codec.Info.Width;
    }
    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } item || Clipboard == null) return;
        await TryAsync(async () =>
        {
            if (item.Kind == "text") await Clipboard.SetTextAsync(_store.ReadText(item));
            else
            {
                CheckImageSize(_store.DataPath(item));
                using var stream = File.OpenRead(_store.DataPath(item));
                var image = new Bitmap(stream);
                try { await Clipboard.SetBitmapAsync(image); }
                catch { image.Dispose(); throw; }
                _clipboardImage?.Dispose(); _clipboardImage = image;
            }
            Notify("In die Zwischenablage kopiert.");
        });
    }
    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        await TryAsync(async () =>
        {
            var name = item.Kind == "text" ? "Text-" + item.Received.ToLocalTime().ToString("yyyyMMdd-HHmmss") + ".txt" : item.Name;
            var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Original speichern", SuggestedFileName = name, ShowOverwritePrompt = true });
            if (target == null) return;
            await using var input = File.OpenRead(_store.DataPath(item));
            await using var output = await target.OpenWriteAsync();
            if (output.CanSeek) output.SetLength(0);
            await input.CopyToAsync(output); Notify("Gespeichert: " + target.Name);
        });
    }
    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } item) return;
        await TryAsync(() => { _store.Delete(item); Refresh(); Notify("Aus dem Eingang gelöscht."); return Task.CompletedTask; });
    }
    private async void CopyLink_Click(object? sender, RoutedEventArgs e) => await TryAsync(async () => { if (Clipboard != null) await Clipboard.SetTextAsync(PairingUrl); Notify("Verbindungslink kopiert."); });
    private async void Rotate_Click(object? sender, RoutedEventArgs e) => await TryAsync(() => { _server.RotateKey(); UpdateQr(); Notify("Zugangsschlüssel erneuert. iPhone neu verbinden."); return Task.CompletedTask; });
    private void Pause_Click(object? sender, RoutedEventArgs e)
    {
        _server.Paused = !_server.Paused; PauseButton.Content = _server.Paused ? "Fortsetzen" : "Pausieren";
        StatusLabel.Text = _server.Paused ? "Empfang pausiert" : "Empfang bereit";
    }
    private async void Settings_Click(object? sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_store, _speaker, async (limits, directory, speech) =>
        {
            var paused = _server.Paused; await _server.PauseAndDrainAsync();
            try { var notice = await _store.ApplySettingsAsync(limits, directory, speech); if (!speech) _speaker.Stop(); return notice; }
            finally { _server.Paused = paused; }
        });
        if (await window.ShowDialog<bool>(this)) { Refresh(); Notify(window.Notice ?? "Einstellungen gespeichert."); }
    }
    private async void Guide_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard != null) await Clipboard.SetTextAsync($"URL: {BaseUrl}/api/receive\nHeader X-Pocket-Key: {_server.Key}");
        await Dialogs.Choose(this, "iPhone-Kurzbefehl einrichten", "In Apples Kurzbefehlen »Im Share-Sheet anzeigen« aktivieren. »Inhalte von URL abrufen«: Methode POST, Header X-Pocket-Key und Formularfeld files (Datei) oder text (Text). Endpunkt und Schlüssel wurden in die Zwischenablage kopiert. Für Fotos vorher optional nach JPEG konvertieren. Beide Geräte benötigen dasselbe Netzwerk.", "Schließen");
    }
    private void Notify(string text) => Feedback.Text = text;
    private async Task TryAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException) { Notify("Aktion fehlgeschlagen: " + ex.Message); }
    }
    private async void ClosingAsync(object? sender, WindowClosingEventArgs e)
    {
        if (_closed) return; e.Cancel = true; if (_closing) return; _closing = true;
        var paused = _server.Paused;
        try
        {
            IsEnabled = false; await _server.PauseAndDrainAsync(); IsEnabled = true;
            if (!await Dialogs.Cleanup(this, _store, true)) { _closing = false; _server.Paused = paused; return; }
            await _server.DisposeAsync(); _speaker.Dispose();
            _preview?.Dispose(); _qr?.Dispose(); _clipboardImage?.Dispose(); _instanceLock.Dispose();
            _closed = true; Dispatcher.UIThread.Post(Close);
        }
        catch (Exception ex) { IsEnabled = true; _closing = false; _server.Paused = paused; Notify(ex.Message); }
    }
}
