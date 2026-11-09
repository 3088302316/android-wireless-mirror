using System;
using System.Diagnostics;
using System.Text;

internal static class AdbClient
{
    internal static string Run(string arguments, string input = null, int timeoutMs = 6500)
    {
        var start = new ProcessStartInfo(System.IO.Path.Combine(AppPaths.Root, "adb.exe"), arguments)
        {
            WorkingDirectory = AppPaths.Root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            RedirectStandardInput = input != null
        };
        var timer = Stopwatch.StartNew();
        try
        {
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("无法启动 adb.exe。");
                if (input != null)
                {
                    process.StandardInput.WriteLine(input);
                    process.StandardInput.Close();
                }
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch (Exception) { }
                    throw new TimeoutException("连接查询超时，请检查手机的无线调试和 Wi-Fi 后重试。");
                }
                string text = output.Result.Trim();
                string detail = error.Result.Trim();
                if (detail.Length > 0) text = text.Length == 0 ? detail : text + "\n" + detail;
                text = text.Trim();
                if (process.ExitCode != 0)
                    AppLog.Warn("adb exit " + process.ExitCode + ": " + Sanitize(arguments) + " -> " + Short(text));
                else if (timer.ElapsedMilliseconds > 2500)
                    AppLog.Warn("adb slow " + timer.ElapsedMilliseconds + "ms: " + Sanitize(arguments));
                return text;
            }
        }
        catch (Exception error)
        {
            AppLog.Error("adb failed: " + Sanitize(arguments), error);
            throw;
        }
    }

    private static string Sanitize(string arguments)
    {
        string text = (arguments ?? String.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length > 240 ? text.Substring(0, 240) + "…" : text;
    }

    private static string Short(string text)
    {
        string value = (text ?? String.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return value.Length > 180 ? value.Substring(0, 180) + "…" : value;
    }
}
