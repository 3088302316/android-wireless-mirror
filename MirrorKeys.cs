using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MirrorKeys : IDisposable
{
    private delegate IntPtr KeyboardHook(int code, IntPtr message, IntPtr data);
    private readonly KeyboardHook callback;
    private readonly Process mirror;
    private IntPtr hook;
    private int settingsPage;
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
    private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);

    public MirrorKeys(Process mirror)
    {
        this.mirror = mirror;
        callback = HandleKey;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
    }

    public async Task RunAsync(string deviceName, Action<IntPtr> rememberConnection, Action<int> showSettings, MirrorMenuSignal menuSignal)
    {
        bool recorded = false;
        var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var icon = WirelessMirror.LoadIcon())
        using (var menu = CreateMenu())
        using (var tray = new NotifyIcon { Icon = icon, Text = deviceName.Length > 50 ? deviceName.Substring(0, 50) : deviceName, ContextMenuStrip = menu, Visible = true })
        using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
        {
            tray.DoubleClick += delegate { settingsPage = 1; };
            timer.Tick += delegate
            {
                IntPtr window = WirelessMirror.MirrorWindow(mirror);
                if (mirror.HasExited) { timer.Stop(); exited.TrySetResult(true); return; }
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
                            tray.ShowBalloonTip(4000, "安卓无线投屏",
                                "本次连接记录未能保存：" + historyError.Message, ToolTipIcon.Warning);
                        }
                    }
                    if (page != 0) showSettings(page - 1);
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, "安卓无线投屏", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        menu.Items.Add("设备管理", null, delegate { settingsPage = 1; });
        menu.Items.Add("快捷键", null, delegate { settingsPage = 2; });
        menu.Items.Add("连接设置", null, delegate { settingsPage = 3; });
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
        if (key < (int)Keys.F2 || key > (int)Keys.F10 || up)
            return CallNextHookEx(hook, code, message, data);

        uint foregroundProcess;
        GetWindowThreadProcessId(GetForegroundWindow(), out foregroundProcess);
        if (foregroundProcess != (uint)mirror.Id || Control.ModifierKeys != Keys.None)
            return CallNextHookEx(hook, code, message, data);
        if (!heldKeys.Add(key)) return new IntPtr(1);

        switch ((Keys)key)
        {
            case Keys.F2: SendShortcut((byte)'H', false); break;
            case Keys.F3: SendShortcut((byte)'S', false); break;
            case Keys.F4: SendShortcut((byte)'N', false); break;
            case Keys.F5: SendShortcut((byte)'N', true); break;
            case Keys.F6: SendShortcut((byte)'F', false); break;
            case Keys.F7: SendShortcut((byte)'B', false); break;
            case Keys.F8: SendShortcut((byte)'O', false); break;
            case Keys.F9: SendShortcut((byte)'O', true); break;
            case Keys.F10: settingsPage = 1; break;
        }
        return new IntPtr(1);
    }

    private static void SendShortcut(byte key, bool shift)
    {
        const byte leftAlt = 0xa4, leftShift = 0xa0;
        const uint keyUp = 2;
        keybd_event(leftAlt, 0, 0, UIntPtr.Zero);
        if (shift) keybd_event(leftShift, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, keyUp, UIntPtr.Zero);
        if (shift) keybd_event(leftShift, 0, keyUp, UIntPtr.Zero);
        keybd_event(leftAlt, 0, keyUp, UIntPtr.Zero);
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }
}
