using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
#if WINDOWS_UI_CHECKS
using System.Windows.Media;
using System.Windows.Media.Imaging;
#endif
using PocketBridge;

var root = Path.Combine(Path.GetTempPath(), "PocketBridge-Checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var store = new InboxStore(root);
    const int testPort = 47832;
    await using var server = new ReceiverServer(store, testPort);
    var receipts = new List<TransferReceipt>();
    server.TransferReceived += receipt => receipts.Add(receipt);
    await server.StartAsync();
    using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{testPort}") };
    var checks = 0;
    void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); checks++; }
    var landing = await client.GetAsync("/");
    Check(landing.IsSuccessStatusCode && (await landing.Content.ReadAsStringAsync()).Contains("PocketBridge"), "Embedded phone UI");
    Check(landing.Headers.Contains("Content-Security-Policy"), "Security headers");
    Check((await client.GetAsync("/api/status")).StatusCode == HttpStatusCode.Unauthorized, "Reject anonymous status");
    using (var empty = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = "no" }))
        Check((await client.PostAsync("/api/receive", empty)).StatusCode == HttpStatusCode.Unauthorized, "Reject anonymous upload");
    client.DefaultRequestHeaders.Add("X-Pocket-Key", server.Key);
    Check((await client.GetAsync("/api/status")).IsSuccessStatusCode, "Paired status");
    using (var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = "Grüße vom iPhone 👋\nhttps://example.com/?a=1&b=2" }))
        Check((await client.PostAsync("/api/receive", content)).IsSuccessStatusCode, "Unicode text and URL upload");
    var text = store.Items.Single();
    Check(store.ReadText(text).Contains("👋") && store.ReadText(text).Contains("\n"), "Text bytes preserved");
    var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2ioAAAAASUVORK5CYII=");
    using (var form = new MultipartFormDataContent())
    {
        form.Add(new ByteArrayContent(png), "files", "../../photo.png");
        form.Add(new ByteArrayContent(png), "files", "second.png");
        var response = await client.PostAsync("/api/receive", form);
        Check(response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync()).Contains("\"received\":2"), "Multiple photos");
        Check(receipts.Last() == new TransferReceipt(2, png.Length * 2), "One announcement summary per completed multi-photo transfer");
    }
    var image = store.Items.First(x => x.Kind == "image");
    Check(File.ReadAllBytes(store.DataPath(image)).SequenceEqual(png) && !image.Name.Contains('/'), "Original preserved and filename sanitized");
    Check(new InboxStore(root).Items.Length == 3, "Inbox persists after restart");
    using (var form = new MultipartFormDataContent())
    {
        form.Add(new ByteArrayContent(png), "files", "valid.png");
        form.Add(new StringContent("<script>alert(1)</script>"), "files", "fake.jpg");
        Check((await client.PostAsync("/api/receive", form)).StatusCode == HttpStatusCode.UnsupportedMediaType && store.Items.Length == 3, "Reject disguised file before committing batch");
    }
    using (var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = new string('a', 100001) }))
        Check((await client.PostAsync("/api/receive", content)).IsSuccessStatusCode, "Text beyond the old character limit is accepted");
    using (var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = " " }))
        Check((await client.PostAsync("/api/receive", content)).StatusCode == HttpStatusCode.BadRequest, "Reject empty transfer");
    server.Paused = true;
    using (var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = "paused" }))
        Check((await client.PostAsync("/api/receive", content)).StatusCode == HttpStatusCode.ServiceUnavailable && store.Items.Length == 4, "Paused receiver rejects upload");
    server.Paused = false;
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/status"))
    {
        request.Headers.Host = "attacker.example";
        Check((await client.SendAsync(request)).StatusCode == HttpStatusCode.BadRequest, "Reject foreign Host header");
    }
    server.RotateKey();
    Check((await client.GetAsync("/api/status")).StatusCode == HttpStatusCode.Unauthorized, "Old pairing revoked");
    store.Delete(image);
    Check(!File.Exists(store.DataPath(image)) && new InboxStore(root).Items.Length == 3, "Delete persists");
#if WINDOWS_UI_CHECKS
    var pixels = new byte[] { 255, 0, 0, 0, 0, 255 };
    var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Rgb24, null, pixels, 6);
    source.Freeze();
    var rotated = ImageSupport.Orient(source, 6);
    var output = new byte[6]; rotated.CopyPixels(output, 3, 0);
    Check(rotated.PixelWidth == 1 && rotated.PixelHeight == 2 && output.SequenceEqual(pixels), "iPhone EXIF rotation retains pixel order");
    var mirrored = ImageSupport.Orient(source, 2);
    mirrored.CopyPixels(output, 6, 0);
    Check(output.SequenceEqual(new byte[] { 0, 0, 255, 255, 0, 0 }), "EXIF mirror orientation");
#endif
    client.DefaultRequestHeaders.Remove("X-Pocket-Key");
    client.DefaultRequestHeaders.Add("X-Pocket-Key", server.Key);
    var defaults = new AppLimits();
    Check(store.Settings.Current == defaults && defaults.MaxInboxMb == 512, "Default reception quota remains 512 MB");
    var custom = defaults with { MaxInboxMb = 1 };
    store.Settings.Save(custom, speechEnabled: false);
    Check(new AppSettings(root).Current == custom && !new AppSettings(root).SpeechEnabled, "Quota and speech preference persist");
    store.Clear();
    var status = JsonDocument.Parse(await client.GetStringAsync("/api/status"));
    Check(status.RootElement.GetProperty("remainingBytes").GetInt64() == 1048576, "Phone discovers remaining reception capacity");
    using (var form = new MultipartFormDataContent())
    {
        var photo = new byte[600000]; png.CopyTo(photo, 0);
        form.Add(new ByteArrayContent(photo), "files", "one.png");
        form.Add(new ByteArrayContent(photo), "files", "two.png");
        var beforeReceipts = receipts.Count;
        var response = await client.PostAsync("/api/receive", form);
        Check(response.StatusCode == HttpStatusCode.InsufficientStorage && store.Items.Length == 0 && receipts.Count == beforeReceipts, "Reject oversized combined batch before persisting or announcing");
    }
    using (var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = new string('\u20AC', 349526) }))
        Check((await client.PostAsync("/api/receive", content)).StatusCode == HttpStatusCode.InsufficientStorage, "UTF-8 text quota counts bytes, not characters");
    using (var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = new string('\u20AC', 349525) }))
        Check((await client.PostAsync("/api/receive", content)).IsSuccessStatusCode && store.RemainingBytes() == 1, "URL-encoded Unicode can fill reception quota without an artificial transport limit");
    store.Clear();
    using (var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = new string('a', 1048576) }))
        Check((await client.PostAsync("/api/receive", content)).IsSuccessStatusCode && store.RemainingBytes() == 0, "Exact remaining capacity is accepted, excluding protocol overhead");
    using (var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = "x" }))
        Check((await client.PostAsync("/api/receive", content)).StatusCode == HttpStatusCode.InsufficientStorage && store.Items.Length == 1, "Full reception quota rejects additional data");
    store.Clear();
    using (var form = new MultipartFormDataContent())
    {
        for (var i = 0; i < 201; i++) form.Add(new ByteArrayContent(png), "files", $"photo-{i}.png");
        Check((await client.PostAsync("/api/receive", form)).IsSuccessStatusCode && store.Items.Length == 201, "No independent photo-count or inbox-entry limit");
    }
    store.Clear();
    store.Settings.Save(defaults);
    using (var form = new MultipartFormDataContent())
    {
        var large = new byte[65 * 1048576]; png.CopyTo(large, 0);
        form.Add(new ByteArrayContent(large), "files", "large.png");
        Check((await client.PostAsync("/api/receive", form)).IsSuccessStatusCode && store.Items.Single().Size == large.Length, "A photo above the old file and request limits fits within reception quota");
    }
    try { store.Settings.Save(custom with { MaxInboxMb = 0 }); Check(false, "Invalid quota rejected"); }
    catch (ArgumentException) { Check(new AppSettings(root).Current == defaults, "Invalid quota does not replace persisted settings"); }
    store.Clear();
    Check(new InboxStore(root).Items.Length == 0 && Directory.GetFiles(store.InboxDirectory).Length == 0, "Cleanup deletes persisted records and data");
    Check(File.Exists(Path.Combine(root, "pairing.key")) && !new AppSettings(root).SpeechEnabled, "Cleanup preserves pairing and speech preference");
    using (var form = new MultipartFormDataContent())
    {
        form.Add(new ByteArrayContent(png), "files", "one.png");
        form.Add(new ByteArrayContent(png), "files", "two.png");
        Check((await client.PostAsync("/api/receive", form)).IsSuccessStatusCode, "Seed relocation fixture");
    }
    await server.PauseAndDrainAsync();
    Check(server.Paused, "Shutdown drains uploads and pauses receiver");
    var disk = StorageCapacity.Read(root);
    Check(disk.AvailableBytes > 0 && disk.TotalBytes >= disk.AvailableBytes, "Actual free disk space is available for cache volume");
    StorageCapacity.Validate(1048576, 524288, false, 524288);
    Check(StorageCapacity.RequiredBytes(1048576, 524288, false) == 524288, "Space check accounts for already stored data");
    try { StorageCapacity.Validate(2097152, 0, false, 1048576); Check(false, "Insufficient free space rejected"); }
    catch (ArgumentException) { Check(true, "Insufficient free space rejected"); }
    var originalDirectory = store.InboxDirectory;
    var snapshot = store.Items;
    var originalBytes = File.ReadAllBytes(store.DataPath(snapshot[0]));
    var movedDirectory = Path.Combine(root, "relocated-cache");
    await store.ApplySettingsAsync(store.Settings.Current with { MaxInboxMb = 1 }, movedDirectory);
    Check(store.InboxDirectory == movedDirectory && new InboxStore(root).Items.Length == snapshot.Length, "Cache location persists and records survive relocation");
    Check(File.ReadAllBytes(store.DataPath(snapshot[0])).SequenceEqual(originalBytes) && Directory.GetFiles(originalDirectory).Length == 0, "Relocation preserves bytes and clears previous inbox files");
    var occupiedDirectory = Path.Combine(root, "occupied-cache");
    Directory.CreateDirectory(occupiedDirectory);
    File.WriteAllText(Path.Combine(occupiedDirectory, "keep.txt"), "keep");
    try { await store.ApplySettingsAsync(store.Settings.Current, occupiedDirectory); Check(false, "Occupied destination rejected"); }
    catch (ArgumentException) { Check(File.ReadAllText(Path.Combine(occupiedDirectory, "keep.txt")) == "keep" && store.InboxDirectory == movedDirectory, "Occupied destination is not overwritten"); }
    var failedDirectory = Path.Combine(root, "failed-cache");
    using (var locked = new FileStream(store.DataPath(snapshot.Last()), FileMode.Open, FileAccess.Read, FileShare.None))
    {
        try { await store.ApplySettingsAsync(store.Settings.Current, failedDirectory); Check(false, "Copy failure rolls back"); }
        catch (IOException) { Check(Directory.GetFiles(failedDirectory).Length == 0 && new AppSettings(root).InboxDirectory == movedDirectory && File.Exists(store.DataPath(snapshot[0])), "Interrupted relocation rolls back copied files and keeps old location"); }
    }
    if (OperatingSystem.IsLinux())
    {
        var privateDirectory = Path.Combine(root, "private-folder");
        AppPaths.CreateDataDirectory(privateDirectory);
        var mode = File.GetUnixFileMode(privateDirectory);
        Check((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) == 0, "New Linux app folders are user-private");
        var link = Path.Combine(root, "linked-folder");
        Directory.CreateSymbolicLink(link, privateDirectory);
        Check(StorageCapacity.Read(link).TotalBytes == StorageCapacity.Read(privateDirectory).TotalBytes, "Storage capacity resolves Linux symlink targets");
        var differentCase = Path.Combine(root, "RELOCATED-CACHE");
        await store.ApplySettingsAsync(store.Settings.Current, differentCase);
        Check(store.InboxDirectory == differentCase && store.Items.Length == snapshot.Length, "Linux cache paths remain case-sensitive");
        var previousXdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(root, "xdg"));
            Check(AppPaths.DefaultRoot == Path.Combine(root, "xdg", "pocketbridge"), "Linux default data path honors XDG_DATA_HOME");
        }
        finally { Environment.SetEnvironmentVariable("XDG_DATA_HOME", previousXdg); }
    }
    var legacyRoot = Path.Combine(root, "legacy-settings");
    Directory.CreateDirectory(legacyRoot);
    File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), JsonSerializer.Serialize(new AppLimits()));
    Check(new AppSettings(legacyRoot).Current == new AppLimits() && new AppSettings(legacyRoot).InboxDirectory == Path.Combine(legacyRoot, "inbox"), "Existing flat settings files remain compatible");
#if WINDOWS_UI_CHECKS
    Check(CleanupDialog.StorageUsage(128 * 1048576L, 512 * 1048576L) == "128,0 MB von 512 MB belegt", "Cleanup usage shows occupied and configured MB");
    Check(CleanupDialog.StorageUsage(1024 * 1048576L, 2048 * 1048576L) == "1,0 GB von 2 GB belegt", "Cleanup usage switches to GB");
    Check(CleanupDialog.StorageUsage(1, 512 * 1048576L).StartsWith("< 0,1 MB von 512 MB"), "Tiny persisted entries remain visible in usage display");
#endif
    var voices = new[] { new SpeechVoice("German", ["de-DE"]), new SpeechVoice("English", ["en-US"]) };
    Check(ReceiptSpeech.SelectVoice("de-AT", voices)?.Language == "de", "German Windows selects a German voice across regions");
    Check(ReceiptSpeech.SelectVoice("en-GB", voices)?.Language == "en" && ReceiptSpeech.SelectVoice("fr-FR", voices)?.Language == "en", "English Windows and unsupported languages select English");
    Check(ReceiptSpeech.SelectVoice("de-DE", [voices[1]])?.Language == "en", "Missing German voice falls back to English text and voice");
    Check(ReceiptSpeech.SelectVoice("fr-FR", [voices[0]]) == null, "Unsupported voice languages are not used to read mismatched text");
    Check(ReceiptSpeech.Text(new TransferReceipt(3, (long)(20.7 * 1048576)), "de") == "3 Dateien mit insgesamt 20,7 Megabyte empfangen.", "German announcement includes localized size");
    Check(ReceiptSpeech.Text(new TransferReceipt(1, 2L * 1073741824), "en") == "1 file with a total of 2.0 gigabytes received.", "English announcement handles singular and GB");
    Check(ReceiptSpeech.Text(new TransferReceipt(1, 100, true), "de").StartsWith("Teilweise empfangen:"), "Partial transfer cannot sound like complete success");
    Check(!string.IsNullOrWhiteSpace(ReceiptSpeech.WindowsLanguage()), "Windows display language detection works");
#if WINDOWS_UI_CHECKS
    UiChecks.Run();
#endif
    Console.WriteLine($"{checks} integration checks passed.");
}
finally
{
    // root is a unique directory created by this process under the system temp directory.
    if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
}
