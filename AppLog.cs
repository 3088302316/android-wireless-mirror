using System;
using System.IO;
using System.Text;

internal static class AppLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new object();
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    internal static string DirectoryPath { get { return AppPaths.LogDirectory; } }

    internal static void Info(string message) { Write("INFO", message); }
    internal static void Warn(string message) { Write("WARN", message); }

    internal static void Error(string context, Exception error)
    {
        if (error == null) { Write("ERROR", context); return; }
        Write("ERROR", context + "\r\n" + error.GetType().FullName + ": " + error.Message +
            "\r\n" + error.StackTrace);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureLogDirectory();
                string path = Path.Combine(AppPaths.LogDirectory, "app.log");
                RotateIfNeeded(path);
                File.AppendAllText(path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " +
                    (message ?? String.Empty) + Environment.NewLine, Utf8);
            }
        }
        catch (Exception) { /* Logging must never break the app. */ }
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxBytes) return;
        for (int index = 4; index >= 1; index--)
        {
            string source = path + "." + index;
            string destination = path + "." + (index + 1);
            if (!File.Exists(source)) continue;
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(source, destination);
        }
        if (File.Exists(path)) File.Move(path, path + ".1");
    }
}
