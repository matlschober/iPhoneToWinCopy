namespace PocketBridge;

public static class AppPaths
{
    public static void CreateDataDirectory(string directory)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static string DefaultRoot
    {
        get
        {
            if (OperatingSystem.IsWindows()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PocketBridge");
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var data = !string.IsNullOrEmpty(xdg) && Path.IsPathFullyQualified(xdg) ? xdg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(data, "pocketbridge");
        }
    }
}
