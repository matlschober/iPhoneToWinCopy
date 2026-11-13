using System.Diagnostics;
using System.Threading.Channels;

namespace PocketBridge.Desktop;

internal sealed class LinuxSpeaker : IDisposable
{
    private readonly Action<string> _report;
    private readonly string? _program;
    private readonly Channel<(TransferReceipt Receipt, int Generation)> _queue = Channel.CreateUnbounded<(TransferReceipt, int)>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly CancellationTokenSource _shutdown = new();
    private Process? _current;
    private int _generation;
    public string Description => _program == null ? "Sprachansage benötigt spd-say oder espeak-ng (optional)." : $"Automatisch: {(Language == "de" ? "Deutsch" : "Englisch")} · {Path.GetFileName(_program)}";
    private static string Language => ReceiptSpeech.SystemLanguage().StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
    public LinuxSpeaker(Action<string> report)
    {
        _report = report;
        _program = OperatingSystem.IsLinux() ? Find("espeak-ng") ?? Find("spd-say") : null;
        _ = Task.Run(Work);
    }
    private static string? Find(string executable) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Where(Path.IsPathFullyQualified).Select(path => Path.Combine(path, executable)).FirstOrDefault(File.Exists);
    public void Announce(TransferReceipt receipt, bool preview = false)
    {
        if (_program == null) { _report(Description); return; }
        if (preview) Stop();
        _queue.Writer.TryWrite((receipt, Volatile.Read(ref _generation)));
    }
    private async Task Work()
    {
        try
        {
            await foreach (var entry in _queue.Reader.ReadAllAsync(_shutdown.Token))
            {
                if (entry.Generation != Volatile.Read(ref _generation) || _program == null) continue;
                var start = new ProcessStartInfo(_program) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, RedirectStandardInput = true, CreateNoWindow = true };
                if (Path.GetFileName(_program) == "spd-say")
                {
                    foreach (var argument in new[] { "--wait", "--language", Language, ReceiptSpeech.Text(entry.Receipt, Language) }) start.ArgumentList.Add(argument);
                }
                else
                {
                    foreach (var argument in new[] { "-v", Language, "--stdin" }) start.ArgumentList.Add(argument);
                }
                try
                {
                    using var process = Process.Start(start)!;
                    _current = process;
                    if (entry.Generation != Volatile.Read(ref _generation)) process.Kill(true);
                    if (Path.GetFileName(_program) != "spd-say") await process.StandardInput.WriteAsync(ReceiptSpeech.Text(entry.Receipt, Language));
                    process.StandardInput.Close();
                    var errors = process.StandardError.ReadToEndAsync();
                    var output = process.StandardOutput.ReadToEndAsync();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                    timeout.CancelAfter(TimeSpan.FromMinutes(1));
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); }
                    await output;
                    if (process.HasExited && process.ExitCode != 0 && entry.Generation == Volatile.Read(ref _generation)) _report("Sprachansage fehlgeschlagen: " + (await errors).Trim());
                }
                catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException) { _report("Sprachansage: " + ex.Message); }
                finally { _current = null; }
            }
        }
        catch (OperationCanceledException) { }
    }
    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        try { if (_current is { HasExited: false } current) current.Kill(true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
    public void Dispose() { Stop(); _queue.Writer.TryComplete(); _shutdown.Cancel(); }
}
