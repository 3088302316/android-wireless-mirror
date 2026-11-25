using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;

internal sealed class PairedPhone
{
    private const string Service = "_adb-tls-connect._tcp";
    private readonly string guid;
    private readonly int pairingPort;
    internal readonly IPAddress Host;

    internal PairedPhone(string endpoint, string pairReply)
    {
        int split = endpoint.LastIndexOf(':');
        Host = IPAddress.Parse(endpoint.Substring(0, split).Trim('[', ']'));
        pairingPort = Int32.Parse(endpoint.Substring(split + 1));
        guid = Regex.Match(pairReply, @"\[guid=([a-zA-Z0-9_-]+)\]").Groups[1].Value;
    }

    private string Endpoint(int port) { return new IPEndPoint(Host, port).ToString(); }

    internal DeviceRecord Connect(int port)
    {
        if (port == pairingPort) throw new IOException("这是配对端口，请填写无线调试主页上的连接端口。");
        string serial = Endpoint(port);
        string response = WirelessMirror.Adb("connect " + serial, null, 3000);
        var record = ReadOnline(serial);
        if (record == null)
            throw new IOException("端口 " + port + " 暂未连接。请核对无线调试主页上的端口。ADB：" + response.Trim());
        return record;
    }

    internal DeviceRecord Discover(CancellationToken token)
    {
        var transports = DeviceDirectory.WirelessTransports();
        var targets = new List<string>();
        if (guid.Length != 0 && transports.Contains(guid + "." + Service))
            targets.Add(guid + "." + Service);
        foreach (string serial in transports)
        {
            int split = serial.LastIndexOf(':');
            IPAddress address;
            if (split > 0 && IPAddress.TryParse(serial.Substring(0, split).Trim('[', ']'), out address) && Host.Equals(address))
                targets.Add(serial);
        }
        foreach (string target in targets)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var row = ReadOnline(target);
                if (row != null) return row;
            }
            catch (TimeoutException error) { AppLog.Warn("配对设备暂未响应: " + error.Message); }
            catch (IOException error) { AppLog.Warn("配对设备暂未就绪: " + error.Message); }
        }
        var entries = WifiPairingDialog.Services(WirelessMirror.Adb("mdns services", null, 2500), Service);
        var ports = new HashSet<int>();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            int split = entry.Value.LastIndexOf(':');
            var host = IPAddress.Parse(entry.Value.Substring(0, split).Trim('[', ']'));
            if (guid.Length == 0 ? !Host.Equals(host) : entry.Key.Trim('"') != guid) continue;
            int port = Int32.Parse(entry.Value.Substring(split + 1));
            if (port == pairingPort || !ports.Add(port)) continue;
            // A phone may advertise its VPN address. Use the LAN host that completed pairing.
            try { return Connect(port); }
            catch (TimeoutException error) { AppLog.Warn("配对后连接端口超时: " + error.Message); }
            catch (IOException error) { AppLog.Warn("配对后连接端口暂不可用: " + error.Message); }
        }
        return null;
    }

    private static DeviceRecord ReadOnline(string serial)
    {
        if (WirelessMirror.Adb("-s " + serial + " get-state", null, 2500) != "device") return null;
        string id = DeviceDirectory.ReadId(serial);
        if (id == null) return null;
        string model = AdbClient.Run("-s " + serial + " shell getprop ro.product.marketname", null, 2500, true);
        if (String.IsNullOrWhiteSpace(model))
            model = AdbClient.Run("-s " + serial + " shell getprop ro.product.model", null, 2500, true);
        return new DeviceRecord { Id = id, Serial = serial, Model = String.IsNullOrWhiteSpace(model) ? "安卓设备" : model };
    }
}
