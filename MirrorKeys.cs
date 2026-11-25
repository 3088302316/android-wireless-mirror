using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MirrorKeys : IDisposable
{
    private delegate IntPtr KeyboardHook(int code, IntPtr message, IntPtr data);
    private readonly KeyboardHook callback;
    private readonly Process mirror;
    private readonly string serial;
    private readonly ConnectionProfile profile;
    private readonly Action<StreamPreset> changePreset;
    private readonly Action<AudioRoute> changeAudio;
    private IntPtr hook;
    private int settingsPage;
    private int rotating;
    private readonly HashSet<int> heldKeys = new HashSet<int>();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int type, KeyboardHook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(Keys key);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);

    public MirrorKeys(Process mirror, string serial, ConnectionProfile profile, Action<AudioRoute> changeAudio, Action<StreamPreset> changePreset)
    {
        this.mirror = mirror;
        this.serial = serial;
        this.profile = profile;
        this.changePreset = changePreset;
        this.changeAudio = changeAudio;
        callback = HandleKey;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
    }

    public async Task RunAsync(string deviceName, bool locked, Action<IntPtr> rememberConnection, Action<int> showSettings, MirrorMenuSignal menuSignal)
    {
        bool recorded = false;
        IntPtr window = IntPtr.Zero;
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var icon = WirelessMirror.LoadIcon())
        using (var menu = CreateMenu())
        using (var tray = new NotifyIcon { Icon = icon, Text = deviceName.Length > 50 ? deviceName.Substring(0, 50) : deviceName, ContextMenuStrip = menu, Visible = true })
        using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
        {
            tray.DoubleClick += delegate { settingsPage = 1; };
            timer.Tick += delegate
            {
                if (mirror.HasExited) { timer.Stop(); exited.TrySetResult(true); return; }
                uint owner;
                if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out owner) == 0 || owner != (uint)mirror.Id)
                    window = WirelessMirror.MirrorWindow(mirror);
                int nativePage = menuSignal.TakePage(window);
                if (nativePage != 0) settingsPage = nativePage;
                bool ready = !recorded && window != IntPtr.Zero;
                int page = settingsPage;
                settingsPage = 0;
                if (!ready && page == 0) return;
                timer.Stop();
                try
                {
                    if (ready)
                    {
                        recorded = true;
                        try { rememberConnection(window); }
                        catch (Exception historyError)
                        {
                            tray.ShowBalloonTip(4000, "手机投屏",
                                "本次连接记录未能保存：" + historyError.Message, ToolTipIcon.Warning);
                        }
                        if (locked)
                            tray.ShowBalloonTip(6000, "手机已锁屏",
                                "锁屏密码界面受 Android 保护，不能投屏。请在手机上解锁，画面会自动恢复。",
                                ToolTipIcon.Warning);
                    }
                    if (page != 0) showSettings(page - 1);
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, "手机投屏", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally { timer.Start(); }
            };
            timer.Start();
            await exited.Task;
        }
    }

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();
        ((ToolStripMenuItem)menu.Items.Add("设备管理", null, delegate { settingsPage = 1; })).ShortcutKeyDisplayString = "左 Alt+D";
        menu.Items.Add("快捷键", null, delegate { settingsPage = 2; });
        ((ToolStripMenuItem)menu.Items.Add("中文输入…", null, delegate { settingsPage = 4; })).ShortcutKeyDisplayString = "左 Alt+E";
        menu.Items.Add("连接设置", null, delegate { settingsPage = 3; });
        menu.Items.Add(new ToolStripSeparator());
        var rotate = new ToolStripMenuItem("旋转屏幕");
        rotate.ShortcutKeyDisplayString = "Ctrl+R";
        rotate.Click += delegate { Task.Run(delegate { Rotate(); }); };
        menu.Items.Add(rotate);
        menu.Items.Add("恢复自动旋转", null, delegate { Task.Run(delegate { AutoRotate(); }); });
        var quality = new ToolStripMenuItem("本次投屏画质");
        foreach (var preset in StreamPresets.All)
        {
            var choice = preset;
            var item = new ToolStripMenuItem(choice.Name);
            item.Checked = choice.Matches(profile);
            item.ToolTipText = choice.Description;
            item.Click += delegate { changePreset(choice); };
            quality.DropDownItems.Add(item);
        }
        menu.Items.Add(quality);
        var audioMenu = new ToolStripMenuItem("声音输出");
        foreach (var option in new[] { AudioRoute.Pc, AudioRoute.Phone, AudioRoute.Both, AudioRoute.None })
        {
            var route = option;
            var item = new ToolStripMenuItem(ConnectionProfile.AudioLabel(route));
            item.Checked = profile.Audio == route;
            item.Click += delegate { if (changeAudio != null) changeAudio(route); };
            audioMenu.DropDownItems.Add(item);
        }
        menu.Items.Add(audioMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("关闭投屏", null, delegate { if (!mirror.HasExited) mirror.CloseMainWindow(); });
        return menu;
    }

    private IntPtr HandleKey(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(hook, code, message, data);
        int key = Marshal.ReadInt32(data);
        bool up = message.ToInt64() == 0x101 || message.ToInt64() == 0x105;
        if (up && heldKeys.Remove(key)) return new IntPtr(1);
        if (up) return CallNextHookEx(hook, code, message, data);
        if (heldKeys.Contains(key)) return new IntPtr(1);
        if (key != (int)Keys.D && key != (int)Keys.E && key != (int)Keys.R)
            return CallNextHookEx(hook, code, message, data);

        uint foregroundProcess;
        GetWindowThreadProcessId(GetForegroundWindow(), out foregroundProcess);
        if (foregroundProcess != (uint)mirror.Id)
            return CallNextHookEx(hook, code, message, data);

        if (GetAsyncKeyState(Keys.ShiftKey) < 0 || GetAsyncKeyState(Keys.LWin) < 0 ||
            GetAsyncKeyState(Keys.RWin) < 0 || GetAsyncKeyState(Keys.RMenu) < 0)
            return CallNextHookEx(hook, code, message, data);

        bool leftAlt = GetAsyncKeyState(Keys.LMenu) < 0;
        bool ctrl = GetAsyncKeyState(Keys.ControlKey) < 0;
        if (leftAlt && !ctrl && (key == (int)Keys.D || key == (int)Keys.E))
            settingsPage = key == (int)Keys.E ? 4 : 1;
        else if (ctrl && !leftAlt && key == (int)Keys.R)
            Task.Run(delegate { Rotate(); });
        else
            return CallNextHookEx(hook, code, message, data);

        heldKeys.Add(key);
        return new IntPtr(1);
    }

    private void Rotate()
    {
        if (Interlocked.Exchange(ref rotating, 1) != 0) return;
        try { WirelessMirror.Rotate(serial); }
        catch (Exception error) { AppLog.Warn("手动旋转屏幕失败: " + error.Message); }
        finally { Interlocked.Exchange(ref rotating, 0); }
    }

    private void AutoRotate()
    {
        try { WirelessMirror.AutoRotate(serial); }
        catch (Exception error) { AppLog.Warn("恢复自动旋转失败: " + error.Message); }
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }
}
