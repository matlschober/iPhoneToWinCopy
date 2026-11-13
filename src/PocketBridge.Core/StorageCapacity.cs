using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace PocketBridge;

public sealed record DiskSpace(long AvailableBytes, long TotalBytes);

public static class StorageCapacity
{
    public static string Usage(long usedBytes, long capacityBytes)
    {
        var gb = capacityBytes >= 1073741824;
        var divisor = gb ? 1073741824d : 1048576d;
        var culture = System.Globalization.CultureInfo.GetCultureInfo("de-AT");
        var used = usedBytes > 0 && usedBytes / divisor < 0.1 ? "< 0,1" : (usedBytes / divisor).ToString("0.0", culture);
        var unit = gb ? "GB" : "MB";
        return $"{used} {unit} von {(capacityBytes / divisor).ToString("0.###", culture)} {unit} belegt";
    }
    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong freeForCaller, out ulong total, out ulong totalFree);

    public static DiskSpace Read(string directory)
    {
        var existing = Path.GetFullPath(directory);
        while (!Directory.Exists(existing)) existing = Path.GetDirectoryName(existing) ?? throw new IOException("Das gewählte Laufwerk ist nicht erreichbar.");
        if (OperatingSystem.IsWindows())
        {
            if (!GetDiskFreeSpaceEx(existing, out var available, out var total, out _))
                throw new IOException("Der freie Speicher am gewählten Speicherort konnte nicht ermittelt werden.", new Win32Exception(Marshal.GetLastWin32Error()));
            return new DiskSpace(checked((long)available), checked((long)total));
        }
        // Resolve symlinked ancestors before choosing the longest matching mount point.
        var root = Path.GetPathRoot(existing)!;
        var physical = root;
        foreach (var segment in existing[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            physical = Path.Combine(physical, segment);
            physical = new DirectoryInfo(physical).ResolveLinkTarget(true)?.FullName ?? physical;
        }
        foreach (var drive in DriveInfo.GetDrives().OrderByDescending(drive => drive.Name.Length))
        {
            var mount = Path.TrimEndingDirectorySeparator(drive.Name);
            if (physical != mount && mount != "/" && !physical.StartsWith(mount + "/", StringComparison.Ordinal)) continue;
            try { if (drive.IsReady) return new DiskSpace(drive.AvailableFreeSpace, drive.TotalSize); }
            catch (IOException) { }
        }
        throw new IOException("Der freie Speicher am gewählten Speicherort konnte nicht ermittelt werden.");
    }
    public static long RequiredBytes(long limit, long used, bool relocating) => relocating ? Math.Max(limit, used) : Math.Max(0, limit - used);
    public static void Validate(long limit, long used, bool relocating, long available)
    {
        var required = RequiredBytes(limit, used, relocating);
        if (required > available)
            throw new ArgumentException($"Am gewählten Speicherort sind nur {Format(available)} frei. Für dieses Limit{(relocating ? " und die Übernahme der vorhandenen Einträge" : "")} werden zusätzlich {Format(required)} benötigt. Bitte Limit verringern oder einen anderen Speicherort wählen.");
    }
    public static string Format(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.##} GB" : bytes > 0 && bytes < 10486 ? "< 0,01 MB" : $"{bytes / (1024d * 1024):0.##} MB";
}
