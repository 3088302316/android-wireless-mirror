using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal class TransparentPanel : Panel
{
    public TransparentPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
    }
}

internal sealed class TransparentCard : TransparentPanel
{
    public int Radius = 12;
    public Color BorderColor = WindowTheme.Line;
    public float BorderWidth = 1f;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Rectangle bounds = ClientRectangle;
        if (bounds.Width < 3 || bounds.Height < 3) return;
        var rect = new Rectangle(bounds.Left, bounds.Top, Math.Max(1, bounds.Width - 1), Math.Max(1, bounds.Height - 1));
        using (var path = Rounded(rect, Radius))
        using (var pen = new Pen(BorderColor, BorderWidth))
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        }
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
