using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PocketBridge;

public partial class SettingsWindow : Window
{
    private readonly InboxStore _store;
    private readonly Func<AppLimits, string, bool, Task<string?>> _apply;
    private readonly ReceiptSpeaker _speaker;
    private readonly DispatcherTimer _diskRefresh = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _saving;
    public string? SaveNotice { get; private set; }
    public SettingsWindow(Window owner, InboxStore store, ReceiptSpeaker speaker, Func<AppLimits, string, bool, Task<string?>> apply)
    {
        InitializeComponent(); Owner = owner; _store = store; _apply = apply; _speaker = speaker;
        LocationBox.Text = store.InboxDirectory; Populate(store.Settings.Current);
        SpeechEnabled.IsChecked = store.Settings.SpeechEnabled;
        SpeechInfo.Text = speaker.Description;
        _diskRefresh.Tick += (_, _) => UpdateStorage();
        Loaded += (_, _) => { UpdateStorage(); _diskRefresh.Start(); };
        Closed += (_, _) => _diskRefresh.Stop();
        Closing += (_, e) => { if (_saving) e.Cancel = true; };
    }
    private void Populate(AppLimits limits)
    {
        InboxMb.Text = limits.MaxInboxMb.ToString();
        ValidationError.Text = "";
    }
    private void Defaults_Click(object sender, RoutedEventArgs e) => Populate(new AppLimits());
    private void TestSpeech_Click(object sender, RoutedEventArgs e) => _speaker.Announce(new TransferReceipt(3, (long)(20.7 * 1048576)), preview: true);
    private void DefaultFolder_Click(object sender, RoutedEventArgs e) => LocationBox.Text = Path.Combine(_store.RootDirectory, "inbox");
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Übergeordneten Ordner für den Zwischenspeicher wählen", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
            LocationBox.Text = Path.Combine(dialog.FolderName, "PocketBridge-Eingang");
    }
    private void Storage_Changed(object sender, TextChangedEventArgs e)
    {
        if (_store != null && LocationBox != null && FreeSpaceLabel != null && CapacityHint != null) UpdateStorage();
    }
    private void UpdateStorage()
    {
        try
        {
            var disk = StorageCapacity.Read(LocationBox.Text);
            var used = _store.Items.Sum(item => item.Size);
            var relocating = !string.Equals(Path.TrimEndingDirectorySeparator(LocationBox.Text), _store.InboxDirectory, StringComparison.OrdinalIgnoreCase);
            FreeSpaceLabel.Text = $"{StorageCapacity.Format(disk.AvailableBytes)} frei von {StorageCapacity.Format(disk.TotalBytes)}";
            FreeSpaceLabel.Foreground = (Brush)FindResource("Ink");
            CapacityHint.Text = $"Aktueller Eingang: {StorageCapacity.Format(used)} belegt.";
            if (int.TryParse(InboxMb.Text, out var mb) && mb > 0)
            {
                var required = StorageCapacity.RequiredBytes(mb * 1048576L, used, relocating);
                CapacityHint.Text += $" Für dieses Limit{(relocating ? " und die Übernahme" : "")} zusätzlich nötig: {StorageCapacity.Format(required)}.";
                if (required > disk.AvailableBytes)
                {
                    CapacityHint.Text += " Dafür reicht der freie Speicher aktuell nicht aus.";
                    FreeSpaceLabel.Foreground = new SolidColorBrush(Color.FromRgb(161, 62, 54));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            FreeSpaceLabel.Text = "Freier Speicher nicht verfügbar";
            CapacityHint.Text = ex.Message;
        }
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        static int Number(string text) => int.TryParse(text.Trim(), out var value) ? value : throw new ArgumentException("Bitte in allen Feldern ganze Zahlen ohne Tausendertrennzeichen eingeben.");
        try
        {
            var limits = new AppLimits
            {
                MaxInboxMb = Number(InboxMb.Text)
            };
            limits.Validate();
            _saving = true; IsEnabled = false;
            ValidationError.Text = "Einstellungen werden gespeichert …";
            SaveNotice = await _apply(limits, LocationBox.Text, SpeechEnabled.IsChecked == true);
            _saving = false; IsEnabled = true;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { ValidationError.Text = ex.Message; }
        finally { _saving = false; IsEnabled = true; UpdateStorage(); }
    }
}
