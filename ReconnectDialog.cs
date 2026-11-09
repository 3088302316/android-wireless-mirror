using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class ReconnectDialog : Form
{
    private readonly CancellationTokenSource stop = new CancellationTokenSource();
    private readonly Label status = new Label();
    private bool finished;
    public DeviceRecord Recovered { get; private set; }

    public ReconnectDialog(DeviceDirectory directory, DeviceRecord device, string priorSerial)
    {
        Text = "正在恢复连接";
        Icon = WirelessMirror.LoadIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(450, 205);
        Font = new Font("Microsoft YaHei UI", 10f);
        BackColor = WindowTheme.Canvas;
        ForeColor = WindowTheme.Ink;
        WindowTheme.Attach(this);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20), BackColor = WindowTheme.Canvas };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(new Label
        {
            Text = "连接中断，正在重新查找「" + device.Name + "」\n只会恢复这一台设备，不会把其他手机接进来。",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = WindowTheme.Ink,
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold)
        }, 0, 0);
        status.AutoSize = false;
        status.Dock = DockStyle.Fill;
        status.ForeColor = WindowTheme.Muted;
        status.Font = new Font("Microsoft YaHei UI", 9.2f);
        status.TextAlign = ContentAlignment.TopLeft;
        status.Text = "准备重连…";
        layout.Controls.Add(status, 0, 1);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0) };
        controls.Controls.Add(DeviceSettings.StyledButton("取消", "CloseOutlined", delegate { Close(); }, false));
        layout.Controls.Add(controls, 0, 2);
        Controls.Add(layout);

        Shown += async delegate
        {
            var token = stop.Token;
            IProgress<string> progress = new Progress<string>(delegate(string message)
            {
                if (!IsDisposed) status.Text = message;
            });
            try
            {
                AppLog.Info("开始恢复连接: " + device.Name + " / " + priorSerial);
                var match = await Task.Run(delegate
                {
                    int[] delays = { 500, 700, 900, 1200, 1600, 2200, 3000, 3000, 3000, 3000 };
                    for (int attempt = 0; attempt < delays.Length && !token.IsCancellationRequested; attempt++)
                    {
                        progress.Report("第 " + (attempt + 1) + " / " + delays.Length + " 次：正在查找设备…");
                        if (attempt == 0 || attempt % 2 == 1) TryReconnectTransport(priorSerial);
                        var same = directory.Scan().FirstOrDefault(row => row.Id == device.Id && row.Online);
                        if (same != null && !token.IsCancellationRequested)
                        {
                            progress.Report("已找到设备，正在恢复投屏…");
                            return same;
                        }
                        if (token.WaitHandle.WaitOne(delays[attempt])) break;
                    }
                    return null;
                });
                if (!IsDisposed && !token.IsCancellationRequested)
                {
                    Recovered = match;
                    AppLog.Info(match == null ? "自动重连未成功" : "自动重连成功: " + match.Serial);
                    Close();
                }
            }
            catch (Exception error)
            {
                AppLog.Error("自动重连异常", error);
                if (!IsDisposed) Close();
            }
            finally { finished = true; if (!Visible) stop.Dispose(); }
        };
        var timeout = new System.Windows.Forms.Timer { Interval = 18000 };
        timeout.Tick += delegate { timeout.Stop(); if (!IsDisposed) Close(); };
        Shown += delegate { timeout.Start(); };
        FormClosed += delegate
        {
            timeout.Dispose();
            if (finished) stop.Dispose();
            else stop.Cancel();
        };
    }

    private static void TryReconnectTransport(string priorSerial)
    {
        if (!DeviceDirectory.IsWirelessSerial(priorSerial)) return;
        const string suffix = "._adb-tls-connect._tcp";
        string address = priorSerial;
        if (priorSerial.EndsWith(suffix, StringComparison.Ordinal))
        {
            string name = priorSerial.Substring(0, priorSerial.Length - suffix.Length);
            address = WifiPairingDialog.Services(WirelessMirror.Adb("mdns services"), "_adb-tls-connect._tcp")
                .FirstOrDefault(row => row.Key == name).Value;
        }
        if (String.IsNullOrEmpty(address)) return;
        try
        {
            string result = WirelessMirror.Adb("connect " + address, null, 5000);
            AppLog.Info("重连传输: " + address + " / " + result.Replace("\n", " "));
        }
        catch (Exception error)
        {
            AppLog.Warn("重连传输失败: " + address + " / " + error.Message);
        }
    }
}
