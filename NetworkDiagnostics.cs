using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

internal sealed class NetworkProbeResult
{
    internal string Host;
    internal int Sent;
    internal int Received;
    internal long Min;
    internal long Average;
    internal long Max;
    internal long Jitter;
    internal string Error;
    internal int LossPercent { get { return Sent == 0 ? 0 : (Sent - Received) * 100 / Sent; } }
}

internal static class NetworkDiagnostics
{
    internal static string ResolveHost(string serial)
    {
        if (String.IsNullOrWhiteSpace(serial)) return null;
        const string suffix = "._adb-tls-connect._tcp";
        if (serial.EndsWith(suffix, StringComparison.Ordinal))
        {
            string service = serial.Substring(0, serial.Length - suffix.Length);
            var endpoint = WifiPairingDialog.Services(WirelessMirror.Adb("mdns services"), "_adb-tls-connect._tcp")
                .FirstOrDefault(row => row.Key == service);
            string value = endpoint.Value;
            if (String.IsNullOrEmpty(value)) return null;
            return HostPart(value);
        }
        int colon = serial.LastIndexOf(':');
        if (colon > 0)
        {
            string host = serial.Substring(0, colon).Trim('[', ']');
            IPAddress address;
            if (IPAddress.TryParse(host, out address)) return address.ToString();
        }
        string route = WirelessMirror.Adb("-s " + serial + " shell ip route");
        var match = Regex.Match(route, @"src\s+(\d{1,3}(?:\.\d{1,3}){3})");
        return match.Success ? match.Groups[1].Value : null;
    }

    internal static NetworkProbeResult Probe(string host, int count = 6, int timeoutMs = 1000)
    {
        var result = new NetworkProbeResult { Host = host, Sent = count };
        var samples = new List<long>();
        try
        {
            using (var ping = new Ping())
            {
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var reply = ping.Send(host, timeoutMs);
                        if (reply != null && reply.Status == IPStatus.Success)
                        {
                            samples.Add(reply.RoundtripTime);
                            result.Received++;
                        }
                    }
                    catch (Exception error)
                    {
                        if (result.Error == null) result.Error = error.Message;
                    }
                }
            }
        }
        catch (Exception error)
        {
            result.Error = error.Message;
            return result;
        }
        if (samples.Count == 0)
        {
            if (result.Error == null) result.Error = "没有收到回应。";
            return result;
        }
        result.Min = samples.Min();
        result.Max = samples.Max();
        result.Average = (long)Math.Round(samples.Average());
        long jitter = 0;
        if (samples.Count > 1)
        {
            for (int i = 1; i < samples.Count; i++) jitter += Math.Abs(samples[i] - samples[i - 1]);
            jitter /= samples.Count - 1;
        }
        result.Jitter = jitter;
        return result;
    }

    private static string HostPart(string endpoint)
    {
        int colon = endpoint.LastIndexOf(':');
        if (colon <= 0) return null;
        return endpoint.Substring(0, colon).Trim('[', ']');
    }
}
