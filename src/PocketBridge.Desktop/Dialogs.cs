using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace PocketBridge.Desktop;

internal static class Dialogs
{
    public static async Task<string> Choose(Window owner, string title, string message, params string[] choices)
    {
        var dialog = new Window { Title = title, Width = 540, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 9, Margin = new Thickness(0, 22, 0, 0) };
        foreach (var choice in choices)
        {
            var button = new Button { Content = choice, IsDefault = choice == "Behalten" || choices.Length == 1, IsCancel = choice == "Abbrechen" || (choice == "Behalten" && !choices.Contains("Abbrechen")) || choices.Length == 1 };
            button.Click += (_, _) => dialog.Close(choice);
            buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel { Margin = new Thickness(26), Spacing = 12, Children =
        {
            new TextBlock { Text = title, FontSize = 23, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 14, LineHeight = 23 },
            buttons
        }};
        dialog.Opened += (_, _) => (buttons.Children.OfType<Button>().FirstOrDefault(button => button.Content as string == "Behalten") ?? buttons.Children.OfType<Button>().Last()).Focus();
        return await dialog.ShowDialog<string?>(owner) ?? "Abbrechen";
    }
    public static async Task<bool> Cleanup(Window owner, InboxStore store, bool exiting)
    {
        var items = store.Items;
        if (items.Length == 0) return true;
        var usage = StorageCapacity.Usage(store.UsedBytes, store.Settings.Current.MaxInboxBytes);
        var choices = exiting ? new[] { "Abbrechen", "Einträge löschen", "Behalten" } : ["Einträge löschen", "Behalten"];
        var result = await Choose(owner, exiting ? "Eingang vor dem Beenden leeren?" : "Alte Einträge löschen?", $"{usage}\n\n{items.Length} gespeicherte Einträge. Separat gespeicherte Dateien und Einstellungen bleiben erhalten. Die Löschung ist endgültig.", choices);
        if (result == "Einträge löschen") store.Clear();
        return !exiting || result != "Abbrechen";
    }
}
