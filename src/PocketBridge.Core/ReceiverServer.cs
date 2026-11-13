using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace PocketBridge;

public sealed class ReceiverServer(InboxStore store, int port = ReceiverServer.Port) : IAsyncDisposable
{
    public const int Port = 47831;
    private WebApplication? _app;
    private readonly SemaphoreSlim _uploads = new(1, 1);
    private volatile bool _paused;
    public event Action<TransferReceipt>? TransferReceived;
    public bool Paused { get => _paused; set => _paused = value; }
    public string Key { get; private set; } = LoadKey(store.RootDirectory);
    private static string LoadKey(string root)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "pairing.key");
        if (File.Exists(path))
        {
            var key = File.ReadAllText(path).Trim();
            if (key.Length == 48 && key.All(Uri.IsHexDigit)) return key;
        }
        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        File.WriteAllText(path, value);
        return value;
    }
    public void RotateKey()
    {
        var value = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(store.RootDirectory, "pairing.key"), value);
        Key = value;
    }
    public static string[] Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
        .Select(a => a.Address.ToString()).Distinct().OrderBy(a => a.StartsWith("192.168.") ? 0 : 1).ToArray();
    public async Task StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o =>
        {
            o.ListenAnyIP(port);
            // Each request receives its own immutable snapshot of the current limits.
            o.Limits.MaxRequestBodySize = null;
            o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
            o.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        });
        var app = builder.Build();
        app.Use(async (ctx, next) =>
        {
            var limits = store.Settings.Current;
            ctx.Items[typeof(AppLimits)] = limits;
            var remaining = store.RemainingBytes(limits);
            ctx.Features.Set<IFormFeature>(new FormFeature(ctx.Request, new FormOptions
            {
                MultipartBodyLengthLimit = Math.Max(1, remaining),
                // URL encoding can require three wire characters for each UTF-8 byte.
                ValueLengthLimit = (int)Math.Min(int.MaxValue, Math.Max(1, remaining * 3)),
                ValueCountLimit = int.MaxValue
            }));
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' blob: data:; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            // Reject requests addressed to a foreign hostname (DNS rebinding).
            if (!IPAddress.TryParse(ctx.Request.Host.Host, out _) && ctx.Request.Host.Host != "localhost")
            { ctx.Response.StatusCode = 400; return; }
            try { await next(ctx); }
            catch (UploadException ex) { ctx.Response.StatusCode = ex.Status; await ctx.Response.WriteAsJsonAsync(new { error = ex.Message }); }
            catch (BadHttpRequestException ex) { ctx.Response.StatusCode = ex.StatusCode; await ctx.Response.WriteAsJsonAsync(new { error = "Die Übertragung ist unvollständig oder ungültig." }); }
            catch (InvalidDataException) { ctx.Response.StatusCode = 507; await ctx.Response.WriteAsJsonAsync(new { error = $"Die Übertragung ist zu groß für den freien Empfangsspeicher ({StorageCapacity.Format(store.RemainingBytes(limits))}) oder enthält ungültige Formulardaten." }); }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { }
            catch (IOException) { ctx.Response.StatusCode = 507; await ctx.Response.WriteAsJsonAsync(new { error = "Speichern am PC fehlgeschlagen. Bitte Speicherplatz prüfen." }); }
        });
        app.MapGet("/", () => Asset("index.html", "text/html; charset=utf-8"));
        app.MapGet("/app.css", () => Asset("app.css", "text/css; charset=utf-8"));
        app.MapGet("/app.js", () => Asset("app.js", "text/javascript; charset=utf-8"));
        app.MapGet("/api/status", (HttpContext ctx) =>
        {
            Authorize(ctx);
            var limits = (AppLimits)ctx.Items[typeof(AppLimits)]!;
            return Results.Json(new { name = Environment.MachineName, paused = Paused, limits, remainingBytes = store.RemainingBytes(limits) });
        });
        app.MapPost("/api/receive", async (HttpContext ctx) =>
        {
            Authorize(ctx);
            if (Paused) throw new UploadException("Der Empfang am PC ist pausiert.", 503);
            if (!await _uploads.WaitAsync(0, ctx.RequestAborted)) throw new UploadException("Eine Übertragung läuft bereits. Bitte kurz warten.", 429);
            var accepted = new List<string>();
            long acceptedBytes = 0;
            var complete = false;
            try
            {
                if (Paused) throw new UploadException("Der Empfang am PC ist pausiert.", 503);
                var limits = (AppLimits)ctx.Items[typeof(AppLimits)]!;
                if (store.RemainingBytes(limits) == 0) throw new UploadException("Der Empfangsspeicher ist voll. Bitte Einträge löschen oder den Speicherrahmen erhöhen.", 507);
                if (!ctx.Request.HasFormContentType) throw new UploadException("Formulardaten erwartet (text oder files).");
                var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
                var text = form["text"].ToString();
                if (string.IsNullOrWhiteSpace(text) && form.Files.Count == 0) throw new UploadException("Bitte Text oder ein Foto auswählen.");
                var totalBytes = (string.IsNullOrWhiteSpace(text) ? 0 : Encoding.UTF8.GetByteCount(text)) + form.Files.Sum(file => file.Length);
                store.CheckQuota(totalBytes, limits);
                if (totalBytes > StorageCapacity.Read(store.InboxDirectory).AvailableBytes)
                    throw new UploadException("Auf dem Laufwerk des Zwischenspeichers ist nicht mehr genug freier Platz für diese Übertragung.", 507);
                // Validate every file before committing any record.
                var files = new List<(IFormFile File, string Mime, string Extension)>();
                foreach (var file in form.Files)
                {
                    if (file.Length == 0) throw new UploadException("Ein Foto ist leer.");
                    await using var source = file.OpenReadStream();
                    var header = new byte[32];
                    var length = await source.ReadAsync(header, ctx.RequestAborted);
                    var (mime, extension) = ImageType(header.AsSpan(0, length));
                    files.Add((file, mime, extension));
                }
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var bytes = Encoding.UTF8.GetBytes(text);
                    var title = new string(text.Split(['\r', '\n'])[0].Take(55).ToArray());
                    using var stream = new MemoryStream(bytes);
                    var item = await store.AddAsync(title, "text", "text/plain", stream, bytes.Length, ctx.RequestAborted, limits);
                    accepted.Add(item.Id);
                    acceptedBytes += item.Size;
                }
                foreach (var (file, mime, extension) in files)
                {
                    await using var stream = file.OpenReadStream();
                    var name = Path.GetFileNameWithoutExtension(InboxStore.CleanName(file.FileName)) + extension;
                    var item = await store.AddAsync(name, "image", mime, stream, file.Length, ctx.RequestAborted, limits);
                    accepted.Add(item.Id);
                    acceptedBytes += item.Size;
                }
                complete = true;
                return Results.Json(new { received = accepted.Count, message = "Am PC angekommen." });
            }
            catch (Exception ex) when (accepted.Count > 0 && ex is not OperationCanceledException)
            {
                return Results.Json(new { received = accepted.Count, error = "Ein Teil ist angekommen. " + (ex is UploadException ? ex.Message : "Der Rest konnte nicht gespeichert werden.") }, statusCode: 207);
            }
            finally
            {
                _uploads.Release();
                if (accepted.Count > 0)
                {
                    // Notification errors must never turn a persisted upload into an HTTP failure.
                    try { TransferReceived?.Invoke(new TransferReceipt(accepted.Count, acceptedBytes, !complete)); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                }
            }
        });
        _app = app;
        await app.StartAsync();
    }
    public async Task PauseAndDrainAsync()
    {
        Paused = true;
        await _uploads.WaitAsync();
        _uploads.Release();
    }
    private void Authorize(HttpContext ctx)
    {
        var supplied = ctx.Request.Headers["X-Pocket-Key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(Key)))
            throw new UploadException("Verbindung abgelaufen. Bitte den QR-Code am PC erneut scannen.", 401);
    }
    private static IResult Asset(string name, string type)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PocketBridge.Web." + name)!;
        return Results.Stream(stream, type);
    }
    private static (string, string) ImageType(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 255 && b[1] == 216 && b[2] == 255) return ("image/jpeg", ".jpg");
        if (b.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ("image/png", ".png");
        if (b.StartsWith("GIF87a"u8) || b.StartsWith("GIF89a"u8)) return ("image/gif", ".gif");
        if (b.StartsWith("BM"u8)) return ("image/bmp", ".bmp");
        if (b.StartsWith("II\x2A\0"u8) || b.StartsWith("MM\0\x2A"u8)) return ("image/tiff", ".tif");
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8)) return ("image/webp", ".webp");
        if (b.Length >= 12 && b.Slice(4, 4).SequenceEqual("ftyp"u8) && new[] { "heic", "heix", "hevc", "hevx", "mif1", "msf1" }.Contains(Encoding.ASCII.GetString(b.Slice(8, 4)))) return ("image/heic", ".heic");
        throw new UploadException("Dieses Dateiformat wird nicht unterstützt. Bitte ein Foto als JPEG, PNG, HEIC, WebP, GIF, BMP oder TIFF senden.", 415);
    }
    public async ValueTask DisposeAsync()
    {
        if (_app != null) { await _app.StopAsync(TimeSpan.FromSeconds(3)); await _app.DisposeAsync(); }
    }
}
