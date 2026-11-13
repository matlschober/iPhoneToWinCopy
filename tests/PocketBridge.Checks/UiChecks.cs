using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Reflection;
using System.Runtime.InteropServices;
using PocketBridge;

internal static class UiChecks
{
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "PocketBridge-UiChecks-" + Guid.NewGuid().ToString("N"));
            try { Execute(root); }
            catch (Exception ex) { failure = ex; }
            finally
            {
                if (Directory.Exists(root) && Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(40))) throw new Exception("Native UI checks timed out.");
        if (failure != null) throw new Exception("Native UI checks failed.", failure);
    }

    private static void Execute(string root)
    {
        var app = new Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PocketBridge;component/Theme.xaml", UriKind.Relative) });
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Show windows under a dispatcher without App.OnStartup's production single-instance mutex.
        var store = new InboxStore(root);
        static void Seed(InboxStore target)
        {
            using var data = new MemoryStream(Encoding.UTF8.GetBytes("UI fixture"));
            target.AddAsync("UI fixture", "text", "text/plain", data, data.Length, default).GetAwaiter().GetResult();
        }
        static void Check(bool value, string label)
        {
            if (!value) throw new Exception("FAIL: " + label);
            Console.WriteLine("PASS UI: " + label);
        }
        static IEnumerable<Button> Buttons(DependencyObject parent)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            {
                if (child is Button button) yield return button;
                foreach (var nested in Buttons(child)) yield return nested;
            }
        }
        static void Click(Window window, string label) => Buttons(window).Single(button => button.Content as string == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        static void Pump(Func<bool> finished)
        {
            var frame = new DispatcherFrame();
            var end = DateTime.UtcNow.AddSeconds(10);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { if (finished() || DateTime.UtcNow > end) frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
            if (!finished()) throw new Exception("UI condition timed out.");
        }
        void ChooseCleanup(string label)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = app.Windows.OfType<CleanupDialog>().FirstOrDefault();
                if (dialog == null) return;
                if (!((TextBlock)dialog.FindName("UsageLabel")).Text.Contains("von 512 MB belegt")) throw new Exception("Cleanup dialog is missing its storage usage.");
                timer.Stop(); Click(dialog, label);
            };
            timer.Start();
        }
        static bool Ready(MainWindow window) => ((TextBlock)window.FindName("StatusLabel")).Text == "Empfang bereit";
        // Exercise the actual native synthesizer, redirecting output to a WAV instead of speakers.
        var speechErrors = new List<string>();
        var speaker = new ReceiptSpeaker(speechErrors.Add);
        Check(speaker.Description.StartsWith("Automatisch:"), "Native speech engine selects an installed voice");
        object streamObject = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpFileStream")!)!;
        var wavePath = Path.Combine(root, "announcement-check.wav");
        Directory.CreateDirectory(root);
        try
        {
            dynamic stream = streamObject;
            stream.Open(wavePath, 3, false);
            dynamic nativeVoice = typeof(ReceiptSpeaker).GetField("_voice", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(speaker)!;
            nativeVoice.AudioOutputStream = streamObject;
            speaker.Announce(new TransferReceipt(3, (long)(20.7 * 1048576)));
            Check((bool)nativeVoice.WaitUntilDone(15000), "Native async speech synthesis finishes");
            speaker.Stop(); nativeVoice.WaitUntilDone(3000);
            stream.Close();
            var wave = File.ReadAllBytes(wavePath);
            Check(wave.Length > 44 && Encoding.ASCII.GetString(wave, 0, 4) == "RIFF" && speechErrors.Count == 0, "Announcement produces actual WAV audio without audible test playback");
        }
        finally { speaker.Dispose(); Marshal.FinalReleaseComObject(streamObject); }
        Seed(store);
        const int port = 47833;
        var window = new MainWindow(store, port);
        ChooseCleanup("Behalten"); window.Show(); Pump(() => Ready(window));
        Check(store.Items.Length == 1, "Startup Keep preserves existing records");
        InboxItem other;
        using (var data = new MemoryStream(Encoding.UTF8.GetBytes("Other")))
            other = store.AddAsync("Other", "text", "text/plain", data, data.Length, default).GetAwaiter().GetResult();
        var search = (TextBox)window.FindName("SearchBox");
        var countLabel = (TextBlock)window.FindName("CountLabel");
        Pump(() => countLabel.Text == "2");
        search.Text = "UI fixture";
        Check(countLabel.Text == "1/2", "Search count shows filtered / total entries");
        search.Text = "No matches";
        Check(countLabel.Text == "0/2", "Search count includes zero matches");
        store.Delete(other); search.Clear();
        Check(countLabel.Text == "1", "Clearing search restores total-only counter");

        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var settings = app.Windows.OfType<SettingsWindow>().Single();
            Check(((TextBlock)settings.FindName("SpeechInfo")).Text.Contains("Automatisch:"), "Installed Windows voice is recognized without speaking");
            Check(settings.FindName("Photos") == null && settings.FindName("FileMb") == null, "Granular limit controls are removed");
            Check(((TextBlock)settings.FindName("FreeSpaceLabel")).Text.Contains("frei von"), "Settings show actual free disk space");
            ((TextBox)settings.FindName("InboxMb")).Text = "1048576";
            Click(settings, "Speichern");
            Check(settings.IsVisible && ((TextBlock)settings.FindName("ValidationError")).Text.Contains("frei"), "Settings reject a cache limit beyond available disk space");
            ((TextBox)settings.FindName("InboxMb")).Text = "512";
            ((TextBox)settings.FindName("InboxMb")).Text = "0";
            Click(settings, "Speichern");
            Check(settings.IsVisible && ((TextBlock)settings.FindName("ValidationError")).Text.Length > 0, "Settings show invalid input without saving");
            ((TextBox)settings.FindName("InboxMb")).Text = "512";
            ((CheckBox)settings.FindName("SpeechEnabled")).IsChecked = false;
            Click(settings, "Speichern");
        }));
        ((Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(!new AppSettings(root).SpeechEnabled, "Settings dialog persists speech switch and single quota");
        var movedDirectory = Path.Combine(root, "ui-relocated-cache");
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var settings = app.Windows.OfType<SettingsWindow>().Single();
            ((TextBox)settings.FindName("LocationBox")).Text = movedDirectory;
            Click(settings, "Speichern");
        }));
        ((Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(store.InboxDirectory == movedDirectory && new InboxStore(root).Items.Length == 1, "Settings dialog migrates the cache and preserves entries");

        ChooseCleanup("Abbrechen"); window.Close();
        Check(window.IsVisible && window.IsEnabled, "Cancel exit leaves app open");
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Add("X-Pocket-Key", File.ReadAllText(Path.Combine(root, "pairing.key")));
            var status = client.GetStringAsync($"http://127.0.0.1:{port}/api/status").GetAwaiter().GetResult();
            Check(status.Contains("\"paused\":false"), "Cancel exit resumes receiver");
        }
        ChooseCleanup("Behalten"); window.Close(); Pump(() => !window.IsVisible);
        Check(new InboxStore(root).Items.Length == 1, "Exit Keep preserves records across restart");

        var restartedStore = new InboxStore(root);
        var restarted = new MainWindow(restartedStore, port);
        ChooseCleanup("Einträge löschen"); restarted.Show(); Pump(() => Ready(restarted));
        Check(new InboxStore(root).Items.Length == 0, "Startup Delete clears persisted records");
        Seed(restartedStore);
        Pump(() => ((TextBlock)restarted.FindName("CountLabel")).Text == "1");
        ChooseCleanup("Einträge löschen"); restarted.Close(); Pump(() => !restarted.IsVisible);
        Check(new InboxStore(root).Items.Length == 0 && !new AppSettings(root).SpeechEnabled, "Exit Delete clears records and preserves settings");
        var empty = new MainWindow(new InboxStore(root), port);
        empty.Show(); Pump(() => Ready(empty));
        empty.Close(); Pump(() => !empty.IsVisible);
        Check(!app.Windows.OfType<CleanupDialog>().Any(), "Empty inbox starts and exits without cleanup prompts");
        app.Shutdown();
    }
}
