using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;

namespace PocketBridge.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                string? root = null;
                var port = ReceiverServer.Port;
                var arguments = desktop.Args ?? [];
                for (var i = 0; i < arguments.Length; i++)
                {
                    if (arguments[i] == "--data-dir" && i + 1 < arguments.Length) root = arguments[++i];
                    else if (arguments[i] == "--port" && i + 1 < arguments.Length && int.TryParse(arguments[++i], out var value) && value is > 1023 and < 65536) port = value;
                    else throw new ArgumentException("Aufruf: PocketBridge.Desktop [--data-dir ORDNER] [--port 47831]");
                }
                desktop.MainWindow = new MainWindow(root, port);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                var close = new Button { Content = "Schließen" };
                var error = new Window { Title = "PocketBridge konnte nicht starten", Width = 540, SizeToContent = SizeToContent.Height, Content = new StackPanel { Margin = new Thickness(25), Spacing = 20, Children = { new TextBlock { Text = "Speicherort, Laufwerk oder laufende Instanz prüfen.\n\n" + ex.Message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, close } } };
                close.Click += (_, _) => error.Close(); desktop.MainWindow = error;
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
