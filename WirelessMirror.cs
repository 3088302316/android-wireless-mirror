using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class WirelessMirror
{
    private const string Title = "安卓无线投屏";
    private static readonly string Root = AppPaths.Root;

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [STAThread]
    private static void Main()
    {
        EnvironmentSanitizer.RemoveCaseDuplicates();
        Application.EnableVisualStyles();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs args)
        {
            AppLog.Error("UI 线程异常", args.Exception);
            try
            {
                MessageBox.Show("程序遇到异常，但已保住当前进程。\n日志：" + AppLog.DirectoryPath +
                    "\n\n" + args.Exception.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception) { }
        };
        AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args)
        {
            AppLog.Error("未处理异常", args.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += delegate(object sender, UnobservedTaskExceptionEventArgs args)
        {
            AppLog.Error("后台任务异常", args.Exception);
            args.SetObserved();
        };
        AppLog.Info("启动 AndroidWirelessMirror " + Application.ProductVersion + " / " +
            Environment.OSVersion.VersionString + " / " + Environment.Is64BitProcess);
        AntdUI.Config.IsDark = true;
        using (var singleInstance = new Mutex(false, "Local\\AndroidWirelessMirrorPortable"))
        using (var openSettings = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AndroidWirelessMirrorSettings"))
        {
            if (!singleInstance.WaitOne(0))
            {
                if (!FocusSettings()) openSettings.Set();
                return;
            }
            try
            {
                openSettings.Reset();
                var directory = new DeviceDirectory();
                using (var center = new DeviceSettings(directory, new List<DeviceRecord>(), 0))
                using (var sessions = new MirrorSessions(directory, center, openSettings))
                    Application.Run(center);
            }
            catch (Exception error)
            {
                AppLog.Error("主循环异常", error);
                MessageBox.Show("程序遇到错误：" + error.Message + "\n日志：" + AppLog.DirectoryPath,
                    Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                AppLog.Info("退出");
                singleInstance.ReleaseMutex();
            }
        }
    }

    internal static bool IsLocked(string serial)
    {
        try
        {
            string output = Adb("-s " + serial + " shell dumpsys window policy", null, 4000);
            if (String.IsNullOrEmpty(output)) return false;
            if (output.IndexOf("KeyguardServiceDelegate", StringComparison.Ordinal) >= 0 &&
                Regex.IsMatch(output, @"(?m)^\s*showing=true\s*$")) return true;
            return Regex.IsMatch(output, @"(?m)^\s*mDreamingLockscreen=true\s*$") ||
                   Regex.IsMatch(output, @"(?m)^\s*mShowingLockscreen=true\s*$");
        }
        catch (Exception error)
        {
            AppLog.Warn("读取锁屏状态失败: " + error.Message);
            return false;
        }
    }

    internal static Process StartMirror(DeviceRecord device, ConnectionProfile profile, MirrorMenuSignal menuSignal)
    {
        bool locked = IsLocked(device.Serial);
        // If the phone is locked, keep its screen on so the user can unlock it
        // on the phone. The secure keyguard itself still cannot be captured.
        string options = "--serial=" + device.Serial + (locked ? " " : " --turn-screen-off ") +
            "--window-height=800 --video-codec=h264 --hwdec=auto " +
            profile.Options + " --window-title=\"" + Caption(device) + "\"";
        AppLog.Info("开始投屏: " + device.Name + " / " + device.Serial + " / " +
            (locked ? "锁屏" : "已解锁") + " / " + profile.Options);
        var start = StartInfo("scrcpy.exe", options);
        start.RedirectStandardError = true;
        start.StandardErrorEncoding = Encoding.UTF8;
        start.EnvironmentVariables["ANDROID_MIRROR_MENU_WINDOW"] =
            menuSignal.Handle.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var process = Process.Start(start);
        if (process == null) throw new InvalidOperationException("scrcpy.exe 没有返回进程。");
        try { process.PriorityClass = ProcessPriorityClass.AboveNormal; }
        catch (Exception error) { AppLog.Warn("设置 scrcpy 优先级失败: " + error.Message); }
        process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args)
        {
            if (String.IsNullOrEmpty(args.Data)) return;
            if (args.Data.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                args.Data.IndexOf("WARN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                args.Data.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0)
                AppLog.Warn("scrcpy: " + args.Data);
        };
        process.BeginErrorReadLine();
        return process;
    }

    internal static void Rotate(string serial)
    {
        string current = (Adb("-s " + serial + " shell settings get system user_rotation") ?? String.Empty).Trim();
        int rotation;
        if (!Int32.TryParse(current, out rotation)) rotation = 0;
        int next = (rotation % 2 == 0) ? 1 : 0;
        Adb("-s " + serial + " shell settings put system accelerometer_rotation 0", null, 4000);
        Adb("-s " + serial + " shell settings put system user_rotation " +
            next.ToString(System.Globalization.CultureInfo.InvariantCulture), null, 4000);
        AppLog.Info("手动旋转屏幕: " + serial + " / " + rotation + " -> " + next);
    }

    internal static void AutoRotate(string serial)
    {
        Adb("-s " + serial + " shell settings put system accelerometer_rotation 1", null, 4000);
        AppLog.Info("恢复手机自动旋转: " + serial);
    }

    internal static string Caption(DeviceRecord device)
    {
        return device.Name.Replace('"', '”').Replace('\r', ' ').Replace('\n', ' ') + " · " + Title;
    }

    public static Icon LoadIcon()
    {
        return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
    }

    internal static IntPtr MirrorWindow(Process process)
    {
        process.Refresh();
        try { return process.HasExited ? IntPtr.Zero : process.MainWindowHandle; }
        catch (InvalidOperationException)
        {
            if (process.HasExited) return IntPtr.Zero;
            throw;
        }
    }

    internal static void FocusWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) return;
        if (IsIconic(window)) ShowWindowAsync(window, 9);
        SetForegroundWindow(window);
    }

    private static bool FocusSettings()
    {
        foreach (string name in new[] { "安卓无线投屏" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                if (process.MainWindowTitle != Title + " · 设备中心" || process.MainWindowHandle == IntPtr.Zero) continue;
                if (!String.Equals(process.MainModule.FileName, Path.Combine(Root, name + ".exe"), StringComparison.OrdinalIgnoreCase)) continue;
                FocusWindow(process.MainWindowHandle);
                return true;
            }
        }
        return false;
    }

    private static ProcessStartInfo StartInfo(string file, string arguments)
    {
        return new ProcessStartInfo(Path.Combine(Root, file), arguments)
        {
            WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true
        };
    }

    public static string Adb(string arguments, string input = null, int timeoutMs = 6500)
    {
        return AdbClient.Run(arguments, input, timeoutMs);
    }

    private static void PairAddress(string address, string code)
    {
        string result = Adb("pair " + address, code, 8000);
        if (result.IndexOf("Successfully paired", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            AppLog.Info("六位码配对成功: " + address);
            return;
        }
        AppLog.Warn("六位码配对失败: " + address + " / " + Short(result));
        string detail = Short(result);
        throw new InvalidOperationException(
            "配对失败。请保持手机配对弹窗打开，核对地址和六位配对码。" +
            (detail.Length == 0 ? "" : " ADB 返回：" + detail));
    }

    private static string Short(string message)
    {
        string text = (message ?? String.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        while (text.Contains("  ")) text = text.Replace("  ", " ");
        return text.Length > 180 ? text.Substring(0, 180) + "…" : text;
    }

    public static bool PairNew(IWin32Window owner)
    {
        using (var dialog = new AddressInputDialog(PairAddress))
            return dialog.ShowDialog(owner) == DialogResult.OK;
    }
}
