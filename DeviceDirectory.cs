using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

[DataContract]
internal sealed class DeviceRecord
{
    [DataMember] public string Id;
    [DataMember] public string Model;
    [DataMember] public string Alias;
    [DataMember(EmitDefaultValue = false)] public DateTime LastUsed;
    [DataMember] public int Connections;
    [DataMember] public List<DateTime> RecentConnections;
    public string Serial;
    public bool Online { get { return !String.IsNullOrEmpty(Serial); } }
    public string Name { get { return String.IsNullOrEmpty(Alias) ? Model : Alias; } }
}

internal sealed class DeviceDirectory
{
    private const string MdnsSuffix = "._adb-tls-connect._tcp";
    private readonly DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<DeviceRecord>));
    private readonly object gate = new object();
    private readonly List<DeviceRecord> saved;

    public DeviceDirectory()
    {
        try
        {
            if (File.Exists(AppPaths.DevicesFile))
            {
                using (var stream = File.OpenRead(AppPaths.DevicesFile))
                    saved = (List<DeviceRecord>)serializer.ReadObject(stream);
            }
            else saved = new List<DeviceRecord>();
            if (saved == null) saved = new List<DeviceRecord>();
        }
        catch (Exception error)
        {
            AppLog.Error("读取设备历史失败，本次从空列表开始", error);
            saved = new List<DeviceRecord>();
        }
    }

    public List<DeviceRecord> Scan()
    {
        List<DeviceRecord> rows;
        lock (gate)
        {
            rows = saved.Select(record => new DeviceRecord
            {
                Id = record.Id,
                Alias = record.Alias,
                Model = record.Model,
                LastUsed = record.LastUsed,
                Connections = record.Connections,
                RecentConnections = record.RecentConnections == null ? null : new List<DateTime>(record.RecentConnections)
            }).ToList();
        }

        var online = new List<string>();
        foreach (string line in WirelessMirror.Adb("devices -l").Split('\n'))
        {
            var fields = Regex.Split(line.Trim(), @"\s+");
            if (fields.Length >= 2 && fields[1] == "device" && IsWirelessSerial(fields[0]))
                online.Add(fields[0]);
        }
        var onlineSet = new HashSet<string>(online, StringComparer.Ordinal);
        Dictionary<string, List<string>> mdns = null;
        // Process direct IP transports first so an mDNS peer does not pull us onto a VPN/tunnel address.
        online.Sort(delegate(string left, string right)
        {
            bool leftMdns = left.EndsWith(MdnsSuffix, StringComparison.Ordinal);
            bool rightMdns = right.EndsWith(MdnsSuffix, StringComparison.Ordinal);
            if (leftMdns == rightMdns) return 0;
            return leftMdns ? 1 : -1;
        });

        foreach (string originalSerial in online)
        {
            bool originalIsMdns = originalSerial.EndsWith(MdnsSuffix, StringComparison.Ordinal);
            string id = null;
            if (originalIsMdns)
            {
                try { id = ReadId(originalSerial); }
                catch (Exception error) { AppLog.Error("读取 mDNS 设备身份失败: " + originalSerial, error); }
                if (id != null)
                {
                    var direct = rows.FirstOrDefault(item => item.Id == id && item.Serial != null &&
                        !item.Serial.EndsWith(MdnsSuffix, StringComparison.Ordinal));
                    if (direct != null)
                    {
                        AppLog.Info("已有 IP 直连，跳过 mDNS 转换: " + direct.Serial);
                        continue;
                    }
                }
            }
            string serial = DirectSerial(originalSerial, onlineSet, ref mdns);
            if (id != null && serial != originalSerial)
            {
                try { if (ReadId(serial) != id) serial = originalSerial; }
                catch (Exception error)
                {
                    AppLog.Warn("无法确认直连设备身份，沿用 mDNS: " + error.Message);
                    serial = originalSerial;
                }
            }
            if (id == null)
            {
                try { id = ReadId(serial); }
                catch (Exception error) { AppLog.Error("读取设备身份失败: " + serial, error); }
            }
            if (id == null && serial != originalSerial)
            {
                serial = originalSerial;
                try { id = ReadId(serial); }
                catch (Exception error) { AppLog.Error("读取 mDNS 设备身份失败: " + serial, error); }
            }

            var record = id == null ? null : rows.FirstOrDefault(item => item.Id == id);
            if (record == null)
            {
                string model = null;
                try
                {
                    model = Property(serial, "ro.product.marketname");
                    if (String.IsNullOrWhiteSpace(model)) model = Property(serial, "ro.product.model");
                }
                catch (Exception error) { AppLog.Warn("读取设备型号失败: " + serial + " " + error.Message); }
                record = new DeviceRecord { Id = id, Model = String.IsNullOrWhiteSpace(model) ? "安卓设备" : model };
                rows.Add(record);
            }

            bool currentIsMdns = record.Serial != null && record.Serial.EndsWith(MdnsSuffix, StringComparison.Ordinal);
            bool candidateIsMdns = serial.EndsWith(MdnsSuffix, StringComparison.Ordinal);
            if (String.IsNullOrEmpty(record.Serial) || (currentIsMdns && !candidateIsMdns))
                record.Serial = serial;
        }

        return rows.OrderByDescending(item => item.Online).ThenByDescending(item => item.LastUsed)
            .ThenBy(item => item.Name).ToList();
    }

    private static string DirectSerial(string serial, HashSet<string> online, ref Dictionary<string, List<string>> mdns)
    {
        if (!serial.EndsWith(MdnsSuffix, StringComparison.Ordinal)) return serial;
        if (mdns == null)
        {
            mdns = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            try
            {
                foreach (var row in WifiPairingDialog.Services(WirelessMirror.Adb("mdns services"), "_adb-tls-connect._tcp"))
                {
                    List<string> endpoints;
                    if (!mdns.TryGetValue(row.Key, out endpoints))
                    {
                        endpoints = new List<string>();
                        mdns[row.Key] = endpoints;
                    }
                    if (!endpoints.Contains(row.Value)) endpoints.Add(row.Value);
                }
            }
            catch (Exception error) { AppLog.Warn("读取 mDNS 服务失败: " + error.Message); }
        }
        string service = serial.Substring(0, serial.Length - MdnsSuffix.Length);
        List<string> candidates;
        if (!mdns.TryGetValue(service, out candidates) || candidates.Count == 0) return serial;
        int firstColon = candidates[0].LastIndexOf(':');
        if (firstColon > 0)
        {
            string port = candidates[0].Substring(firstColon);
            string derived = ResolveWifiEndpoint(serial, port);
            if (derived != null)
            {
                try
                {
                    if (TryConnectEndpoint(derived, online))
                    {
                        AppLog.Info("从手机路由解析到 Wi-Fi 直连: " + derived);
                        return derived;
                    }
                }
                catch (Exception error) { AppLog.Warn("Wi-Fi 直连失败: " + derived + " " + error.Message); }
            }
            string samePort = online.FirstOrDefault(value =>
                !value.EndsWith(MdnsSuffix, StringComparison.Ordinal) && value.EndsWith(port, StringComparison.Ordinal));
            if (samePort != null)
            {
                try
                {
                    if (WirelessMirror.Adb("-s " + samePort + " get-state", null, 2500) == "device")
                    {
                        AppLog.Info("优先使用同端口 IP 直连: " + samePort);
                        return samePort;
                    }
                }
                catch (Exception error) { AppLog.Warn("同端口直连检查失败: " + samePort + " " + error.Message); }
            }
        }
        foreach (string endpoint in candidates.OrderByDescending(value => EndpointScore(value, online)))
        {
            if (online.Contains(endpoint))
            {
                try
                {
                    if (WirelessMirror.Adb("-s " + endpoint + " get-state", null, 2500) == "device")
                    {
                        AppLog.Info("优先使用 IP 直连: " + endpoint);
                        return endpoint;
                    }
                }
                catch (Exception error) { AppLog.Warn("直连状态检查失败: " + endpoint + " " + error.Message); }
                continue;
            }
            try
            {
                WirelessMirror.Adb("connect " + endpoint, null, 2500);
                if (WirelessMirror.Adb("-s " + endpoint + " get-state", null, 2500) == "device")
                {
                    online.Add(endpoint);
                    AppLog.Info("优先使用 IP 直连: " + endpoint);
                    return endpoint;
                }
            }
            catch (Exception error) { AppLog.Warn("mDNS 转直连失败: " + endpoint + " " + error.Message); }
        }
        return serial;
    }

    private static string ResolveWifiEndpoint(string serial, string port)
    {
        try
        {
            string route = WirelessMirror.Adb("-s " + serial + " shell ip route", null, 4000);
            string best = null;
            int bestScore = Int32.MinValue;
            foreach (Match match in Regex.Matches(route, @"src\s+(\d{1,3}(?:\.\d{1,3}){3})"))
            {
                string host = match.Groups[1].Value;
                int score = PrivateScore(host);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = host;
                }
            }
            return bestScore > 0 && best != null ? best + port : null;
        }
        catch (Exception error)
        {
            AppLog.Warn("解析手机 Wi-Fi IP 失败: " + error.Message);
            return null;
        }
    }

    private static bool TryConnectEndpoint(string endpoint, HashSet<string> online)
    {
        if (!online.Contains(endpoint))
            WirelessMirror.Adb("connect " + endpoint, null, 2500);
        if (WirelessMirror.Adb("-s " + endpoint + " get-state", null, 2500) != "device") return false;
        online.Add(endpoint);
        return true;
    }

    private static int PrivateScore(string host)
    {
        System.Net.IPAddress address;
        if (!System.Net.IPAddress.TryParse(host, out address)) return -1000;
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return -1000;
        if (bytes[0] == 192 && bytes[1] == 168) return 300;
        if (bytes[0] == 10) return 250;
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return 240;
        if (bytes[0] == 169 && bytes[1] == 254) return -200;
        return 0;
    }

    private static int EndpointScore(string endpoint, HashSet<string> online)
    {
        if (online.Contains(endpoint)) return 1000;
        int colon = endpoint.LastIndexOf(':');
        if (colon <= 0) return 0;
        string host = endpoint.Substring(0, colon).Trim('[', ']');
        System.Net.IPAddress address;
        if (!System.Net.IPAddress.TryParse(host, out address)) return 0;
        if (System.Net.IPAddress.IsLoopback(address)) return -1000;
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4) return 0;
        if (bytes[0] == 169 && bytes[1] == 254) return -800;
        if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return -500;
        if (bytes[0] == 192 && bytes[1] == 168) return 300;
        if (bytes[0] == 10) return 250;
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return 240;
        return 100;
    }

    public static bool IsWirelessSerial(string serial)
    {
        return Regex.IsMatch(serial, @"^[a-zA-Z0-9_.:%\[\]-]+$") &&
            (serial.EndsWith(MdnsSuffix, StringComparison.Ordinal) || Regex.IsMatch(serial, @":\d+$"));
    }

    public static string ReadId(string serial)
    {
        string value = Property(serial, "ro.serialno");
        if (String.IsNullOrWhiteSpace(value) || value == "unknown" || value == "0123456789ABCDEF")
        {
            value = WirelessMirror.Adb("-s " + serial + " shell settings get secure android_id");
            if (!Regex.IsMatch(value, @"^[a-fA-F0-9]{16}$")) return null;
            value = "android-id:" + value;
        }
        else value = "serial:" + value;
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string Property(string serial, string name)
    {
        return WirelessMirror.Adb("-s " + serial + " shell getprop " + name);
    }

    public void Rename(DeviceRecord record, string alias)
    {
        lock (gate)
        {
            record.Alias = alias;
            var current = GetSaved(record);
            current.Alias = alias;
            SaveLocked();
        }
    }

    public void RememberConnection(DeviceRecord record)
    {
        if (record.Id == null) return;
        lock (gate)
        {
            var current = GetSaved(record);
            current.LastUsed = DateTime.UtcNow;
            current.Connections++;
            if (current.RecentConnections == null) current.RecentConnections = new List<DateTime>();
            current.RecentConnections.Insert(0, current.LastUsed);
            if (current.RecentConnections.Count > 30) current.RecentConnections.RemoveAt(30);
            record.LastUsed = current.LastUsed;
            record.Connections = current.Connections;
            record.RecentConnections = new List<DateTime>(current.RecentConnections);
            SaveLocked();
        }
    }

    private DeviceRecord GetSaved(DeviceRecord record)
    {
        var current = saved.FirstOrDefault(item => item.Id == record.Id);
        if (current != null) return current;
        current = new DeviceRecord { Id = record.Id, Model = record.Model, Alias = record.Alias };
        saved.Add(current);
        return current;
    }

    private void SaveLocked()
    {
        AppPaths.EnsureDataDirectory();
        string temp = AppPaths.DevicesFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                serializer.WriteObject(stream, saved);
            if (File.Exists(AppPaths.DevicesFile))
            {
                try { File.Replace(temp, AppPaths.DevicesFile, null); }
                catch (IOException)
                {
                    File.Copy(temp, AppPaths.DevicesFile, true);
                    File.Delete(temp);
                }
            }
            else File.Move(temp, AppPaths.DevicesFile);
        }
        catch (Exception error)
        {
            AppLog.Error("保存设备历史失败", error);
            throw;
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch (Exception) { }
            }
        }
    }
}
