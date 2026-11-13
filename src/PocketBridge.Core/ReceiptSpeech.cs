using System.Globalization;
using System.Runtime.InteropServices;

namespace PocketBridge;

public sealed record TransferReceipt(int Count, long TotalBytes, bool IsPartial = false);
public sealed record SpeechVoice(string Name, string[] Languages);
public sealed record SpeechSelection(int Index, string Language, string Name);

public static class ReceiptSpeech
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserPreferredUILanguages(uint flags, out uint count, [Out] char[]? languages, ref uint size);

    public static string WindowsLanguage()
    {
        if (!OperatingSystem.IsWindows()) return SystemLanguage();
        uint size = 0;
        if (GetUserPreferredUILanguages(8, out _, null, ref size) && size is > 1 and < 65536)
        {
            var buffer = new char[size];
            if (GetUserPreferredUILanguages(8, out _, buffer, ref size))
                return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? CultureInfo.CurrentUICulture.Name;
        }
        return string.IsNullOrWhiteSpace(CultureInfo.CurrentUICulture.Name) ? "en-US" : CultureInfo.CurrentUICulture.Name;
    }
    public static string SystemLanguage()
    {
        if (OperatingSystem.IsWindows()) return WindowsLanguage();
        foreach (var variable in new[] { "LANGUAGE", "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var value = Environment.GetEnvironmentVariable(variable)?.Split(':')[0].Split('.')[0].Split('@')[0].Replace('_', '-');
            if (string.IsNullOrEmpty(value) || value is "C" or "POSIX") continue;
            try { return CultureInfo.GetCultureInfo(value).Name; } catch (CultureNotFoundException) { }
        }
        return string.IsNullOrWhiteSpace(CultureInfo.CurrentUICulture.Name) ? "en-US" : CultureInfo.CurrentUICulture.Name;
    }
    public static SpeechSelection? SelectVoice(string windowsLanguage, IReadOnlyList<SpeechVoice> voices)
    {
        var language = windowsLanguage.Split('-')[0].Equals("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en";
        foreach (var wanted in new[] { language, "en" }.Distinct())
        {
            var exact = Enumerable.Range(0, voices.Count).FirstOrDefault(i => voices[i].Languages.Contains(windowsLanguage, StringComparer.OrdinalIgnoreCase) && windowsLanguage.StartsWith(wanted + "-", StringComparison.OrdinalIgnoreCase), -1);
            var index = exact >= 0 ? exact : Enumerable.Range(0, voices.Count).FirstOrDefault(i => voices[i].Languages.Any(tag => tag.Split('-')[0].Equals(wanted, StringComparison.OrdinalIgnoreCase)), -1);
            if (index >= 0) return new SpeechSelection(index, wanted, voices[index].Name);
        }
        return null;
    }
    public static string Text(TransferReceipt receipt, string language)
    {
        var german = language == "de";
        var culture = CultureInfo.GetCultureInfo(german ? "de-DE" : "en-US");
        var gigabytes = receipt.TotalBytes >= 1024L * 1024 * 1024;
        var size = receipt.TotalBytes / (gigabytes ? 1024d * 1024 * 1024 : 1024d * 1024);
        var number = size > 0 && size < 0.1 ? (german ? "weniger als 0,1" : "less than 0.1") : size.ToString("0.0", culture);
        var unit = gigabytes ? (german ? "Gigabyte" : "gigabytes") : (german ? "Megabyte" : "megabytes");
        if (german)
            return $"{(receipt.IsPartial ? "Teilweise empfangen: " : "")}{receipt.Count} {(receipt.Count == 1 ? "Datei" : "Dateien")} mit insgesamt {number} {unit} empfangen.";
        return $"{(receipt.IsPartial ? "Partially received: " : "")}{receipt.Count} {(receipt.Count == 1 ? "file" : "files")} with a total of {number} {unit} received.";
    }
}

