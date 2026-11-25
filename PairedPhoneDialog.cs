using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class PairedPhoneDialog : Form
{
    internal DeviceRecord Added { get; private set; }
    private readonly PairedPhone phone;
    private readonly DeviceDirectory directory;
    private readonly Label status = new Label();
    private readonly TextBox port = new TextBox();
    private readonly AccessibleButton connect;
    private readonly CancellationTokenSource stop = new CancellationTokenSource();
    private bool busy;
    private int pendingPort;

    internal PairedPhoneDialog(PairedPhone phone, DeviceDirectory directory)
    {
        this.phone = phone;
        this.directory = directory;
        Text = "已配对 · 添加设备";
        Icon = WirelessMirror.LoadIcon();
        Font = new Font("Microsoft YaHei UI", 10f);
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = WindowTheme.Canvas;
        ForeColor = WindowTheme.Ink;
        ClientSize = new Size(520, 237);
        WindowTheme.Attach(this);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "已配对 " + phone.Host +
            "。如果设备没有自动上线，请填写手机「无线调试」主页上 IP 地址后面的连接端口。" }, 0, 0);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "无线调试连接端口" }, 0, 1);
        var line = new FlowLayoutPanel { Dock = DockStyle.Fill };
        port.MaxLength = 5;
        port.Width = 110;
        port.AccessibleName = "连接端口";
        connect = DeviceSettings.StyledButton("连接设备", null, Submit, true);
        line.Controls.Add(port);
        line.Controls.Add(connect);
        layout.Controls.Add(line, 0, 2);
        status.Dock = DockStyle.Fill;
        layout.Controls.Add(status, 0, 3);
        Controls.Add(layout);
        AcceptButton = connect;
        Shown += delegate { Run(true); };
        FormClosed += delegate { stop.Cancel(); if (!busy) stop.Dispose(); };
    }

    private void Submit()
    {
        int number;
        if (!Int32.TryParse(port.Text.Trim(), out number) || number < 1 || number > 65535)
        {
            status.Text = "连接端口范围为 1–65535。"; port.Focus(); return;
        }
        pendingPort = number;
        connect.Enabled = false;
        if (!busy) Run(false);
    }

    private async void Run(bool automatic)
    {
        busy = true;
        var token = stop.Token;
        var timer = Stopwatch.StartNew();
        try
        {
            status.Text = "正在查找刚配对的设备…";
            DeviceRecord found = null;
            while (automatic && pendingPort == 0 && timer.Elapsed < TimeSpan.FromSeconds(12))
            {
                token.ThrowIfCancellationRequested();
                try { found = await Task.Run(delegate { return phone.Discover(token); }); }
                catch (TimeoutException error) { AppLog.Warn("配对后发现超时: " + error.Message); }
                catch (System.IO.IOException error) { AppLog.Warn("配对后设备尚未就绪: " + error.Message); }
                if (found != null || pendingPort != 0) break;
                await Task.Delay(650, token);
            }
            token.ThrowIfCancellationRequested();
            if (found == null && pendingPort != 0)
            {
                int number = pendingPort;
                status.Text = "正在连接 " + phone.Host + "，端口 " + number + "…";
                found = await Task.Run(delegate { return phone.Connect(number); });
            }
            token.ThrowIfCancellationRequested();
            if (found == null)
            {
                status.Text = "配对已完成，尚未发现连接服务。填写主页上的端口即可继续。";
                port.Focus();
                return;
            }
            directory.RememberPaired(found);
            Added = found;
            AppLog.Info("已添加配对设备: " + found.Name + " / " + found.Serial);
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested) { status.Text = error.Message; AppLog.Warn("配对后添加设备: " + error.Message); }
        }
        finally
        {
            busy = false;
            pendingPort = 0;
            if (token.IsCancellationRequested) stop.Dispose();
            else if (!IsDisposed) connect.Enabled = true;
        }
    }
}
