using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

internal sealed class WallpaperPanel : Panel
{
    internal Image Image;
    private Bitmap surface;
    private readonly Timer settle = new Timer { Interval = 90 };
    private bool resizing;

    public WallpaperPanel()
    {
        BackColor = WindowTheme.Canvas;
        DoubleBuffered = true;
        ResizeRedraw = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        settle.Tick += delegate
        {
            settle.Stop();
            resizing = false;
            surface = null;
            Invalidate();
        };
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        resizing = true;
        settle.Stop();
        settle.Start();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Rectangle bounds = ClientRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (surface == null || (!resizing && (surface.Width != bounds.Width || surface.Height != bounds.Height)))
            BuildSurface(bounds.Size);

        if (surface != null && resizing && (surface.Width != bounds.Width || surface.Height != bounds.Height))
        {
            e.Graphics.InterpolationMode = InterpolationMode.Bilinear;
            e.Graphics.DrawImage(surface, bounds);
        }
        else if (surface != null)
        {
            e.Graphics.DrawImageUnscaled(surface, 0, 0);
        }
    }

    private void BuildSurface(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        Bitmap next = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(next))
        {
            using (var background = new SolidBrush(WindowTheme.Canvas))
                graphics.FillRectangle(background, new Rectangle(0, 0, size.Width, size.Height));
            if (Image != null)
            {
                double imageRatio = (double)Image.Width / Image.Height;
                double clientRatio = (double)size.Width / size.Height;
                int width, height;
                if (imageRatio > clientRatio)
                {
                    height = size.Height;
                    width = (int)Math.Ceiling(height * imageRatio);
                }
                else
                {
                    width = size.Width;
                    height = (int)Math.Ceiling(width / imageRatio);
                }
                width = Math.Max(width, size.Width);
                height = Math.Max(height, size.Height);
                graphics.InterpolationMode = InterpolationMode.Bilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
                graphics.DrawImage(Image,
                    new Rectangle((size.Width - width) / 2, (size.Height - height) / 2, width, height));
                using (var shade = new SolidBrush(Color.FromArgb(172, 8, 10, 16)))
                    graphics.FillRectangle(shade, new Rectangle(0, 0, size.Width, size.Height));
            }
        }
        if (surface != null) surface.Dispose();
        surface = next;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            settle.Dispose();
            if (surface != null) surface.Dispose();
        }
        base.Dispose(disposing);
    }
}
