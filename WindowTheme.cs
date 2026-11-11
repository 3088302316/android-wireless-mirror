using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class WindowTheme
{
    public static readonly Color Canvas = Color.FromArgb(23, 19, 31);
    public static readonly Color Rail = Color.FromArgb(33, 27, 43);
    public static readonly Color Surface = Color.FromArgb(45, 37, 56);
    public static readonly Color SurfaceRaised = Color.FromArgb(59, 49, 74);
    public static readonly Color Selected = Color.FromArgb(82, 63, 103);
    public static readonly Color Ink = Color.FromArgb(255, 246, 240);
    public static readonly Color Muted = Color.FromArgb(215, 199, 217);
    public static readonly Color Faint = Color.FromArgb(162, 142, 175);
    public static readonly Color Accent = Color.FromArgb(255, 139, 121);
    public static readonly Color AccentLight = Color.FromArgb(255, 183, 168);
    public static readonly Color Green = Color.FromArgb(134, 229, 194);
    public static readonly Color Red = Color.FromArgb(255, 127, 150);
    public static readonly Color Amber = Color.FromArgb(255, 208, 143);
    public static readonly Color Line = Color.FromArgb(74, 62, 88);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    public static void Attach(Form form)
    {
        if (form == null) return;
        if (form.IsHandleCreated) Apply(form.Handle);
        else form.HandleCreated += delegate { Apply(form.Handle); };
    }

    private static void Apply(IntPtr handle)
    {
        int enabled = 1;
        // Windows 11 uses attribute 20; Windows 10 1809+ uses 19.
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));

        int border = ColorTranslator.ToWin32(Canvas);
        DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
    }

    public static Font Mono(float size, FontStyle style = FontStyle.Regular)
    {
        return new Font("Consolas", size, style, GraphicsUnit.Point);
    }
}
