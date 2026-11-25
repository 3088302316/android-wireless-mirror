using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal sealed class MirrorMenuSignal : NativeWindow, IDisposable
{
    private const int MenuMessage = 0x8028; // WM_APP + 0x28
    private IntPtr sender;
    private int page;
    private ChineseInputDialog editor;

    [StructLayout(LayoutKind.Sequential)]
    private struct CopyData
    {
        public IntPtr Type;
        public int Size;
        public IntPtr Bytes;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr sender,
        ref CopyData data, uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private sealed class WindowOwner : IWin32Window
    {
        public WindowOwner(IntPtr window) { Handle = window; }
        public IntPtr Handle { get; private set; }
    }

    public void ShowTextInput(Process mirror, IntPtr window, string deviceName)
    {
        if (editor != null && !editor.IsDisposed) { editor.Activate(); return; }
        if (window == IntPtr.Zero || mirror.HasExited) return;
        editor = new ChineseInputDialog(deviceName, delegate(string text)
        {
            uint owner;
            if (mirror.HasExited || GetWindowThreadProcessId(window, out owner) == 0 || owner != (uint)mirror.Id)
                return "设备已断开。";
            return Paste(window, text);
        });
        editor.Show(new WindowOwner(window));
    }

    private string Paste(IntPtr window, string text)
    {
        text = text.Replace("\r\n", "\n");
        if (text.IndexOf('\0') >= 0) return "文字包含无法输入的空字符。";
        if (Encoding.UTF8.GetByteCount(text) > 65535) return "文字太长，请分段输入。";
        byte[] bytes = Encoding.UTF8.GetBytes(text + "\0");
        GCHandle pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var data = new CopyData { Type = new IntPtr(0x4d495458), Size = bytes.Length, Bytes = pinned.AddrOfPinnedObject() };
            IntPtr result;
            if (SendMessageTimeoutW(window, 0x004A, Handle, ref data, 3, 1200, out result) == IntPtr.Zero)
                return "未收到投屏窗口确认，请先检查设备上是否已输入。";
            return result == IntPtr.Zero ? "投屏窗口未接受文字，请检查连接并恢复画面。" : null;
        }
        finally { pinned.Free(); }
    }

    public bool CloseRequested { get; private set; }

    public MirrorMenuSignal()
    {
        CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
    }

    public int TakePage(IntPtr mirrorWindow)
    {
        int result = sender != IntPtr.Zero && sender == mirrorWindow ? page : 0;
        sender = IntPtr.Zero;
        page = 0;
        return result;
    }

    protected override void WndProc(ref Message message)
    {
        long command = message.WParam.ToInt64();
        if (message.Msg == MenuMessage && command == 7)
        {
            CloseRequested = true;
            return;
        }
        if (message.Msg == MenuMessage && command >= 1 && command <= 4)
        {
            sender = message.LParam;
            page = (int)command;
            return;
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (editor != null && !editor.IsDisposed) editor.Dispose();
        DestroyHandle();
    }
}
