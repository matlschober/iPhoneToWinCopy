using System.IO;
using System.Text;
using System.Text.Json;

namespace PocketBridge;

public sealed record InboxItem(string Id, string Name, string Kind, string Mime, long Size, DateTimeOffset Received)
{
    public string Summary => Kind == "text" ? "Text · " + FormatSize(Size) : "Foto · " + FormatSize(Size);
    public string TimeLabel => Received.ToLocalTime().ToString("dd.MM. · HH:mm");
    public string Glyph => Kind == "text" ? "\uE8A5" : "\uEB9F";
    public static string FormatSize(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1048576 ? $"{bytes / 1024d:0.#} KB" : $"{bytes / 1048576d:0.#} MB";
}

public sealed class InboxStore
{
    public static readonly string Root = AppPaths.DefaultRoot;
    public string RootDirectory { get; }
    public AppSettings Settings { get; }
    private string _directory;
    public string InboxDirectory => Volatile.Read(ref _directory);
    private readonly object _gate = new();
    private readonly List<InboxItem> _items = [];
    public event Action<InboxItem>? Received;
    public InboxStore(string? root = null)
    {
        RootDirectory = root ?? Root;
        AppPaths.CreateDataDirectory(RootDirectory);
        Settings = new AppSettings(RootDirectory);
        _directory = Settings.InboxDirectory;
        AppPaths.CreateDataDirectory(_directory);
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var item = JsonSerializer.Deserialize<InboxItem>(File.ReadAllText(file));
                if (item != null && Guid.TryParseExact(item.Id, "N", out _) && File.Exists(DataPath(item))) _items.Add(item);
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }
        // Interrupted uploads never become visible records.
        foreach (var file in Directory.EnumerateFiles(_directory, "*.part")) File.Delete(file);
    }
    public InboxItem[] Items { get { lock (_gate) return _items.OrderByDescending(x => x.Received).ToArray(); } }
    public long UsedBytes { get { lock (_gate) return _items.Sum(item => item.Size); } }
    public long RemainingBytes(AppLimits? limits = null) => Math.Max(0, (limits ?? Settings.Current).MaxInboxBytes - UsedBytes);
    public void CheckQuota(long size, AppLimits? limits = null)
    {
        var remaining = RemainingBytes(limits);
        if (size > remaining) throw new UploadException($"Zu wenig Platz im Eingang: Die Übertragung benötigt {StorageCapacity.Format(size)}, verfügbar sind noch {StorageCapacity.Format(remaining)}. Bitte Einträge löschen oder den Empfangsspeicher erhöhen.", 507);
    }
    public string DataPath(InboxItem item) => Path.Combine(_directory, item.Id + ".data");
    public string ReadText(InboxItem item) => File.ReadAllText(DataPath(item));
    public async Task<InboxItem> AddAsync(string name, string kind, string mime, Stream data, long size, CancellationToken ct, AppLimits? limits = null)
    {
        limits ??= Settings.Current;
        lock (_gate)
        {
            CheckQuota(size, limits);
        }
        var item = new InboxItem(Guid.NewGuid().ToString("N"), CleanName(name), kind, mime, size, DateTimeOffset.UtcNow);
        var path = DataPath(item);
        try
        {
            await using (var target = File.Create(path + ".part")) await data.CopyToAsync(target, ct);
            File.Move(path + ".part", path);
            await File.WriteAllTextAsync(Path.Combine(_directory, item.Id + ".json"), JsonSerializer.Serialize(item), ct);
            lock (_gate) _items.Add(item);
        }
        catch
        {
            File.Delete(path + ".part"); File.Delete(path); File.Delete(Path.Combine(_directory, item.Id + ".json"));
            throw;
        }
        Received?.Invoke(item);
        return item;
    }
    public void Delete(InboxItem item)
    {
        lock (_gate)
        {
            File.Delete(DataPath(item));
            File.Delete(Path.Combine(_directory, item.Id + ".json"));
            _items.RemoveAll(x => x.Id == item.Id);
        }
    }
    public void Clear()
    {
        // Only known inbox records are removed. Settings, pairing and saved exports stay intact.
        foreach (var item in Items) Delete(item);
    }
    // The owner pauses and drains the receiver before applying settings or relocating.
    public async Task<string?> ApplySettingsAsync(AppLimits limits, string directory, bool? speechEnabled = null)
    {
        limits.Validate();
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var original = InboxDirectory;
        var relocating = !string.Equals(destination, original, AppPaths.Comparison);
        var items = Items;
        var used = items.Sum(item => item.Size);
        StorageCapacity.Validate(limits.MaxInboxBytes, used, relocating, StorageCapacity.Read(destination).AvailableBytes);
        if (!relocating) { Settings.Save(limits, original, speechEnabled); return null; }
        if (destination.StartsWith(original + Path.DirectorySeparatorChar, AppPaths.Comparison)
            || original.StartsWith(destination + Path.DirectorySeparatorChar, AppPaths.Comparison))
            throw new ArgumentException("Bitte einen Speicherort außerhalb des bisherigen Eingangsordners wählen.");
        AppPaths.CreateDataDirectory(destination);
        if (Directory.EnumerateFileSystemEntries(destination).Any())
            throw new ArgumentException("Der neue Eingangsordner ist nicht leer. Bitte einen leeren Ordner wählen; vorhandene Dateien werden nicht überschrieben.");
        var created = new List<string>();
        try
        {
            foreach (var item in items)
            {
                var targetPath = Path.Combine(destination, item.Id + ".data");
                await using (var source = File.OpenRead(Path.Combine(original, item.Id + ".data")))
                {
                    if (source.Length != item.Size) throw new IOException("Ein Eintrag ist unvollständig. Der bisherige Speicherort bleibt aktiv.");
                    await using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                    created.Add(targetPath);
                    await source.CopyToAsync(target);
                }
                var metadata = Path.Combine(destination, item.Id + ".json");
                // CreateNew prevents overwriting files introduced by another process mid-copy.
                await using (var target = new FileStream(metadata, FileMode.CreateNew, FileAccess.Write))
                {
                    created.Add(metadata);
                    await JsonSerializer.SerializeAsync(target, item);
                }
            }
            // Commit only after every record has been copied. Original files still exist here.
            Settings.Save(limits, destination, speechEnabled);
            Volatile.Write(ref _directory, destination);
        }
        catch
        {
            foreach (var path in created)
            {
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }
        var leftovers = 0;
        foreach (var item in items)
        {
            foreach (var extension in new[] { ".data", ".json" })
            {
                try { File.Delete(Path.Combine(original, item.Id + extension)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { leftovers++; }
            }
        }
        return leftovers == 0 ? null : $"Neuer Speicherort ist aktiv. {leftovers} alte Dateien konnten unter {original} nicht entfernt werden; bitte dort manuell prüfen.";
    }
    public static string CleanName(string name)
    {
        name = name.Replace('\\', '/').Split('/').Last();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "Foto" : name[..Math.Min(name.Length, 120)];
    }
}

public sealed class UploadException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}
