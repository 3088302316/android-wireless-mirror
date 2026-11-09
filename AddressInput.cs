using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal sealed class AddressEntryControl : UserControl
{
    private const int SegmentWidth = 62;
    private const int DotWidth = 14;
    private const int ColonWidth = 16;
    private const int PortWidth = 86;
    private const int SegmentHeight = 52;
    private readonly SegmentBox[] octets = new SegmentBox[4];
    private readonly SegmentBox port;
    private readonly ContextMenuStrip menu;
    private bool settingText;
    private string lastPasteError;

    internal event EventHandler AddressChanged;
    internal event EventHandler SubmitRequested;
    internal event EventHandler CancelRequested;

    public AddressEntryControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = WindowTheme.Canvas;
        Height = SegmentHeight;
        MinimumSize = new Size(SegmentWidth * 4 + DotWidth * 3 + ColonWidth + PortWidth, SegmentHeight);
        Size = MinimumSize;
        Margin = new Padding(0);
        TabStop = false;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 9,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = WindowTheme.Canvas
        };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        int[] widths = { SegmentWidth, DotWidth, SegmentWidth, DotWidth, SegmentWidth, DotWidth, SegmentWidth, ColonWidth, PortWidth };
        for (int i = 0; i < widths.Length; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, widths[i]));

        menu = new ContextMenuStrip { ShowImageMargin = false, Font = new Font("Microsoft YaHei UI", 9.5f) };
        menu.Items.Add("粘贴完整地址  Ctrl+V", null, delegate { PasteFromClipboard(); });
        menu.Items.Add("清空地址", null, delegate { Clear(); });
        menu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e)
        {
            bool hasText = Address.Length > 0;
            menu.Items[1].Enabled = hasText;
        };

        for (int i = 0; i < 4; i++)
        {
            octets[i] = new SegmentBox(this, i, false);
            octets[i].Dock = DockStyle.Fill;
            octets[i].Margin = new Padding(0);
            table.Controls.Add(octets[i], i * 2, 0);
            if (i < 3) table.Controls.Add(DotLabel(), i * 2 + 1, 0);
        }
        port = new SegmentBox(this, 4, true);
        port.Dock = DockStyle.Fill;
        port.Margin = new Padding(0);
        table.Controls.Add(ColonLabel(), 7, 0);
        table.Controls.Add(port, 8, 0);
        Controls.Add(table);
    }

    internal string Address
    {
        get
        {
            return octets[0].Text + "." + octets[1].Text + "." + octets[2].Text + "." + octets[3].Text + ":" + port.Text;
        }
    }

    internal TextBox PortEditor { get { return port.Editor; } }
    internal TextBox FirstEditor { get { return octets[0].Editor; } }
    internal string LastPasteError { get { return lastPasteError; } }

    private static Label DotLabel()
    {
        return new Label
        {
            Text = ".",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = WindowTheme.Muted,
            Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold),
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 8),
            TabStop = false
        };
    }

    private static Label ColonLabel()
    {
        return new Label
        {
            Text = ":",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = WindowTheme.AccentLight,
            Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, 5),
            TabStop = false
        };
    }

    internal bool TryGetAddress(out string address, out string error)
    {
        address = null;
        error = null;
        int invalid = -1;
        for (int i = 0; i < 4; i++)
            if (!octets[i].HasUsableValue)
            {
                invalid = i;
                break;
            }
        if (invalid < 0 && !port.HasUsableValue) invalid = 4;

        string parsed;
        string parseError;
        if (!AddressParser.TryParse(Address, out parsed, out parseError))
        {
            error = parseError;
            if (invalid < 0) invalid = FirstEmptyOrInvalid();
            SetInvalid(invalid);
            if (invalid >= 0) FocusSegment(invalid, true);
            return false;
        }
        ClearValidation();
        address = parsed;
        return true;
    }

    internal void SetAddress(string value)
    {
        string parsed;
        string error;
        if (!AddressParser.TryParse(value, out parsed, out error)) throw new ArgumentException(error, "value");
        string[] hostPort = parsed.Split(':');
        string[] parts = hostPort[0].Split('.');
        settingText = true;
        try
        {
            for (int i = 0; i < 4; i++) octets[i].SetText(parts[i]);
            port.SetText(hostPort[1]);
        }
        finally { settingText = false; }
        ClearValidation();
        RaiseChanged();
    }

    internal bool PasteText(string value)
    {
        string parsed;
        string error;
        if (!AddressParser.TryParse(value, out parsed, out error))
        {
            lastPasteError = error;
            ClearValidation();
            return false;
        }
        lastPasteError = null;
        SetAddress(parsed);
        FocusSegment(4, true);
        return true;
    }

    internal bool PasteFromClipboard()
    {
        string value = null;
        try { value = Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : null; }
        catch (Exception) { value = null; }
        if (String.IsNullOrEmpty(value))
        {
            lastPasteError = "剪贴板里没有可粘贴的地址。";
            ClearValidation();
            return false;
        }
        return PasteText(value);
    }

    internal void Clear()
    {
        settingText = true;
        try
        {
            foreach (var box in octets) box.SetText(String.Empty);
            port.SetText(String.Empty);
        }
        finally { settingText = false; }
        ClearValidation();
        FocusFirstEmpty();
        RaiseChanged();
    }

    internal void FocusFirstEmpty()
    {
        for (int i = 0; i < 4; i++)
            if (octets[i].Text.Length == 0) { FocusSegment(i, false); return; }
        FocusSegment(4, false);
    }

    internal void ClearValidation()
    {
        foreach (var box in octets) box.SetInvalid(false);
        port.SetInvalid(false);
    }

    internal void SetInvalid(int index)
    {
        for (int i = 0; i < 4; i++) octets[i].SetInvalid(i == index);
        port.SetInvalid(index == 4);
    }

    private int FirstEmptyOrInvalid()
    {
        for (int i = 0; i < 4; i++)
            if (!octets[i].HasUsableValue) return i;
        if (!port.HasUsableValue) return 4;
        return -1;
    }

    private void FocusSegment(int index, bool selectAll)
    {
        var box = index == 4 ? port : octets[index];
        box.Editor.Focus();
        if (selectAll) box.Editor.SelectAll();
        else
        {
            box.Editor.SelectionStart = box.Editor.TextLength;
            box.Editor.SelectionLength = 0;
        }
    }

    private void RaiseChanged()
    {
        var handler = AddressChanged;
        if (handler != null && !settingText) handler(this, EventArgs.Empty);
    }

    internal void OnSegmentTextChanged(SegmentBox box)
    {
        if (settingText) return;
        RaiseChanged();
        if (!box.IsPort && box.TextLength == 3)
        {
            BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) MoveNext(box); });
        }
    }

    internal void MoveNext(SegmentBox box)
    {
        int index = box.Index + 1;
        if (index <= 4) FocusSegment(index, false);
    }

    internal void MovePrevious(SegmentBox box)
    {
        int index = box.Index - 1;
        if (index >= 0) FocusSegment(index, true);
    }

    internal void AdvanceSeparator(SegmentBox box, char separator)
    {
        if (separator == ':' || box.Index == 3) FocusSegment(4, false);
        else if (box.Index < 3) FocusSegment(box.Index + 1, false);
    }

    internal void Step(SegmentBox box, int delta)
    {
        int value;
        if (!Int32.TryParse(box.Text, out value)) value = box.IsPort ? 1 : 0;
        value += delta;
        int max = box.IsPort ? 65535 : 255;
        int min = box.IsPort ? 1 : 0;
        if (value < min) value = min;
        if (value > max) value = max;
        box.SetText(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        box.Editor.SelectAll();
        RaiseChanged();
    }

    internal void Submit()
    {
        var handler = SubmitRequested;
        if (handler != null) handler(this, EventArgs.Empty);
    }

    internal void Cancel()
    {
        var handler = CancelRequested;
        if (handler != null) handler(this, EventArgs.Empty);
    }

    internal ContextMenuStrip SharedMenu { get { return menu; } }

    internal sealed class SegmentBox : Panel
    {
        private readonly AddressEntryControl owner;
        private readonly bool isPort;
        private readonly int index;
        private readonly Color field = Color.FromArgb(28, 33, 50);
        private bool invalid;
        private bool hover;
        internal readonly TextBox Editor;

        internal SegmentBox(AddressEntryControl owner, int index, bool isPort)
        {
            this.owner = owner;
            this.index = index;
            this.isPort = isPort;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = owner.BackColor;
            Margin = new Padding(0);
            Padding = new Padding(0);

            Editor = new PasteAwareTextBox(owner);
            Editor.BorderStyle = BorderStyle.None;
            Editor.BackColor = field;
            Editor.ForeColor = WindowTheme.Ink;
            Editor.Font = WindowTheme.Mono(isPort ? 15f : 16f, FontStyle.Bold);
            Editor.TextAlign = HorizontalAlignment.Center;
            Editor.MaxLength = isPort ? 5 : 3;
            Editor.ImeMode = ImeMode.Disable;
            Editor.AutoSize = false;
            Editor.ShortcutsEnabled = true;
            Editor.AccessibleName = isPort ? "端口" : "IPv4 第 " + (index + 1) + " 段";
            Editor.TabStop = true;
            Editor.TabIndex = index;
            Editor.ContextMenuStrip = owner.SharedMenu;
            Editor.TextChanged += EditorTextChanged;
            Editor.KeyDown += EditorKeyDown;
            Editor.KeyPress += EditorKeyPress;
            Editor.HandleCreated += delegate { NativeMethods.SetCue(Editor, isPort ? "端口" : "0"); };
            Editor.Enter += delegate { Invalidate(); };
            Editor.Leave += delegate { Invalidate(); };
            Editor.MouseEnter += delegate { hover = true; Invalidate(); };
            Editor.MouseLeave += delegate { hover = false; Invalidate(); };
            Controls.Add(Editor);
        }

        internal int Index { get { return index; } }
        internal bool IsPort { get { return isPort; } }
        internal new string Text { get { return Editor.Text; } }
        internal int TextLength { get { return Editor.TextLength; } }
        internal bool HasUsableValue
        {
            get
            {
                if (Text.Length == 0) return false;
                int value;
                if (!Int32.TryParse(Text, out value)) return false;
                return isPort ? value >= 1 && value <= 65535 : value >= 0 && value <= 255;
            }
        }

        internal void SetText(string value)
        {
            Editor.Text = value ?? String.Empty;
        }

        internal void SetInvalid(bool value)
        {
            if (invalid == value) return;
            invalid = value;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int height = Editor.PreferredHeight;
            Editor.SetBounds(5, Math.Max(4, (Height - height) / 2), Math.Max(10, Width - 10), height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Rounded(box, 9))
            using (var fill = new SolidBrush(field))
            using (var pen = new Pen(invalid ? WindowTheme.Red : Editor.Focused ? WindowTheme.AccentLight : hover ? Color.FromArgb(92, 93, 137) : WindowTheme.Line,
                invalid || Editor.Focused ? 1.6f : 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
        }

        private void EditorTextChanged(object sender, EventArgs e)
        {
            if (owner.settingText) return;
            string clean = Digits(Editor.Text);
            if (!String.Equals(clean, Editor.Text, StringComparison.Ordinal))
            {
                int start = Editor.SelectionStart;
                owner.settingText = true;
                try { Editor.Text = clean; }
                finally { owner.settingText = false; }
                Editor.SelectionStart = Math.Min(start, Editor.TextLength);
                Editor.SelectionLength = 0;
            }
            if (Editor.TextLength > 0)
            {
                int value;
                if (Int32.TryParse(Editor.Text, out value))
                    SetInvalid(isPort ? value < 1 || value > 65535 : value > 255);
                else SetInvalid(false);
            }
            else SetInvalid(false);
            owner.OnSegmentTextChanged(this);
        }

        private void EditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                owner.Submit();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                owner.Cancel();
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                e.SuppressKeyPress = true;
                owner.Step(this, e.KeyCode == Keys.Up ? 1 : -1);
            }
            else if (e.KeyCode == Keys.Left && Editor.SelectionLength == 0 && Editor.SelectionStart == 0)
            {
                e.SuppressKeyPress = true;
                owner.MovePrevious(this);
            }
            else if (e.KeyCode == Keys.Right && Editor.SelectionLength == 0 && Editor.SelectionStart == Editor.TextLength)
            {
                e.SuppressKeyPress = true;
                owner.MoveNext(this);
            }
            else if (e.KeyCode == Keys.Back && Editor.TextLength == 0)
            {
                e.SuppressKeyPress = true;
                owner.MovePrevious(this);
            }
        }

        private void EditorKeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '.' || e.KeyChar == ':' || e.KeyChar == '。' || e.KeyChar == '；')
            {
                e.Handled = true;
                owner.AdvanceSeparator(this, e.KeyChar == '；' ? ':' : e.KeyChar);
                return;
            }
            if (!Char.IsDigit(e.KeyChar) && !Char.IsControl(e.KeyChar)) e.Handled = true;
        }

        private static string Digits(string value)
        {
            var output = new StringBuilder(value.Length);
            foreach (char original in value)
            {
                char c = original;
                if (c >= '０' && c <= '９') c = (char)('0' + (c - '０'));
                if (c >= '0' && c <= '9') output.Append(c);
            }
            return output.ToString();
        }

        private static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private sealed class PasteAwareTextBox : TextBox
        {
            private readonly AddressEntryControl owner;

            internal PasteAwareTextBox(AddressEntryControl owner)
            {
                this.owner = owner;
            }

            protected override bool ProcessCmdKey(ref Message message, Keys keyData)
            {
                if (keyData == (Keys.Control | Keys.V) || keyData == (Keys.Shift | Keys.Insert))
                {
                    owner.RequestPaste();
                    return true;
                }
                if (keyData == (Keys.Control | Keys.A))
                {
                    SelectAll();
                    return true;
                }
                return base.ProcessCmdKey(ref message, keyData);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0302)
                {
                    owner.RequestPaste();
                    return;
                }
                base.WndProc(ref message);
            }
        }
    }

    private void RequestPaste()
    {
        bool pasted = PasteFromClipboard();
        var handler = PasteAttempted;
        if (handler != null) handler(this, new PasteEventArgs(pasted));
    }

    internal event EventHandler<PasteEventArgs> PasteAttempted;
    internal sealed class PasteEventArgs : EventArgs
    {
        internal PasteEventArgs(bool success) { Success = success; }
        internal bool Success { get; private set; }
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);

        internal static void SetCue(TextBox box, string cue)
        {
            if (box != null && box.IsHandleCreated)
                SendMessage(box.Handle, 0x1501, (IntPtr)1, cue);
        }
    }
}
