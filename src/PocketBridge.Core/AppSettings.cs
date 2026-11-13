using System.IO;
using System.Text.Json;

namespace PocketBridge;

public sealed record AppLimits
{
    public int MaxInboxMb { get; init; } = 512;
    public long MaxInboxBytes => MaxInboxMb * 1024L * 1024;

    public void Validate()
    {
        static void Range(int value, int min, int max, string name)
        {
            if (value < min || value > max) throw new ArgumentException($"{name}: Bitte einen Wert zwischen {min:N0} und {max:N0} eingeben.");
        }
        Range(MaxInboxMb, 1, 1_048_576, "MB im Eingang");
    }
}

public sealed class AppSettings
{
    private sealed record Preferences(AppLimits Limits, string InboxDirectory, bool SpeechEnabled = true);
    private readonly string _path;
    private Preferences _preferences;
    public AppLimits Current => Volatile.Read(ref _preferences).Limits;
    public string InboxDirectory => Volatile.Read(ref _preferences).InboxDirectory;
    public bool SpeechEnabled => Volatile.Read(ref _preferences).SpeechEnabled;
    public string? LoadWarning { get; }
    public AppSettings(string root)
    {
        _path = Path.Combine(root, "settings.json");
        _preferences = new(new AppLimits(), Path.Combine(root, "inbox"));
        if (!File.Exists(_path)) return;
        try
        {
            var json = File.ReadAllText(_path);
            using var document = JsonDocument.Parse(json);
            var preferences = document.RootElement.TryGetProperty("Limits", out _)
                ? JsonSerializer.Deserialize<Preferences>(json) ?? throw new JsonException()
                : new Preferences(JsonSerializer.Deserialize<AppLimits>(json) ?? throw new JsonException(), InboxDirectory);
            if (preferences.Limits == null) throw new JsonException();
            preferences.Limits.Validate();
            if (!Path.IsPathFullyQualified(preferences.InboxDirectory)) throw new ArgumentException("Der Speicherort muss ein absoluter Pfad sein.");
            _preferences = preferences with { InboxDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(preferences.InboxDirectory)) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            LoadWarning = "Die gespeicherten Limits konnten nicht geladen werden. Es gelten die Standardwerte; bitte Einstellungen prüfen.";
        }
    }
    public void Save(AppLimits limits, string? inboxDirectory = null, bool? speechEnabled = null)
    {
        limits.Validate();
        var preferences = new Preferences(limits, Path.TrimEndingDirectorySeparator(Path.GetFullPath(inboxDirectory ?? InboxDirectory)), speechEnabled ?? SpeechEnabled);
        AppPaths.CreateDataDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, true);
        Volatile.Write(ref _preferences, preferences);
    }
}
