using System;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AppButton = AccessibleButton;

internal sealed class AddressInputDialog : Form
{
    private readonly Action<string, string> operation;
    private readonly AddressEntryControl addressInput = new AddressEntryControl();
    private readonly AntdUI.Input code;
    private readonly Label status = new Label();
    private readonly AppButton submit;
    private readonly AppButton cancel;
    private readonly Bitmap icon;
    private bool busy;
    private bool sanitizingCode;

    internal AddressInputDialog(Action<string, string> operation)
    {
        this.operation = operation;
        Text = "六位码配对";
        Icon = WirelessMirror.LoadIcon();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Microsoft YaHei UI", 10f);
        BackColor = WindowTheme.Canvas;
        ForeColor = WindowTheme.Ink;
        ClientSize = new Size(600, 560);
        WindowTheme.Attach(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(28, 22, 28, 20),
            BackColor = WindowTheme.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(root);

        code = CreateCodeInput();
        icon = AntdUI.SvgExtend.SvgToBmp(MirrorIcons.Keyboard, 27, 27, null);
        root.Controls.Add(Header(icon), 0, 0);
        root.Controls.Add(HelpCard("从手机配对弹窗读取",
            "点「使用配对码配对设备」，弹窗里会显示 IP:端口 和六位配对码。此端口通常不同于无线调试主页端口。"), 0, 1);
        root.Controls.Add(AddressSection(), 0, 2);
        root.Controls.Add(CodeSection(), 0, 3);

        status.Dock = DockStyle.Fill;
        status.AutoSize = false;
        status.ForeColor = WindowTheme.Muted;
        status.Font = new Font("Microsoft YaHei UI", 9.4f);
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Padding = new Padding(1, 4, 1, 4);
        status.Text = DefaultHint();
        root.Controls.Add(status, 0, 4);

        cancel = DeviceSettings.StyledButton("取消", "CloseOutlined", null, false);
        cancel.Size = new Size(96, 42);
        cancel.DialogResult = DialogResult.Cancel;
        submit = DeviceSettings.StyledButton("开始配对", MirrorIcons.Keyboard, null, true);
        submit.Size = new Size(132, 42);
        submit.Click += delegate { SubmitFromUi(); };
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        var keys = new Label
        {
            Text = "Enter 提交  ·  Esc 取消",
            AutoSize = true,
            ForeColor = WindowTheme.Faint,
            Font = new Font("Microsoft YaHei UI", 8.8f),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0)
        };
        footer.Controls.Add(keys, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = false, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0) };
        actions.Controls.Add(submit);
        actions.Controls.Add(cancel);
        footer.Controls.Add(actions, 1, 0);
        root.Controls.Add(footer, 0, 5);

        AcceptButton = submit;
        CancelButton = cancel;
        addressInput.SubmitRequested += delegate { BeginInvoke((MethodInvoker)SubmitFromUi); };
        addressInput.CancelRequested += delegate { Close(); };
        addressInput.AddressChanged += delegate { if (!busy) SetStatus(DefaultHint(), WindowTheme.Muted); };
        addressInput.PasteAttempted += delegate(object sender, AddressEntryControl.PasteEventArgs e)
        {
            if (!e.Success) SetStatus(addressInput.LastPasteError, WindowTheme.Red);
            else SetStatus("已填入完整地址，请核对端口后继续。", WindowTheme.Green);
        };
        Shown += delegate { addressInput.FocusFirstEmpty(); };
        FormClosed += delegate { if (icon != null) icon.Dispose(); };
    }

    private AntdUI.Input CreateCodeInput()
    {
        var input = new AntdUI.Input
        {
            Width = 214,
            Height = 46,
            MaxLength = 6,
            Font = WindowTheme.Mono(16f, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Center,
            PlaceholderText = "6 位数字",
            Radius = 9,
            BorderWidth = 1,
            BorderColor = WindowTheme.Line,
            BorderActive = WindowTheme.AccentLight,
            BorderHover = Color.FromArgb(92, 93, 137),
            BackColor = Color.FromArgb(28, 33, 50),
            ForeColor = WindowTheme.Ink,
            PaddingLR = 10,
            AccessibleName = "六位配对码",
            Margin = new Padding(0, 1, 0, 0)
        };
        input.TextChanged += CodeTextChanged;
        input.KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; BeginInvoke((MethodInvoker)SubmitFromUi); }
        };
        return input;
    }

    private Control Header(Bitmap glyph)
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var tile = new AntdUI.Panel { Size = new Size(46, 46), Radius = 12, Back = WindowTheme.Selected, Margin = new Padding(0, 8, 14, 0), Anchor = AnchorStyles.Left | AnchorStyles.Top };
        var picture = new PictureBox { Image = glyph, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Dock = DockStyle.Fill, Padding = new Padding(9) };
        tile.Controls.Add(picture);
        header.Controls.Add(tile, 0, 0);
        var text = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        text.Controls.Add(new Label
        {
            Text = "六位码配对",
            AutoSize = true,
            ForeColor = WindowTheme.Ink,
            Font = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold),
            Margin = new Padding(0, 3, 0, 0)
        }, 0, 0);
        text.Controls.Add(new Label
        {
            Text = "输入手机配对弹窗里的地址和配对码",
            AutoSize = true,
            ForeColor = WindowTheme.Muted,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Margin = new Padding(1, 0, 0, 0)
        }, 0, 1);
        header.Controls.Add(text, 1, 0);
        return header;
    }

    private Control HelpCard(string title, string body)
    {
        var card = new AntdUI.Panel
        {
            Dock = DockStyle.Fill,
            Radius = 10,
            Back = WindowTheme.Surface,
            BorderWidth = 1,
            BorderColor = WindowTheme.Line,
            Margin = new Padding(0, 2, 0, 12)
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14, 10, 14, 9), Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = WindowTheme.AccentLight,
            Font = new Font("Microsoft YaHei UI", 9.8f, FontStyle.Bold),
            Margin = new Padding(0)
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = body,
            Dock = DockStyle.Fill,
            ForeColor = WindowTheme.Muted,
            Font = new Font("Microsoft YaHei UI", 9f),
            Margin = new Padding(0),
            AutoSize = false
        }, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control AddressSection()
    {
        var section = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0, 0, 0, 8) };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.Controls.Add(new Label
        {
            Text = "IP 地址与端口",
            AutoSize = true,
            ForeColor = WindowTheme.Ink,
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            Margin = new Padding(0)
        }, 0, 0);
        heading.Controls.Add(new Label
        {
            Text = "只填数字  ·  可直接粘贴完整地址",
            AutoSize = true,
            ForeColor = WindowTheme.Faint,
            Font = new Font("Microsoft YaHei UI", 8.8f),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0)
        }, 1, 0);
        section.Controls.Add(heading, 0, 0);
        addressInput.Dock = DockStyle.Fill;
        addressInput.Margin = new Padding(0, 2, 0, 2);
        section.Controls.Add(addressInput, 0, 1);
        section.Controls.Add(new Label
        {
            Text = "填满三位自动跳到下一格；Tab 或 . 前进，↑↓ 调整数值。",
            AutoSize = true,
            ForeColor = WindowTheme.Faint,
            Font = new Font("Microsoft YaHei UI", 8.8f),
            Margin = new Padding(1, 3, 0, 0)
        }, 0, 2);
        return section;
    }

    private Control CodeSection()
    {
        var section = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 2, 0, 0) };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.Controls.Add(new Label
        {
            Text = "六位配对码",
            AutoSize = true,
            ForeColor = WindowTheme.Ink,
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            Margin = new Padding(0)
        }, 0, 0);
        heading.Controls.Add(new Label
        {
            Text = "只填配对弹窗里显示的数字",
            AutoSize = true,
            ForeColor = WindowTheme.Faint,
            Font = new Font("Microsoft YaHei UI", 8.8f),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0)
        }, 1, 0);
        section.Controls.Add(heading, 0, 0);
        section.Controls.Add(code, 0, 1);
        return section;
    }

    private string DefaultHint()
    {
        return "填好地址和六位配对码后按 Enter 开始配对。";
    }

    private async void SubmitFromUi()
    {
        await SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        if (busy) return;
        string address;
        string error;
        if (!addressInput.TryGetAddress(out address, out error))
        {
            SetStatus(error, WindowTheme.Red);
            return;
        }

        string pairingCode;
        if (!AddressParser.TryParsePairingCode(code.Text, out pairingCode, out error))
        {
            SetStatus(error, WindowTheme.Red);
            code.Focus();
            return;
        }

        busy = true;
        SetInputEnabled(false);
        submit.Loading = true;
        submit.Text = "正在配对…";
        UseWaitCursor = true;
        SetStatus("正在向 " + address + " 发起配对…", WindowTheme.AccentLight);
        try
        {
            await Task.Run(delegate { operation(address, pairingCode); });
            if (IsDisposed) return;
            busy = false;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception failure)
        {
            if (IsDisposed) return;
            busy = false;
            SetInputEnabled(true);
            submit.Loading = false;
            submit.Text = "开始配对";
            UseWaitCursor = false;
            SetStatus(Clean(failure.Message), WindowTheme.Red);
            code.Focus();
            code.SelectAll();
        }
    }

    private void SetInputEnabled(bool enabled)
    {
        addressInput.Enabled = enabled;
        code.Enabled = enabled;
        cancel.Enabled = true;
    }

    private void SetStatus(string text, Color color)
    {
        status.Text = String.IsNullOrEmpty(text) ? DefaultHint() : text;
        status.ForeColor = color;
    }

    private void CodeTextChanged(object sender, EventArgs e)
    {
        if (sanitizingCode) return;
        string value = code.Text ?? String.Empty;
        var digits = new StringBuilder(value.Length);
        foreach (char original in value)
        {
            char c = original;
            if (c >= '０' && c <= '９') c = (char)('0' + (c - '０'));
            if (c >= '0' && c <= '9') digits.Append(c);
        }
        if (!String.Equals(value, digits.ToString(), StringComparison.Ordinal))
        {
            sanitizingCode = true;
            try { code.Text = digits.ToString(); }
            finally { sanitizingCode = false; }
        }
        if (!busy && status.ForeColor != WindowTheme.Green) SetStatus(DefaultHint(), WindowTheme.Muted);
    }

    private static string Clean(string message)
    {
        if (String.IsNullOrWhiteSpace(message)) return "操作失败，请检查手机和 Wi-Fi 后重试。";
        string text = message.Replace("\r", " ").Replace("\n", " ").Trim();
        while (text.Contains("  ")) text = text.Replace("  ", " ");
        if (text.Length > 260) text = text.Substring(0, 260) + "…";
        return text;
    }
}
