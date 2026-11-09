using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class MirrorTelemetry : IDisposable
{
    private readonly Process mirror;
    private readonly string serial;
    private readonly Timer timer = new Timer { Interval = 2000 };
    private string host;
    private bool sampling;
    private bool resolved;
    private bool disposed;

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);

    public MirrorTelemetry(Process mirror, string serial)
    {
        this.mirror = mirror;
        this.serial = serial;
        timer.Tick += delegate { Sample(); };
    }

    public void Start()
    {
        timer.Start();
        Sample();
    }

    private async void Sample()
    {
        if (disposed || sampling) return;
        sampling = true;
        try
        {
            if (!resolved)
            {
                host = await Task.Run(delegate { return NetworkDiagnostics.ResolveHost(serial); });
                resolved = true;
            }
            if (disposed) return;
            int milliseconds = -1;
            if (host != null)
            {
                var result = await Task.Run(delegate { return NetworkDiagnostics.Probe(host, 1, 750); });
                if (disposed) return;
                if (result.Received > 0) milliseconds = (int)result.Average;
            }

            IntPtr window = WirelessMirror.MirrorWindow(mirror);
            if (window != IntPtr.Zero)
                PostMessage(window, 0x802A, new IntPtr(milliseconds), IntPtr.Zero);
        }
        catch (Exception error)
        {
            resolved = true;
            if (!disposed) AppLog.Warn("实时网络往返探测: " + error.Message);
        }
        finally { sampling = false; }
    }

    public void Dispose()
    {
        disposed = true;
        timer.Dispose();
    }
}
