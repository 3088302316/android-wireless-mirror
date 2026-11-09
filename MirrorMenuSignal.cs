using System;
using System.Windows.Forms;

internal sealed class MirrorMenuSignal : NativeWindow, IDisposable
{
    private const int MenuMessage = 0x8028; // WM_APP + 0x28
    private IntPtr sender;
    private int page;

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
        if (message.Msg == MenuMessage && command >= 1 && command <= 3)
        {
            sender = message.LParam;
            page = (int)command;
            return;
        }
        base.WndProc(ref message);
    }

    public void Dispose() { DestroyHandle(); }
}
