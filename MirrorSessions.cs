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
        public bool HasWindow;
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

    public MirrorSessions(DeviceDirectory directory, DeviceSettings center, EventWaitHandle openSettings)
    {
        this.directory = directory;
        this.center = center;
        center.OpenRequested += Open;
        center.RenameApplied += UpdateTitle;
        center.StreamStatus = Status;
        center.FormClosed += delegate { Dispose(); };
        activation.Tick += delegate { if (openSettings.WaitOne(0)) ShowCenter(); };
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
                ShowCenter();
            }
        }
    }

    private async Task Run(Session session)
    {
        int retries = 2;
        while (!closing)
        {
            var device = session.Device;
            string serial = device.Serial;
            bool valid = await Task.Run(delegate
            {
                return serial != null && DeviceDirectory.IsWirelessSerial(serial) &&
                    WirelessMirror.Adb("-s " + serial + " get-state") == "device" &&
                    DeviceDirectory.ReadId(serial) == device.Id;
            });
            if (closing) return;
            if (!valid)
            {
                center.ReportStream(device.Name + " 已离线或连接地址已变化，请刷新后再试。", WindowTheme.Amber);
                return;
            }

            ConnectionProfile replacement = null;
            int exitCode;
            var elapsed = Stopwatch.StartNew();
            using (var signal = new MirrorMenuSignal())
            using (var mirror = WirelessMirror.StartMirror(device, session.Profile, signal))
            using (var telemetry = new MirrorTelemetry(mirror, serial))
            using (var keys = new MirrorKeys(mirror, serial, session.Profile.Audio, delegate(AudioRoute route)
            {
                if (session.Profile.Audio == route) return;
                var updated = session.Profile.Copy();
                updated.Audio = route;
                try { updated.Save(); }
                catch (Exception error)
                {
                    AppLog.Warn("保存声音输出设置失败: " + error.Message);
                    return;
                }
                replacement = updated;
                Stop(mirror);
            }))
            {
                session.Process = mirror;
                try
                {
                    await keys.RunAsync(device.Name, delegate(IntPtr window)
                    {
                        session.HasWindow = true;
                        telemetry.Start();
                        PositionWindow(window);
                        directory.RememberConnection(device);
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
                            if (chosen.Options == session.Profile.Options)
                            {
                                session.Profile = chosen;
                                WirelessMirror.FocusWindow(WirelessMirror.MirrorWindow(mirror));
                                return;
                            }
                            if (!mirror.HasExited && !mirror.CloseMainWindow())
                                throw new InvalidOperationException("无法关闭当前窗口，请稍后重试。");
                            replacement = chosen;
                        }
                    }, signal);
                    exitCode = mirror.ExitCode;
                }
                finally
                {
                    session.Process = null;
                    session.HasWindow = false;
                    if (!mirror.HasExited) Stop(mirror);
                }
            }

            AppLog.Info("scrcpy 退出: " + device.Name + " / " + exitCode + " / " + elapsed.ElapsedMilliseconds + "ms");
            if (closing) return;
            if (replacement != null)
            {
                session.Profile = replacement;
                retries = 2;
                center.RefreshStreamStatus();
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

            if (exitCode != 0 && elapsed.Elapsed < TimeSpan.FromSeconds(6))
                center.ReportStream(device.Name + " 投屏启动失败，请确认无线调试已开启。", WindowTheme.Red);
            return;
        }
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
