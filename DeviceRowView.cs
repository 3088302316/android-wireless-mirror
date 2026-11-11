using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class DeviceRowView : Control
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    private readonly DeviceRecord row;
    private readonly Bitmap phoneIcon;
    private readonly Font nameFont;
    private readonly Font smallFont;
    private readonly int index;
    private bool selected;
    private bool hover;

    public event Action<int> SelectedRequested;
    public event Action<int> OpenRequested;

    public DeviceRowView(DeviceRecord row, Bitmap phoneIcon, Font nameFont, Font smallFont, int index)
    {
        this.row = row;
        this.phoneIcon = phoneIcon;
        this.nameFont = nameFont;
        this.smallFont = smallFont;
        this.index = index;
        Height = 66;
        Margin = new Padding(0, 0, 0, 7);
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;

        var hit = new AccessibleButton
        {
            Dock = DockStyle.Fill,
            Text = "",
            BackColor = Color.FromArgb(1, 0, 0, 0),
            DefaultBack = Color.FromArgb(1, 0, 0, 0),
            BackHover = Color.FromArgb(24, 255, 255, 255),
            BorderWidth = 0,
            Radius = 10,
            TabStop = false,
            Cursor = Cursors.Hand
        };
        hit.HandleCreated += delegate
        {
            int style = GetWindowLong(hit.Handle, GWL_EXSTYLE);
            SetWindowLong(hit.Handle, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT);
        };
        hit.Click += delegate
        {
            if (SelectedRequested != null) SelectedRequested(index);
        };
        hit.MouseDown += delegate(object sender, MouseEventArgs args)
        {
            if (args.Button == MouseButtons.Left && args.Clicks == 2 && OpenRequested != null)
                OpenRequested(index);
        };
        Controls.Add(hit);
    }

    public int Index { get { return index; } }

    public bool IsSelected
    {
        get { return selected; }
        set { if (selected == value) return; selected = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            if (SelectedRequested != null) SelectedRequested(index);
            if (e.Clicks == 2 && OpenRequested != null) OpenRequested(index);
        }
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = ClientRectangle;
        if (bounds.Width < 40 || bounds.Height < 24) return;
        var tile = Rectangle.Inflate(bounds, -2, -2);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        int iconX = tile.Left + 14;
        int iconY = tile.Top + (tile.Height - 30) / 2;
        if (phoneIcon != null) e.Graphics.DrawImage(phoneIcon, iconX, iconY, 22, 22);

        int textX = iconX + 36;
        int rightX = tile.Right - 150;
        int nameWidth = Math.Max(80, rightX - textX - 10);
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
            TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        Color nameColor = selected ? WindowTheme.AccentLight :
            hover ? Color.FromArgb(248, 251, 255) : WindowTheme.Ink;
        TextRenderer.DrawText(e.Graphics, row.Name, nameFont,
            new Rectangle(textX, tile.Top + 8, nameWidth, 27), nameColor, flags);
        string model = row.Model == row.Name ? "Android 无线调试" : row.Model;
        TextRenderer.DrawText(e.Graphics, model, smallFont,
            new Rectangle(textX, tile.Top + 34, nameWidth, 20), WindowTheme.Muted, flags);

        var state = row.Online ? "● 在线" : "○ 离线";
        TextRenderer.DrawText(e.Graphics, state, smallFont,
            new Rectangle(tile.Right - 148, tile.Top + 8, 136, 22),
            row.Online ? WindowTheme.Green : WindowTheme.Faint,
            TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        if (row.LastUsed != DateTime.MinValue)
        {
            TextRenderer.DrawText(e.Graphics, row.LastUsed.ToLocalTime().ToString("MM-dd HH:mm"), smallFont,
                new Rectangle(tile.Right - 148, tile.Top + 34, 136, 20),
                WindowTheme.Faint,
                TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
