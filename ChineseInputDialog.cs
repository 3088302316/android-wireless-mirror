using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class ChineseInputDialog : Form
{
    private sealed class CompositionBox : TextBox
    {
        internal bool Composing { get; private set; }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x010D) Composing = true;  // WM_IME_STARTCOMPOSITION
            base.WndProc(ref message);
            if (message.Msg == 0x010E) Composing = false; // WM_IME_ENDCOMPOSITION
        }
    }

    internal ChineseInputDialog(string deviceName, Func<string, string> paste)
    {
        Text = "中文输入 · " + deviceName;
        Icon = WirelessMirror.LoadIcon();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Microsoft YaHei UI", 10f);
        BackColor = WindowTheme.Canvas;
        ForeColor = WindowTheme.Ink;
        ClientSize = new Size(490, 242);
        WindowTheme.Attach(this);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = 4, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var hint = new Label { AutoSize = true, Text = "用电脑输入法选词，文字会粘贴到设备当前的输入框。", Margin = new Padding(0, 0, 0, 10) };
        var input = new CompositionBox { Name = "Editor", Multiline = true, AcceptsReturn = true,
            ImeMode = ImeMode.On, AccessibleName = "输入到设备的文字", Dock = DockStyle.Fill,
            MaxLength = 65535, ScrollBars = ScrollBars.Vertical, BackColor = WindowTheme.Surface,
            ForeColor = WindowTheme.Ink, Font = new Font(Font.FontFamily, 12f) };
        var status = new Label { AutoSize = true, ForeColor = WindowTheme.Muted,
            Text = "使用设备剪贴板。快捷键 Ctrl+Enter。", Margin = new Padding(0, 7, 0, 5) };
        var submit = DeviceSettings.StyledButton("输入到设备", null, null, true);
        submit.Name = "Submit";
        submit.Enabled = false;
        submit.Anchor = AnchorStyles.Right;
        submit.Size = new Size(126, 40);
        input.TextChanged += delegate { submit.Enabled = input.TextLength != 0; };
        submit.Click += delegate
        {
            if (input.TextLength == 0 || input.Composing) return;
            string error = paste(input.Text);
            if (error == null) { Close(); return; }
            status.ForeColor = WindowTheme.Red;
            status.Text = error;
            input.Focus();
        };
        input.KeyDown += delegate(object sender, KeyEventArgs key)
        {
            if (key.KeyData != (Keys.Control | Keys.Enter) || input.Composing) return;
            key.SuppressKeyPress = true;
            submit.PerformClick();
        };
        layout.Controls.Add(hint, 0, 0);
        layout.Controls.Add(input, 0, 1);
        layout.Controls.Add(status, 0, 2);
        layout.Controls.Add(submit, 0, 3);
        Controls.Add(layout);
        Shown += delegate { input.Focus(); };
    }
}
