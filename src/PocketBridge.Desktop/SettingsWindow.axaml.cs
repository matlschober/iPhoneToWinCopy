using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PocketBridge.Desktop;

internal partial class SettingsWindow : Window
{
    private readonly InboxStore _store;
    private readonly LinuxSpeaker _speaker;
    private readonly Func<AppLimits, string, bool, Task<string?>> _apply;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _saving;
    public string? Notice { get; private set; }
    public SettingsWindow(InboxStore store, LinuxSpeaker speaker, Func<AppLimits, string, bool, Task<string?>> apply)
    {
        InitializeComponent(); _store = store; _speaker = speaker; _apply = apply;
        LocationBox.Text = store.InboxDirectory; InboxMb.Text = store.Settings.Current.MaxInboxMb.ToString();
        SpeechEnabled.IsChecked = store.Settings.SpeechEnabled; SpeechInfo.Text = speaker.Description;
        _timer.Tick += (_, _) => UpdateStorage();
        Opened += (_, _) => { UpdateStorage(); _timer.Start(); };
        Closed += (_, _) => _timer.Stop();
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
    }
    private void Storage_Changed(object? sender, TextChangedEventArgs e) { if (_store != null && CapacityHint != null) UpdateStorage(); }
    private void UpdateStorage()
    {
        try
        {
            var disk = StorageCapacity.Read(LocationBox.Text ?? _store.InboxDirectory);
            FreeSpaceLabel.Text = $"{StorageCapacity.Format(disk.AvailableBytes)} frei von {StorageCapacity.Format(disk.TotalBytes)}";
            CapacityHint.Text = $"Eingang: {StorageCapacity.Format(_store.UsedBytes)} belegt.";
            if (int.TryParse(InboxMb.Text, out var mb) && mb > 0)
            {
                var moving = !string.Equals(LocationBox.Text, _store.InboxDirectory, AppPaths.Comparison);
                var required = StorageCapacity.RequiredBytes(mb * 1048576L, _store.UsedBytes, moving);
                CapacityHint.Text += $" Zusätzlich benötigt: {StorageCapacity.Format(required)}.";
                if (required > disk.AvailableBytes) CapacityHint.Text += " Nicht genug freier Laufwerksspeicher.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { FreeSpaceLabel.Text = "Speicher nicht verfügbar"; CapacityHint.Text = ex.Message; }
    }
    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Übergeordneten Ordner wählen", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) LocationBox.Text = Path.Combine(path, "PocketBridge-Eingang");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException) { ValidationError.Text = "Ordnerauswahl nicht verfügbar: " + ex.Message; }
    }
    private void DefaultFolder_Click(object? sender, RoutedEventArgs e) => LocationBox.Text = Path.Combine(_store.RootDirectory, "inbox");
    private void TestSpeech_Click(object? sender, RoutedEventArgs e) => _speaker.Announce(new TransferReceipt(3, (long)(20.7 * 1048576)), true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (_saving) return;
        try
        {
            if (!int.TryParse(InboxMb.Text, out var mb)) throw new ArgumentException("Bitte eine ganze Zahl in MB eingeben.");
            var limits = new AppLimits { MaxInboxMb = mb }; limits.Validate();
            _saving = true; IsEnabled = false; ValidationError.Text = "Einstellungen werden gespeichert …";
            Notice = await _apply(limits, LocationBox.Text ?? _store.InboxDirectory, SpeechEnabled.IsChecked == true);
            _saving = false; Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { ValidationError.Text = ex.Message; }
        finally { _saving = false; IsEnabled = true; UpdateStorage(); }
    }
}
