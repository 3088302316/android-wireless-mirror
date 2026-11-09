using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using QRCoder;

internal sealed class WifiPairingDialog : Form
{
    private readonly Label status = new Label();
    private readonly string service = "studio-" + RandomHex(6);
    private readonly string password = RandomHex(12);
    private readonly CancellationTokenSource stop = new CancellationTokenSource();
    private readonly PictureBox qr = new PictureBox();
    private bool pairingFinished;

    public WifiPairingDialog()
    {
        Text = "扫码配对";
        Icon = WirelessMirror.LoadIcon();
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 465);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = WindowTheme.Canvas;
        ForeColor = WindowTheme.Ink;
        Font = new Font("Microsoft YaHei UI", 10f);
        WindowTheme.Attach(this);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Text = "在手机上打开 开发者选项 → 无线调试\n点「使用二维码配对设备」，扫描下面的二维码" }, 0, 0);
        qr.Size = new Size(240, 240);
        qr.Anchor = AnchorStyles.None;
        qr.SizeMode = PictureBoxSizeMode.Zoom;
        qr.BackColor = Color.White;
        qr.Image = CreateQr("WIFI:T:ADB;S:" + service + ";P:" + password + ";;");
        layout.Controls.Add(qr, 0, 1);
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleCenter;
        status.Text = "正在等待扫描…";
        layout.Controls.Add(status, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.Add(DeviceSettings.StyledButton("取消", "CloseOutlined", delegate { Close(); }, false));
        actions.Controls.Add(DeviceSettings.StyledButton("改用六位码", "KeyOutlined", delegate { DialogResult = DialogResult.Retry; }, false));
        layout.Controls.Add(actions, 0, 3);
        Controls.Add(layout);
        Shown += BeginPairing;
        FormClosed += delegate
        {
            if (pairingFinished) stop.Dispose();
            else stop.Cancel();
            qr.Image.Dispose();
        };
    }

    private async void BeginPairing(object sender, EventArgs e)
    {
        CancellationToken token = stop.Token;
        try
        {
            string result = await Task.Run(delegate
            {
                var limit = Stopwatch.StartNew();
                while (!token.IsCancellationRequested && limit.Elapsed < TimeSpan.FromSeconds(90))
                {
                    bool quoted;
                    string endpoint = FindPairingEndpoint(WirelessMirror.Adb("mdns services"), service, out quoted);
                    if (endpoint != null && !token.IsCancellationRequested)
                    {
                        // Some HyperOS QR scanners wrap both Wi-Fi fields in literal quotes.
                        // A quoted instance name tells us to send the password in the same form.
                        string secret = quoted ? Quote(password) : password;
                        AppLog.Info("二维码发现配对服务: " + endpoint + " / " +
                            (quoted ? "带引号的 QR 字段" : "标准 QR 字段"));
                        string paired;
                        try { paired = WirelessMirror.Adb("pair " + endpoint, secret, 8000); }
                        catch (TimeoutException) { return "pair timeout"; }
                        AppLog.Info("二维码 adb pair: " + paired.Replace(secret, "<redacted>").Replace(password, "<redacted>"));
                        if (paired.Contains("Successfully paired")) DiscoverPairedPhone(endpoint, token);
                        return paired;
                    }
                    token.WaitHandle.WaitOne(600);
                }
                return String.Empty;
            });
            if (IsDisposed || token.IsCancellationRequested) return;
            if (result.Contains("Successfully paired")) { DialogResult = DialogResult.OK; return; }
            if (result.Length == 0)
                status.Text = "未发现本次扫码的配对服务。可重新打开二维码，或改用六位码。";
            else if (result == "pair timeout")
                status.Text = "已发现手机，但配对响应超时。请改用六位码。";
            else
                status.Text = "已发现手机，但 ADB 未完成配对验证。请改用六位码。";
        }
        catch (Exception)
        {
            if (!IsDisposed) status.Text = "无法完成局域网发现，请检查 Wi-Fi，或改用六位码。";
        }
        finally { pairingFinished = true; if (!Visible) stop.Dispose(); }
    }

    internal static string FindPairingEndpoint(string discovered, string service, out bool quoted)
    {
        var candidates = Services(discovered, "_adb-tls-pairing._tcp");
        string standard = candidates.FirstOrDefault(row => row.Key == service).Value;
        quoted = false;
        if (standard != null) return standard;

        string quotedService = Quote(service);
        string endpoint = candidates.FirstOrDefault(row => row.Key == quotedService).Value;
        quoted = endpoint != null;
        return endpoint;
    }

    private static string Quote(string value) { return "\"" + value + "\""; }

    internal static IEnumerable<KeyValuePair<string, string>> Services(string discovered, string type)
    {
        foreach (string line in discovered.Split('\n'))
        {
            string[] fields = Regex.Split(line.Trim(), @"\s+");
            if (fields.Length < 3 || fields[1].TrimEnd('.') != type) continue;
            string endpoint = fields[2];
            int separator = endpoint.LastIndexOf(':');
            int port;
            IPAddress address;
            if (separator > 0 && Int32.TryParse(endpoint.Substring(separator + 1), out port) && port > 0 && port <= 65535 &&
                IPAddress.TryParse(endpoint.Substring(0, separator).Trim('[', ']'), out address) &&
                Regex.IsMatch(endpoint, @"^[0-9a-fA-F.:%\[\]]+$"))
                yield return new KeyValuePair<string, string>(fields[0].TrimEnd('.'), endpoint);
        }
    }

    private static void DiscoverPairedPhone(string pairingEndpoint, CancellationToken token)
    {
        var host = IPAddress.Parse(pairingEndpoint.Substring(0, pairingEndpoint.LastIndexOf(':')).Trim('[', ']'));
        try
        {
            for (int attempt = 0; attempt < 5 && !token.WaitHandle.WaitOne(500); attempt++)
            {
                var candidate = Services(WirelessMirror.Adb("mdns services"), "_adb-tls-connect._tcp").FirstOrDefault(row =>
                    IPAddress.Parse(row.Value.Substring(0, row.Value.LastIndexOf(':')).Trim('[', ']')).Equals(host));
                if (candidate.Value == null || token.IsCancellationRequested) continue;
                string connected = WirelessMirror.Adb("connect " + candidate.Value);
                if (connected.Contains("connected to")) return;
            }
        }
        catch (TimeoutException) { /* Pairing is already complete. The main screen also supports manual connection. */ }
    }

    private static Bitmap CreateQr(string payload)
    {
        using (var generator = new QRCodeGenerator())
        using (var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M))
        using (var renderer = new PngByteQRCode(data))
        using (var stream = new MemoryStream(renderer.GetGraphic(6)))
        using (var image = Image.FromStream(stream)) return new Bitmap(image);
    }

    private static string RandomHex(int count)
    {
        var bytes = new byte[count];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
