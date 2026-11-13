using System.Globalization;
using System.Runtime.InteropServices;

namespace PocketBridge;

// Owned by the WPF STA thread. SAPI queues and plays speech asynchronously.
public sealed class ReceiptSpeaker(Action<string> reportError) : IDisposable
{
    private object? _voice;
    private SpeechSelection? _selection;
    private bool _unavailable;
    public string Description => EnsureVoice() ? $"Automatisch: {(_selection!.Language == "de" ? "Deutsch" : "Englisch")} · {_selection.Name}" : "Keine passende Windows-Stimme verfügbar. Deutsch oder Englisch in Windows installieren.";
    private static void Release(object? resource)
    {
        if (resource != null && Marshal.IsComObject(resource)) Marshal.FinalReleaseComObject(resource);
    }
    private bool EnsureVoice()
    {
        if (_voice != null) return true;
        if (_unavailable) return false;
        object? tokensObject = null;
        object? voiceObject = null;
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice") ?? throw new NotSupportedException("Windows-Sprachausgabe fehlt.");
            voiceObject = Activator.CreateInstance(type)!;
            dynamic voice = voiceObject;
            tokensObject = voice.GetVoices();
            dynamic tokens = tokensObject;
            var voices = new List<SpeechVoice>();
            for (var i = 0; i < (int)tokens.Count; i++)
            {
                object tokenObject = tokens.Item(i);
                try
                {
                    dynamic token = tokenObject;
                    var tags = new List<string>();
                    foreach (var id in ((string)token.GetAttribute("Language")).Split(';'))
                    {
                        if (!int.TryParse(id, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid)) continue;
                        try { tags.Add(CultureInfo.GetCultureInfo(lcid).Name); } catch (CultureNotFoundException) { }
                    }
                    voices.Add(new SpeechVoice((string)token.GetDescription(), tags.ToArray()));
                }
                finally { Release(tokenObject); }
            }
            _selection = ReceiptSpeech.SelectVoice(ReceiptSpeech.WindowsLanguage(), voices);
            if (_selection == null) { _unavailable = true; return false; }
            object chosen = tokens.Item(_selection.Index);
            try { voice.Voice = chosen; } finally { Release(chosen); }
            _voice = voiceObject; voiceObject = null;
            return true;
        }
        catch (Exception ex) when (ex is COMException or NotSupportedException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            _unavailable = true;
            reportError("Sprachausgabe nicht verfügbar: " + ex.Message);
            return false;
        }
        finally { Release(tokensObject); Release(voiceObject); }
    }
    public void Announce(TransferReceipt receipt, bool preview = false)
    {
        if (receipt.Count <= 0) return;
        if (!EnsureVoice()) { reportError("Keine deutsche oder englische Windows-Stimme verfügbar. Der Empfang funktioniert weiterhin."); return; }
        try
        {
            dynamic voice = _voice!;
            // Async + literal text; a preview replaces pending announcements.
            voice.Speak(ReceiptSpeech.Text(receipt, _selection!.Language), 1 | 16 | (preview ? 2 : 0));
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { reportError("Sprachausgabe fehlgeschlagen: " + ex.Message); }
    }
    public void Stop()
    {
        if (_voice == null) return;
        try { ((dynamic)_voice).Speak("", 1 | 2 | 16); }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
    }
    public void Dispose() { Stop(); Release(_voice); _voice = null; }
}
