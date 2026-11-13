using System.Windows;
using System.Threading;
using System.IO;

namespace PocketBridge;

public partial class App : Application
{
    private Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new Mutex(true, @"Local\PocketBridge", out var created);
        if (!created)
        {
            MessageBox.Show("PocketBridge läuft bereits. Öffne das vorhandene Fenster.", "PocketBridge");
            Shutdown();
            return;
        }
        base.OnStartup(e);
        try
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("Der Zwischenspeicher ist nicht erreichbar. Bitte das gewählte Laufwerk verbinden und die Anwendung erneut starten.\n\n" + ex.Message, "PocketBridge", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
