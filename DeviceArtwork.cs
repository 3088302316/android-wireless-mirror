using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

internal sealed class DeviceArtwork : Control
{
    private readonly Timer timer = new Timer { Interval = 33 };
    private readonly Stopwatch clock = new Stopwatch();
    private readonly Bitmap monitor;
    private readonly Bitmap phone;
    private bool online;

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int enabled, uint flags);

    public DeviceArtwork()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(200, 124);
        Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Margin = new Padding(0, 5, 0, 13);
        TabStop = false;
        monitor = AntdUI.SvgExtend.SvgToBmp(MirrorIcons.Monitor, 27, 27, null);
        phone = AntdUI.SvgExtend.SvgToBmp(MirrorIcons.Smartphone, 27, 27, null);
        timer.Tick += delegate
        {
            if (clock.ElapsedMilliseconds >= 1600) timer.Stop();
            Invalidate();
        };
    }

    public void ShowStatus(bool connected)
    {
        online = connected;
        clock.Restart();
        timer.Stop();
        int animation;
        if (online && Visible && SystemParametersInfo(0x1042, 0, out animation, 0) && animation != 0) timer.Start();
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Disposing || IsDisposed) return;
        if (!Visible) timer.Stop();
        else ShowStatus(online);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 40 || Height < 40) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Math.Min(ClientSize.Width / 200f, ClientSize.Height / 124f);
        g.TranslateTransform((ClientSize.Width - 200f * scale) / 2f,
            (ClientSize.Height - 124f * scale) / 2f);
        g.ScaleTransform(scale, scale);
        using (var edge = Shape(new Rectangle(1, 1, 198, 122), 16))
        using (var background = new LinearGradientBrush(new Rectangle(0, 0, 200, 124),
            Color.FromArgb(47, 40, 88), Color.FromArgb(20, 59, 70), 20f))
        using (var border = new Pen(Color.FromArgb(95, 103, 154)))
        {
            g.FillPath(background, edge);
            g.DrawPath(border, edge);
            g.SetClip(edge);
        }
        using (var grid = new SolidBrush(Color.FromArgb(46, 174, 192, 241)))
            for (int y = 10; y < 124; y += 12)
                for (int x = 10; x < 200; x += 12)
                    g.FillEllipse(grid, x, y, 1.5f, 1.5f);

        Color signal = online ? Color.FromArgb(133, 252, 218) : Color.FromArgb(157, 164, 194);
        using (var line = new Pen(Color.FromArgb(133, signal), 1.4f) { DashStyle = DashStyle.Dot })
            g.DrawLine(line, 61, 61, 139, 61);
        if (online && timer.Enabled)
        {
            float progress = (clock.ElapsedMilliseconds % 800) / 800f;
            float size = 6 + progress * 40;
            using (var pulse = new Pen(Color.FromArgb((int)(150 * (1 - progress)), signal), 1.6f))
                g.DrawEllipse(pulse, 100 - size / 2, 61 - size / 2, size, size);
        }
        using (var point = new SolidBrush(signal))
            g.FillEllipse(point, 97, 58, 6, 6);
        DrawNode(g, new Rectangle(17, 37, 49, 49), phone);
        DrawNode(g, new Rectangle(134, 37, 49, 49), monitor);
    }

    private static void DrawNode(Graphics g, Rectangle bounds, Bitmap icon)
    {
        using (var node = Shape(bounds, 13))
        using (var fill = new SolidBrush(Color.FromArgb(29, 35, 60)))
        using (var edge = new Pen(Color.FromArgb(115, 130, 181)))
        {
            g.FillPath(fill, node);
            g.DrawPath(edge, node);
        }
        if (icon != null) g.DrawImage(icon, new Rectangle(bounds.X + 11, bounds.Y + 11, 27, 27));
    }

    internal static GraphicsPath Shape(Rectangle box, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(box.Left, box.Top, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            if (monitor != null) monitor.Dispose();
            if (phone != null) phone.Dispose();
        }
        base.Dispose(disposing);
    }
}
