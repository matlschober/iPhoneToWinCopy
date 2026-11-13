using System.Windows;

namespace PocketBridge;

public enum CleanupChoice { Keep, Delete, Cancel }

public partial class CleanupDialog : Window
{
    public CleanupChoice Choice { get; private set; }
    public CleanupDialog(Window owner, InboxItem[] items, bool exiting, long capacityBytes)
    {
        InitializeComponent(); Owner = owner;
        Choice = exiting ? CleanupChoice.Cancel : CleanupChoice.Keep;
        Title = exiting ? "PocketBridge beenden" : "Persistierter Eingang";
        Heading.Text = exiting ? "Eingang vor dem Beenden leeren?" : "Alte Einträge löschen?";
        UsageLabel.Text = StorageUsage(items.Sum(item => item.Size), capacityBytes);
        Description.Text = $"{items.Length} gespeicherte Einträge sind im Eingang vorhanden. Möchtest du die alten Einträge löschen oder behalten?";
        CancelButton.Visibility = exiting ? Visibility.Visible : Visibility.Collapsed;
    }
    public static string StorageUsage(long usedBytes, long capacityBytes) => StorageCapacity.Usage(usedBytes, capacityBytes);
    private void Delete_Click(object sender, RoutedEventArgs e) { Choice = CleanupChoice.Delete; DialogResult = true; }
    private void Keep_Click(object sender, RoutedEventArgs e) { Choice = CleanupChoice.Keep; DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Choice = CleanupChoice.Cancel; DialogResult = false; }
}
