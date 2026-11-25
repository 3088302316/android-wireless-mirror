using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MirrorSessions : IDisposable
{
    private sealed class Session
    {
        public DeviceRecord Device;
        public ConnectionProfile Profile;
        public Process Process;
        public Form Recovery;
        public IntPtr Window;
        public Rectangle Bounds;
        public bool Maximized;
        public bool Counted;
        public bool HasWindow;
        public bool RestartRequested;
    }

    private readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
    private readonly DeviceDirectory directory;
    private readonly DeviceSettings center;
    private readonly System.Windows.Forms.Timer activation = new System.Windows.Forms.Timer { Interval = 150 };
    private bool closing;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr window, string title);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    public MirrorSessions(DeviceDirectory directory, DeviceSettings center, EventWaitHandle openSettings)
    {
        this.directory = directory;
        this.center = center;
        center.OpenRequested += Open;
        center.RenameApplied += UpdateTitle;
        center.SettingsSaved += ApplySavedProfile;
        center.StreamStatus = Status;
        center.FormClosed += delegate { Dispose(); };
        int ticks = 0;
        activation.Tick += delegate
        {
            if (openSettings.WaitOne(0)) ShowCenter();
            if (++ticks < 5) return;
            ticks = 0;
            foreach (var session in sessions.Values) RememberWindow(session);
        };
        activation.Start();
    }

    private string Status(DeviceRecord device)
    {
        Session current;
        if (device.Id == null || !sessions.TryGetValue(device.Id, out current)) return null;
        if (current.Recovery != null) return "正在重连";
        return current.HasWindow ? "投屏中" : "正在打开";
    }

    private void UpdateTitle(DeviceRecord device)
    {
        Session current;
        if (device.Id == null || !sessions.TryGetValue(device.Id, out current)) return;
        current.Device.Alias = device.Alias;
        if (current.Process != null && !current.Process.HasExited)
        {
            IntPtr window = WirelessMirror.MirrorWindow(current.Process);
            if (window != IntPtr.Zero) SetWindowText(window, WirelessMirror.Caption(current.Device));
        }
    }

    private async void Open(DeviceRecord device)
    {
        if (closing) return;
        if (device.Id == null)
        {
            center.ReportStream("暂时无法确认「" + device.Name + "」的身份。请刷新设备后再试。", WindowTheme.Amber);
            return;
        }

        Session existing;
        if (sessions.TryGetValue(device.Id, out existing))
        {
            if (existing.Recovery != null) WirelessMirror.FocusWindow(existing.Recovery.Handle);
            else if (existing.Process != null && !existing.Process.HasExited)
            {
                WirelessMirror.FocusWindow(WirelessMirror.MirrorWindow(existing.Process));
            }
            return;
        }

        var session = new Session { Device = device, Profile = ConnectionProfile.Load() };
        sessions.Add(device.Id, session);
        center.RefreshStreamStatus();
        try { await Run(session); }
        catch (Exception error)
        {
            AppLog.Error("投屏异常: " + device.Name, error);
            if (!closing) center.ReportStream(device.Name + " · " + error.Message, WindowTheme.Red);
        }
        finally
        {
            sessions.Remove(device.Id);
            if (!closing)
            {
                center.RefreshStreamStatus();
                if (!sessions.Values.Any(item => item.Window != IntPtr.Zero && item.Window == GetForegroundWindow()))
                    ShowCenter();
            }
        }
    }

    private async Task Run(Session session)
    {
        int retries = 2;
        bool softwareDecoding = false;
        while (!closing)
        {
            var device = session.Device;
            string serial = device.Serial;
            bool locked = false;
            bool valid = await Task.Run(delegate
            {
                bool connected = serial != null && DeviceDirectory.IsWirelessSerial(serial) &&
                    WirelessMirror.Adb("-s " + serial + " get-state") == "device" &&
                    DeviceDirectory.ReadId(serial) == device.Id;
                if (connected) locked = WirelessMirror.IsLocked(serial);
                return connected;
            });
            if (closing) return;
            if (!valid)
            {
                center.ReportStream(device.Name + " 已离线或连接地址已变化，请刷新后再试。", WindowTheme.Amber);
                return;
            }

            int exitCode;
            bool closeRequested;
            var elapsed = Stopwatch.StartNew();
            using (var signal = new MirrorMenuSignal())
            using (var mirror = WirelessMirror.StartMirror(device, session.Profile, signal, locked, softwareDecoding))
            using (var telemetry = session.Profile.ShowStats ? new MirrorTelemetry(mirror, serial) : null)
            using (var keys = new MirrorKeys(mirror, serial, session.Profile, delegate(AudioRoute route)
            {
                if (session.Profile.Audio == route) return;
                var updated = session.Profile.Copy();
                updated.Audio = route;
                try
                {
                    var defaults = ConnectionProfile.Load();
                    defaults.Audio = route;
                    defaults.Save();
                }
                catch (Exception error)
                {
                    AppLog.Warn("保存声音输出设置失败: " + error.Message);
                    return;
                }
                ApplySavedProfile(device, updated);
            }, delegate(StreamPreset preset)
            {
                var updated = session.Profile.Copy();
                preset.Apply(updated);
                ApplySavedProfile(device, updated);
            }))
            {
                session.Process = mirror;
                try
                {
                    await keys.RunAsync(device.Name, locked, delegate(IntPtr window)
                    {
                        session.HasWindow = true;
                        if (telemetry != null) telemetry.Start();
                        session.Window = window;
                        if (!RestoreWindow(session)) PositionWindow(window);
                        RememberWindow(session);
                        if (!session.Counted)
                        {
                            directory.RememberConnection(device);
                            session.Counted = true;
                        }
                        center.RefreshStreamStatus(device);
                    }, delegate(int page)
                    {
                        if (page == 0) { ShowCenter(); return; }
                        using (var settings = new DeviceSettings(directory, new List<DeviceRecord>(), page, device, session.Profile))
                        {
                            settings.OpenRequested += Open;
                            settings.RenameApplied += UpdateTitle;
                            settings.StreamStatus = Status;
                            if (settings.ShowDialog(center) != DialogResult.OK || closing) return;
                            var chosen = settings.SavedProfile;
                            if (chosen == null) return;
                            ApplySavedProfile(device, chosen);
                        }
                    }, signal);
                    exitCode = mirror.ExitCode;
                    closeRequested = signal.CloseRequested;
                }
                finally
                {
                    session.Window = IntPtr.Zero;
                    session.Process = null;
                    session.HasWindow = false;
                    if (!mirror.HasExited) Stop(mirror);
                }
            }

            AppLog.Info("scrcpy 退出: " + device.Name + " / " + exitCode + " / " + elapsed.ElapsedMilliseconds + "ms");
            if (closing) return;
            if (session.RestartRequested)
            {
                session.RestartRequested = false;
                retries = 2;
                center.RefreshStreamStatus();
                continue;
            }
            if (elapsed.Elapsed >= TimeSpan.FromMinutes(1)) retries = 2;
            if (closeRequested || exitCode == 0) return;

            if (exitCode == unchecked((int)0xC0000005) && session.Profile.AutoReconnect && !softwareDecoding)
            {
                softwareDecoding = true;
                AppLog.Warn("投屏进程异常退出，改用软件解码恢复当前会话: " + device.Name);
                center.ReportStream(device.Name + " 的播放进程意外退出，正在使用软件解码恢复画面。", WindowTheme.Amber);
                continue;
            }

            if (exitCode == 2 && session.Profile.AutoReconnect && retries > 0)
            {
                retries--;
                using (var recovery = new ReconnectDialog(directory, device, serial))
                {
                    session.Recovery = recovery;
                    center.RefreshStreamStatus();
                    var finished = new TaskCompletionSource<DeviceRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
                    recovery.FormClosed += delegate { finished.TrySetResult(recovery.Recovered); };
                    recovery.Show(center);
                    var found = await finished.Task;
                    session.Recovery = null;
                    if (found == null || closing) return;
                    session.Device = found;
                    continue;
                }
            }

            if (exitCode == 2)
                center.ReportStream(device.Name + " 的无线连接已中断。", WindowTheme.Amber);
            else
                center.ReportStream(device.Name + " 播放进程已退出（0x" + exitCode.ToString("X8") +
                    "）。可在连接设置中打开日志。", WindowTheme.Red);
            return;
        }
    }

    private static void RememberWindow(Session session)
    {
        IntPtr window = session.Window;
        uint owner;
        if (window == IntPtr.Zero || session.Process == null || IsIconic(window) ||
            GetWindowThreadProcessId(window, out owner) == 0 || owner != (uint)session.Process.Id) return;
        WindowRect native;
        if (!GetWindowRect(window, out native)) return;
        var bounds = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
        if (bounds.Width <= 0 || bounds.Height <= 0 || bounds == Screen.FromHandle(window).Bounds) return;
        session.Maximized = IsZoomed(window);
        if (!session.Maximized) session.Bounds = bounds;
    }

    private static bool RestoreWindow(Session session)
    {
        if (session.Bounds.IsEmpty) return false;
        Rectangle previous = session.Bounds;
        Rectangle work = Screen.FromRectangle(previous).WorkingArea;
        double scale = Math.Min(1, Math.Min((double)work.Width / previous.Width,
                                             (double)work.Height / previous.Height));
        int width = Math.Max(1, (int)(previous.Width * scale));
        int height = Math.Max(1, (int)(previous.Height * scale));
        int x = Math.Max(work.Left, Math.Min(previous.X, work.Right - width));
        int y = Math.Max(work.Top, Math.Min(previous.Y, work.Bottom - height));
        bool restored = SetWindowPos(session.Window, IntPtr.Zero, x, y, width, height, 0x14);
        if (restored && session.Maximized) ShowWindowAsync(session.Window, 3);
        return restored;
    }

    private void PositionWindow(IntPtr window)
    {
        WindowRect bounds;
        if (!GetWindowRect(window, out bounds)) return;
        Rectangle work = Screen.FromHandle(window).WorkingArea;
        int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
        var occupied = new List<Rectangle>();
        foreach (var other in sessions.Values)
        {
            var process = other.Process;
            if (!other.HasWindow || process == null || process.HasExited) continue;
            IntPtr handle = WirelessMirror.MirrorWindow(process);
            WindowRect rect;
            if (handle != window && GetWindowRect(handle, out rect))
                occupied.Add(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
        }
        if (occupied.Count == 0) return;
        var original = new Rectangle(bounds.Left, bounds.Top, width, height);
        if (!occupied.Any(area => area.IntersectsWith(original))) return;

        int y = Math.Max(work.Top, Math.Min(work.Bottom - height, bounds.Top));
        foreach (int x in occupied.SelectMany(area => new[] { area.Right + 12, area.Left - width - 12 }))
        {
            if (x < work.Left || x + width > work.Right) continue;
            var target = new Rectangle(x, y, width, height);
            if (occupied.Any(area => area.IntersectsWith(target))) continue;
            SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, 0x15);
            return;
        }
        int offset = 30 * occupied.Count;
        int cascadeX = Math.Max(work.Left, Math.Min(work.Right - width, bounds.Left + offset));
        int cascadeY = Math.Max(work.Top, Math.Min(work.Bottom - height, y + offset));
        SetWindowPos(window, IntPtr.Zero, cascadeX, cascadeY, 0, 0, 0x15);
    }

    private async void ApplySavedProfile(DeviceRecord device, ConnectionProfile profile)
    {
        if (closing || device == null || device.Id == null || profile == null) return;
        Session session;
        if (!sessions.TryGetValue(device.Id, out session)) return;
        var runningProfile = session.Profile;
        bool restart = runningProfile.Options != profile.Options;
        session.Profile = profile.Copy();
        var process = session.Process;
        if (!restart || session.RestartRequested || process == null || process.HasExited) return;
        RememberWindow(session);
        session.RestartRequested = true;
        AppLog.Info("应用新的投屏参数: " + device.Name);
        try
        {
            if (process.CloseMainWindow()) await Task.Delay(1200);
            if (session.Process == process && !process.HasExited) process.Kill();
        }
        catch (InvalidOperationException) { /* Already exited. */ }
        catch (Exception error)
        {
            if (session.Process == process)
            {
                session.Profile = runningProfile;
                session.RestartRequested = false;
                if (!closing) center.ReportStream("无法重启投屏，请关闭该窗口后再打开。", WindowTheme.Amber);
            }
            AppLog.Error("重新启动投屏失败", error);
        }
    }

    private void ShowCenter()
    {
        if (closing || center.IsDisposed) return;
        center.ShowDevicePage();
        WirelessMirror.FocusWindow(center.Handle);
    }

    private static void Stop(Process mirror)
    {
        try
        {
            if (mirror.HasExited) return;
            if (mirror.CloseMainWindow() && mirror.WaitForExit(1200)) return;
            if (!mirror.HasExited) mirror.Kill();
            mirror.WaitForExit(1500);
        }
        catch (Exception error) { AppLog.Warn("停止投屏窗口失败: " + error.Message); }
    }

    public void Dispose()
    {
        if (closing) return;
        closing = true;
        activation.Dispose();
        foreach (var session in sessions.Values.ToArray())
        {
            if (session.Process != null) Stop(session.Process);
            if (session.Recovery != null && !session.Recovery.IsDisposed) session.Recovery.Close();
        }
    }
}
