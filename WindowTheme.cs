using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class WindowTheme
{
    public static readonly Color Canvas = Color.FromArgb(18, 20, 32);
    public static readonly Color Rail = Color.FromArgb(24, 28, 44);
    public static readonly Color Surface = Color.FromArgb(32, 38, 58);
    public static readonly Color SurfaceRaised = Color.FromArgb(39, 46, 69);
    public static readonly Color Selected = Color.FromArgb(54, 50, 100);
    public static readonly Color Ink = Color.FromArgb(239, 244, 255);
    public static readonly Color Muted = Color.FromArgb(170, 188, 213);
    public static readonly Color Faint = Color.FromArgb(126, 142, 169);
    public static readonly Color Accent = Color.FromArgb(100, 80, 218);
    public static readonly Color AccentLight = Color.FromArgb(178, 171, 255);
    public static readonly Color Green = Color.FromArgb(94, 224, 175);
    public static readonly Color Red = Color.FromArgb(246, 113, 137);
    public static readonly Color Amber = Color.FromArgb(246, 192, 104);
    public static readonly Color Line = Color.FromArgb(59, 70, 97);

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

        // Keep the Windows 11 resize frame dark instead of its default white edge.
        int border = ColorTranslator.ToWin32(Canvas);
        DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
    }

    public static Font Mono(float size, FontStyle style = FontStyle.Regular)
    {
        return new Font("Consolas", size, style, GraphicsUnit.Point);
    }
}
