using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class DeviceArtwork : Control
{
    private readonly Timer timer = new Timer { Interval = 33 };
    private readonly Stopwatch clock = new Stopwatch();
    private bool online;

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int enabled, uint flags);

    public DeviceArtwork()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Size = new Size(200, 124);
        Anchor = AnchorStyles.Left | AnchorStyles.Right;
        Margin = new Padding(0, 5, 0, 13);
        TabStop = false;
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
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Math.Min(ClientSize.Width / 200f, ClientSize.Height / 124f);
        graphics.TranslateTransform((ClientSize.Width - 200f * scale) / 2f,
            (ClientSize.Height - 124f * scale) / 2f);
        graphics.ScaleTransform(scale, scale);

        Color signal = online ? Color.FromArgb(137, 244, 210) : Color.FromArgb(151, 162, 190);
        DrawCard(graphics, signal);
        DrawGlow(graphics, new Rectangle(13, 20, 82, 86), Color.FromArgb(online ? 48 : 20, 117, 93, 230));
        DrawGlow(graphics, new Rectangle(112, 24, 78, 76), Color.FromArgb(online ? 38 : 16, 71, 190, 184));
        DrawPhone(graphics, signal);
        DrawMonitor(graphics, signal);
        DrawLink(graphics, signal);
    }

    private void DrawCard(Graphics graphics, Color signal)
    {
        var bounds = new Rectangle(1, 1, 198, 122);
        using (var path = Shape(bounds, 18))
        using (var background = new LinearGradientBrush(bounds,
            Color.FromArgb(42, 50, 82), Color.FromArgb(16, 34, 45), 22f))
        using (var border = new Pen(Color.FromArgb(online ? 100 : 72, 91, 109, 155), 1f))
        {
            graphics.FillPath(background, path);
            graphics.DrawPath(border, path);
        }
        using (var top = new Pen(Color.FromArgb(42, 255, 255, 255), 1f))
            graphics.DrawLine(top, 12, 3, 188, 3);
    }

    private static void DrawPhone(Graphics graphics, Color signal)
    {
        var body = new Rectangle(29, 29, 46, 68);
        using (var shadow = Shape(new Rectangle(body.X + 2, body.Y + 4, body.Width, body.Height), 9))
        using (var brush = new SolidBrush(Color.FromArgb(85, 0, 0, 0)))
            graphics.FillPath(brush, shadow);
        using (var path = Shape(body, 9))
        using (var fill = new LinearGradientBrush(body,
            Color.FromArgb(72, 86, 136), Color.FromArgb(29, 37, 68), 25f))
        using (var border = new Pen(Color.FromArgb(172, 178, 220), 1.3f))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }

        var screen = new Rectangle(34, 37, 36, 46);
        using (var path = Shape(screen, 5))
        using (var fill = new LinearGradientBrush(screen,
            Color.FromArgb(21, 31, 54), Color.FromArgb(14, 47, 54), 35f))
        using (var border = new Pen(Color.FromArgb(76, 104, 144), 0.8f))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }
        using (var speaker = new Pen(Color.FromArgb(184, 196, 232), 1.2f))
            graphics.DrawLine(speaker, 46, 33, 59, 33);
        using (var camera = new SolidBrush(Color.FromArgb(164, 190, 229)))
            graphics.FillEllipse(camera, 56, 32, 2.2f, 2.2f);
        using (var home = new Pen(Color.FromArgb(132, 160, 208), 1f))
            graphics.DrawLine(home, 45, 91, 59, 91);
        using (var accent = new SolidBrush(Color.FromArgb(120, signal)))
            graphics.FillEllipse(accent, 49, 55, 6, 6);
    }

    private static void DrawMonitor(Graphics graphics, Color signal)
    {
        var body = new Rectangle(123, 31, 50, 38);
        using (var shadow = Shape(new Rectangle(body.X + 2, body.Y + 4, body.Width, body.Height), 6))
        using (var brush = new SolidBrush(Color.FromArgb(72, 0, 0, 0)))
            graphics.FillPath(brush, shadow);
        using (var path = Shape(body, 6))
        using (var fill = new LinearGradientBrush(body,
            Color.FromArgb(65, 78, 127), Color.FromArgb(24, 36, 63), 28f))
        using (var border = new Pen(Color.FromArgb(168, 177, 219), 1.2f))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }

        var screen = new Rectangle(128, 36, 40, 26);
        using (var path = Shape(screen, 3))
        using (var fill = new LinearGradientBrush(screen,
            Color.FromArgb(17, 27, 47), Color.FromArgb(17, 60, 64), 30f))
        using (var border = new Pen(Color.FromArgb(72, 109, 148), 0.8f))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
        }
        using (var stand = new Pen(Color.FromArgb(136, 151, 192), 1.2f))
        {
            graphics.DrawLine(stand, 148, 69, 148, 78);
            graphics.DrawLine(stand, 137, 82, 159, 82);
        }
        using (var accent = new SolidBrush(Color.FromArgb(105, signal)))
            graphics.FillEllipse(accent, 145, 47, 6, 6);
    }

    private void DrawLink(Graphics graphics, Color signal)
    {
        using (var dot = new SolidBrush(Color.FromArgb(104, 166, 190, 228)))
        {
            for (int x = 80; x < 122; x += 7)
                graphics.FillEllipse(dot, x, 63, 2, 2);
        }
        using (var ring = new Pen(Color.FromArgb(88, signal), 1f))
        {
            graphics.DrawArc(ring, 91, 52, 18, 18, 210, 120);
            graphics.DrawArc(ring, 94, 56, 12, 12, 210, 120);
            graphics.DrawArc(ring, 97, 60, 6, 6, 210, 120);
        }
        using (var center = new SolidBrush(signal))
            graphics.FillEllipse(center, 98, 60, 5, 5);
        if (online && timer.Enabled)
        {
            float progress = (clock.ElapsedMilliseconds % 1200) / 1200f;
            float size = 6 + progress * 22;
            using (var pulse = new Pen(Color.FromArgb((int)(120 * (1 - progress)), signal), 1.1f))
                graphics.DrawEllipse(pulse, 100 - size / 2, 62 - size / 2, size, size);
        }
    }

    private static void DrawGlow(Graphics graphics, Rectangle bounds, Color color)
    {
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(bounds);
            using (var brush = new PathGradientBrush(path))
            {
                brush.CenterColor = color;
                brush.SurroundColors = new[] { Color.FromArgb(0, color) };
                graphics.FillPath(brush, path);
            }
        }
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
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
