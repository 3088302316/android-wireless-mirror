using System;
using System.IO;

internal static class AppPaths
{
    internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AndroidWirelessMirror");
    internal static readonly string LogDirectory = Path.Combine(DataDirectory, "logs");
    internal static readonly string DevicesFile = Path.Combine(DataDirectory, "devices.json");
    internal static readonly string SettingsFile = Path.Combine(DataDirectory, "connection-settings.json");

    internal static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
    }

    internal static void EnsureLogDirectory()
    {
        Directory.CreateDirectory(LogDirectory);
    }
}
