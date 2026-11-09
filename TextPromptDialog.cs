using System;
using System.Drawing;
using System.Windows.Forms;
using AppButton = AccessibleButton;

internal sealed class TextPromptDialog : Form
{
    private readonly AntdUI.Input input;
    private readonly Label error = new Label();
    private readonly AppButton submit;

    internal TextPromptDialog(string title, string prompt, string initialValue, int maxLength)
    {
        Text = title;
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
        ClientSize = new Size(470, 244);
        WindowTheme.Attach(this);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(24, 20, 24, 18), BackColor = WindowTheme.Canvas };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = WindowTheme.Ink, Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold), Margin = new Padding(0) }, 0, 0);
        heading.Controls.Add(new Label { Text = prompt, AutoSize = true, ForeColor = WindowTheme.Muted, Font = new Font("Microsoft YaHei UI", 9.2f), Margin = new Padding(1, 0, 0, 0) }, 0, 1);
        layout.Controls.Add(heading, 0, 0);

        input = new AntdUI.Input
        {
            Dock = DockStyle.Fill,
            MaxLength = maxLength,
            Text = initialValue,
            Radius = 9,
            BorderWidth = 1,
            BorderColor = WindowTheme.Line,
            BorderActive = WindowTheme.AccentLight,
            BorderHover = Color.FromArgb(92, 93, 137),
            BackColor = Color.FromArgb(28, 33, 50),
            ForeColor = WindowTheme.Ink,
            Font = new Font("Microsoft YaHei UI", 11f),
            PaddingLR = 11,
            AccessibleName = "设备名称",
            Margin = new Padding(0, 6, 0, 0)
        };
        input.TextChanged += delegate { error.Text = String.Empty; };
        layout.Controls.Add(input, 0, 1);

        error.AutoSize = false;
        error.Dock = DockStyle.Fill;
        error.ForeColor = WindowTheme.Red;
        error.Font = new Font("Microsoft YaHei UI", 9f);
        error.TextAlign = ContentAlignment.MiddleLeft;
        error.Margin = new Padding(1, 2, 0, 0);
        layout.Controls.Add(error, 0, 2);

        submit = DeviceSettings.StyledButton("保存", "CheckOutlined", null, true);
        submit.Size = new Size(100, 42);
        submit.Click += delegate { SaveName(); };
        var cancel = DeviceSettings.StyledButton("取消", "CloseOutlined", null, false);
        cancel.Size = new Size(96, 42);
        cancel.DialogResult = DialogResult.Cancel;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0) };
        actions.Controls.Add(submit);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
        AcceptButton = submit;
        CancelButton = cancel;
        input.KeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SaveName(); }
        };
        Shown += delegate
        {
            input.Focus();
            input.SelectionStart = 0;
            input.SelectionLength = (input.Text ?? String.Empty).Length;
        };
    }

    internal string Value { get; private set; }

    private void SaveName()
    {
        string value = (input.Text ?? String.Empty).Trim();
        if (value.Length == 0) { error.Text = "设备名称不能为空。"; return; }
        if (value.Length > 40) { error.Text = "设备名称最多 40 个字。"; return; }
        foreach (char c in value)
            if (Char.IsControl(c)) { error.Text = "设备名称需要是单行文字。"; return; }
        Value = value;
        DialogResult = DialogResult.OK;
        Close();
    }
}
